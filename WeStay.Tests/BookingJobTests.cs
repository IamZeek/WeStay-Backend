namespace WeStay.Tests
{
    // BookingService background jobs: auto-complete (Confirmed past checkout → Completed) and
    // auto-cancel (Pending past the confirm window → Cancelled). Triggered via the ops endpoints
    // with an `asOf` override so the test doesn't have to wait real time.
    public class BookingJobTests
    {
        private static async Task<string> GetStatusAsync(ApiClient api, int bookingId)
        {
            // /info is internal (service-to-service): requires the shared internal service key.
            var info = await Json.ReadAsync<BookingStatusInfo>(
                await api.GetAsync($"/api/bookings/{bookingId}/info", internalKey: TestConfig.InternalApiKey));
            return info.Status;
        }

        [Fact]
        public async Task AutoComplete_PastCheckoutConfirmedBooking_BecomesCompleted()
        {
            using var api = new ApiClient();
            var (_, _, hostReg) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, hostReg);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm, guests: 4);

            var (_, _, guestToken) = await Flows.RegisterAsync(api);
            // Earliest-possible stay (checkout ~ today+4). Confirm it.
            var bookingId = await Flows.CreateBookingAsync(api, guestToken, listing.Id, numberOfGuests: 2, checkInOffsetDays: 1);
            (await api.PostAsync($"/api/bookings/{bookingId}/confirm", null, hostToken)).EnsureSuccessStatusCode();
            Assert.Equal("Confirmed", await GetStatusAsync(api, bookingId));

            // The job endpoints are Admin-only.
            var adminToken = await Flows.LoginAdminAsync(api);

            // Run auto-complete as if "now" were a week from today (so the checkout is in the past).
            var asOf = Uri.EscapeDataString(DateTime.UtcNow.Date.AddDays(7).ToString("s"));
            (await api.PostAsync($"/api/bookings/jobs/auto-complete?asOf={asOf}", null, adminToken)).EnsureSuccessStatusCode();

            Assert.Equal("Completed", await GetStatusAsync(api, bookingId));
        }

        [Fact]
        public async Task AutoCancel_StalePendingBooking_BecomesCancelled()
        {
            using var api = new ApiClient();
            var (_, _, hostReg) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, hostReg);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm, guests: 4);

            var (_, _, guestToken) = await Flows.RegisterAsync(api);
            // Stays Pending (host never confirms).
            var bookingId = await Flows.CreateBookingAsync(api, guestToken, listing.Id, numberOfGuests: 2);
            Assert.Equal("Pending", await GetStatusAsync(api, bookingId));

            // The job endpoints are Admin-only.
            var adminToken = await Flows.LoginAdminAsync(api);

            // Run auto-cancel as if "now" were 48h later (past the 24h confirm window).
            var asOf = Uri.EscapeDataString(DateTime.UtcNow.AddHours(48).ToString("s"));
            (await api.PostAsync($"/api/bookings/jobs/auto-cancel?asOf={asOf}", null, adminToken)).EnsureSuccessStatusCode();

            Assert.Equal("Cancelled", await GetStatusAsync(api, bookingId));
        }

        [Fact]
        public async Task JobEndpoints_RejectNonAdmin_Return403()
        {
            using var api = new ApiClient();
            // A freshly-registered user is a Guest — must not be able to trigger the batch jobs.
            var (_, _, guestToken) = await Flows.RegisterAsync(api);

            var complete = await api.PostAsync("/api/bookings/jobs/auto-complete", null, guestToken);
            Assert.Equal(HttpStatusCode.Forbidden, complete.StatusCode);

            var cancel = await api.PostAsync("/api/bookings/jobs/auto-cancel", null, guestToken);
            Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);
        }
    }
}
