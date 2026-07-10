using System.Security.Cryptography;
using System.Text;

namespace WeStay.Tests
{
    // SafePay payment tests. The always-run tests exercise validation + webhook signature rejection
    // without needing SafePay creds. Tests that need a real tracker soft-skip (early return) when
    // SafePay isn't configured on BookingService; the valid-signature test additionally needs a
    // shared dev Safepay:WebhookSecret in both the service and the test config.
    public class PaymentTests
    {
        private static async Task<(int bookingId, string guestToken, string hostToken)> ConfirmedBookingAsync(ApiClient api)
        {
            var (_, _, hostReg) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, hostReg);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm);
            var (_, _, guestToken) = await Flows.RegisterAsync(api);
            var bookingId = await Flows.CreateBookingAsync(api, guestToken, listing.Id);
            (await api.PostAsync($"/api/bookings/{bookingId}/confirm", null, hostToken)).EnsureSuccessStatusCode();
            return (bookingId, guestToken, hostToken);
        }

        private static string HmacHex(string message, string key)
        {
            using var h = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
        }

        private static async Task<bool> NotConfigured(HttpResponseMessage resp) =>
            resp.StatusCode == HttpStatusCode.BadRequest &&
            (await resp.Content.ReadAsStringAsync()).Contains("not configured", StringComparison.OrdinalIgnoreCase);

        // ===== Always run (no SafePay creds needed) =====

        [Fact]
        public async Task Webhook_BadSignature_Rejected()
        {
            using var api = new ApiClient();
            var resp = await api.PostAsync("/api/payments/webhook",
                new { tracker = "track_test", status = "success", signature = "not-a-valid-signature" });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Webhook_MissingTrackerOrSignature_BadRequest()
        {
            using var api = new ApiClient();
            var resp = await api.PostAsync("/api/payments/webhook", new { });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Initiate_UnconfirmedBooking_Rejected()
        {
            using var api = new ApiClient();
            var (_, _, hostReg) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, hostReg);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm);
            var (_, _, guestToken) = await Flows.RegisterAsync(api);
            var bookingId = await Flows.CreateBookingAsync(api, guestToken, listing.Id); // stays Pending

            var resp = await api.PostAsync("/api/payments/initiate", new { bookingId }, guestToken);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Contains("Confirmed", await resp.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Initiate_NotBookingOwner_Forbidden()
        {
            using var api = new ApiClient();
            var (bookingId, _, _) = await ConfirmedBookingAsync(api);
            var (_, _, otherGuest) = await Flows.RegisterAsync(api);

            var resp = await api.PostAsync("/api/payments/initiate", new { bookingId }, otherGuest);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // ===== Gated: soft-skip unless SafePay is configured on the service =====

        [Fact]
        public async Task Initiate_ConfirmedBooking_ReturnsCheckoutUrl()
        {
            using var api = new ApiClient();
            var (bookingId, guestToken, _) = await ConfirmedBookingAsync(api);

            var resp = await api.PostAsync("/api/payments/initiate", new { bookingId }, guestToken);
            if (await NotConfigured(resp)) return;
            resp.EnsureSuccessStatusCode();

            var init = await Json.ReadAsync<InitiateResponse>(resp);
            Assert.False(string.IsNullOrEmpty(init.CheckoutUrl));
            Assert.Contains("beacon=", init.CheckoutUrl);

            var view = await Json.ReadAsync<PaymentView>(await api.GetAsync($"/api/payments/booking/{bookingId}", guestToken));
            Assert.Equal("Pending", view.Status);
        }

        [Fact]
        public async Task Refund_PendingPayment_RejectedIllegalTransition()
        {
            using var api = new ApiClient();
            var (bookingId, guestToken, _) = await ConfirmedBookingAsync(api);
            var initResp = await api.PostAsync("/api/payments/initiate", new { bookingId }, guestToken);
            if (await NotConfigured(initResp)) return;
            initResp.EnsureSuccessStatusCode();
            var init = await Json.ReadAsync<InitiateResponse>(initResp);

            // Only Paid / HeldForStay are refundable — a Pending payment must be rejected.
            var refund = await api.PostAsync($"/api/payments/{init.PaymentId}/refund", null, guestToken);
            Assert.Equal(HttpStatusCode.BadRequest, refund.StatusCode);
            Assert.Contains("cannot be refunded", await refund.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task MarkPaidOut_NonReleasablePayment_Rejected()
        {
            using var api = new ApiClient();
            var (bookingId, guestToken, _) = await ConfirmedBookingAsync(api);
            var initResp = await api.PostAsync("/api/payments/initiate", new { bookingId }, guestToken);
            if (await NotConfigured(initResp)) return;
            initResp.EnsureSuccessStatusCode();
            var init = await Json.ReadAsync<InitiateResponse>(initResp);

            var adminToken = await Flows.LoginAdminAsync(api);
            var resp = await api.PostAsync($"/api/admin/payments/{init.PaymentId}/mark-paid-out", null, adminToken);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode); // Pending, not ReleasableToHost
        }

        // ===== Gated: soft-skip unless the shared dev webhook secret is configured =====

        [Fact]
        public async Task Webhook_ValidSignature_ProcessedOnceThenIdempotent()
        {
            if (string.IsNullOrEmpty(TestConfig.SafepayWebhookSecret)) return;

            using var api = new ApiClient();
            var (bookingId, guestToken, _) = await ConfirmedBookingAsync(api);
            var initResp = await api.PostAsync("/api/payments/initiate", new { bookingId }, guestToken);
            if (await NotConfigured(initResp)) return;
            initResp.EnsureSuccessStatusCode();
            var init = await Json.ReadAsync<InitiateResponse>(initResp);

            var payload = new
            {
                tracker = init.Tracker,
                status = "success",
                signature = HmacHex(init.Tracker, TestConfig.SafepayWebhookSecret)
            };

            (await api.PostAsync("/api/payments/webhook", payload)).EnsureSuccessStatusCode();
            var afterFirst = await Json.ReadAsync<PaymentView>(await api.GetAsync($"/api/payments/booking/{bookingId}", guestToken));
            Assert.Equal("HeldForStay", afterFirst.Status);

            // Same webhook again → idempotent (still exactly HeldForStay, not re-processed).
            (await api.PostAsync("/api/payments/webhook", payload)).EnsureSuccessStatusCode();
            var afterSecond = await Json.ReadAsync<PaymentView>(await api.GetAsync($"/api/payments/booking/{bookingId}", guestToken));
            Assert.Equal("HeldForStay", afterSecond.Status);
        }
    }
}
