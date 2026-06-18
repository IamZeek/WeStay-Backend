namespace WeStay.Tests
{
    // Host confirm/reject of bookings: ownership enforced via ListingService, only while Pending.
    public class BookingApprovalTests
    {
        // Sets up a host who owns a listing, and a separate guest who has a Pending booking on it.
        private static async Task<(string hostToken, int bookingId, string bookerToken)> SetupPendingBookingAsync(ApiClient api)
        {
            var (_, _, hostReg) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, hostReg);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm, guests: 4);

            var (_, _, bookerToken) = await Flows.RegisterAsync(api);
            var bookingId = await Flows.CreateBookingAsync(api, bookerToken, listing.Id, numberOfGuests: 2);
            return (hostToken, bookingId, bookerToken);
        }

        // ---- Confirm ----

        [Fact]
        public async Task Host_CanConfirm_OwnListingBooking_Succeeds()
        {
            using var api = new ApiClient();
            var (hostToken, bookingId, _) = await SetupPendingBookingAsync(api);

            var response = await api.PostAsync($"/api/bookings/{bookingId}/confirm", null, hostToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await Json.ReadAsync<BookingActionResponse>(response);
            Assert.Equal("Confirmed", result.Booking.Status);
        }

        [Fact]
        public async Task Host_CannotConfirm_OtherHostsListingBooking_Returns403()
        {
            using var api = new ApiClient();
            var (_, bookingId, _) = await SetupPendingBookingAsync(api);

            // A different host who does not own the listing.
            var (_, _, otherReg) = await Flows.RegisterAsync(api);
            var otherHost = await Flows.BecomeHostAsync(api, otherReg);

            var response = await api.PostAsync($"/api/bookings/{bookingId}/confirm", null, otherHost);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Confirm_AlreadyCancelledBooking_ReturnsClearError_Not500()
        {
            using var api = new ApiClient();
            var (hostToken, bookingId, bookerToken) = await SetupPendingBookingAsync(api);

            // The booker cancels first.
            var cancel = await api.PostAsync($"/api/bookings/{bookingId}/cancel", new { Reason = "changed plans" }, bookerToken);
            cancel.EnsureSuccessStatusCode();

            var response = await api.PostAsync($"/api/bookings/{bookingId}/confirm", null, hostToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // clear 400, not a generic 500
        }

        // ---- Reject ----

        [Fact]
        public async Task Host_CanReject_OwnListingBooking_Succeeds()
        {
            using var api = new ApiClient();
            var (hostToken, bookingId, _) = await SetupPendingBookingAsync(api);

            var response = await api.PostAsync($"/api/bookings/{bookingId}/reject", new { Reason = "Dates no longer available" }, hostToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await Json.ReadAsync<BookingActionResponse>(response);
            Assert.Equal("Rejected", result.Booking.Status);
        }

        [Fact]
        public async Task Host_CannotReject_OtherHostsListingBooking_Returns403()
        {
            using var api = new ApiClient();
            var (_, bookingId, _) = await SetupPendingBookingAsync(api);

            var (_, _, otherReg) = await Flows.RegisterAsync(api);
            var otherHost = await Flows.BecomeHostAsync(api, otherReg);

            var response = await api.PostAsync($"/api/bookings/{bookingId}/reject", new { Reason = "not mine" }, otherHost);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Reject_AlreadyCancelledBooking_ReturnsClearError_Not500()
        {
            using var api = new ApiClient();
            var (hostToken, bookingId, bookerToken) = await SetupPendingBookingAsync(api);

            var cancel = await api.PostAsync($"/api/bookings/{bookingId}/cancel", new { Reason = "changed plans" }, bookerToken);
            cancel.EnsureSuccessStatusCode();

            var response = await api.PostAsync($"/api/bookings/{bookingId}/reject", new { Reason = "too late" }, hostToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
