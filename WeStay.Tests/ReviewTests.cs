namespace WeStay.Tests
{
    // Reviews: only the guest of a Completed booking may review, once per booking.
    public class ReviewTests
    {
        // Host confirms then completes a booking (the only path to Completed today).
        private static async Task CompleteBookingAsync(ApiClient api, string hostToken, int bookingId)
        {
            (await api.PostAsync($"/api/bookings/{bookingId}/confirm", null, hostToken)).EnsureSuccessStatusCode();
            (await api.PostAsync($"/api/bookings/{bookingId}/complete", null, hostToken)).EnsureSuccessStatusCode();
        }

        // Host + listing + a guest with a COMPLETED booking on it.
        private static async Task<(int listingId, int bookingId, string guestToken)> SetupCompletedBookingAsync(ApiClient api, int dayOffset = 7)
        {
            var (_, _, hostReg) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, hostReg);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm, guests: 4);

            var (_, _, guestToken) = await Flows.RegisterAsync(api);
            var bookingId = await Flows.CreateBookingAsync(api, guestToken, listing.Id, numberOfGuests: 2, checkInOffsetDays: dayOffset);
            await CompleteBookingAsync(api, hostToken, bookingId);

            return (listing.Id, bookingId, guestToken);
        }

        // A new guest gets a completed booking on the given listing and leaves a review.
        private static async Task ReviewListingOnceAsync(ApiClient api, string hostToken, int listingId, int dayOffset, int rating)
        {
            var (_, _, guestToken) = await Flows.RegisterAsync(api);
            var bookingId = await Flows.CreateBookingAsync(api, guestToken, listingId, numberOfGuests: 2, checkInOffsetDays: dayOffset);
            await CompleteBookingAsync(api, hostToken, bookingId);
            (await api.PostAsync("/api/reviews", new { BookingId = bookingId, Rating = rating, Comment = "ok" }, guestToken))
                .EnsureSuccessStatusCode();
        }

        [Fact]
        public async Task Guest_CanReview_CompletedBooking_Succeeds()
        {
            using var api = new ApiClient();
            var (_, bookingId, guestToken) = await SetupCompletedBookingAsync(api);

            var response = await api.PostAsync("/api/reviews",
                new { BookingId = bookingId, Rating = 5, Comment = "Great stay" }, guestToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var review = await Json.ReadAsync<ReviewDto>(response);
            Assert.Equal(5, review.Rating);
            Assert.Equal(bookingId, review.BookingId);
        }

        [Fact]
        public async Task Cannot_Review_NonCompletedBooking_Returns400()
        {
            using var api = new ApiClient();
            // A Pending booking (never confirmed/completed).
            var (_, _, hostReg) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, hostReg);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm, guests: 4);
            var (_, _, guestToken) = await Flows.RegisterAsync(api);
            var bookingId = await Flows.CreateBookingAsync(api, guestToken, listing.Id, numberOfGuests: 2);

            var response = await api.PostAsync("/api/reviews",
                new { BookingId = bookingId, Rating = 4, Comment = "too soon" }, guestToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Cannot_Review_OthersBooking_Returns403()
        {
            using var api = new ApiClient();
            var (_, bookingId, _) = await SetupCompletedBookingAsync(api);

            // A different guest (not the booking's guest) tries to review it.
            var (_, _, otherGuest) = await Flows.RegisterAsync(api);
            var response = await api.PostAsync("/api/reviews",
                new { BookingId = bookingId, Rating = 4, Comment = "not mine" }, otherGuest);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task Cannot_DoubleReview_SameBooking_Returns409()
        {
            using var api = new ApiClient();
            var (_, bookingId, guestToken) = await SetupCompletedBookingAsync(api);

            (await api.PostAsync("/api/reviews", new { BookingId = bookingId, Rating = 5, Comment = "first" }, guestToken))
                .EnsureSuccessStatusCode();

            var second = await api.PostAsync("/api/reviews", new { BookingId = bookingId, Rating = 3, Comment = "again" }, guestToken);

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        [Fact]
        public async Task ListingSummary_ReturnsCorrectAverage()
        {
            using var api = new ApiClient();
            var (_, _, hostReg) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, hostReg);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm, guests: 4);

            // Two completed bookings (non-overlapping dates) by two guests, rated 5 and 3 → avg 4.0.
            await ReviewListingOnceAsync(api, hostToken, listing.Id, dayOffset: 7, rating: 5);
            await ReviewListingOnceAsync(api, hostToken, listing.Id, dayOffset: 20, rating: 3);

            var response = await api.GetAsync($"/api/reviews/listing/{listing.Id}/summary");
            response.EnsureSuccessStatusCode();

            var summary = await Json.ReadAsync<ReviewSummaryDto>(response);
            Assert.Equal(2, summary.ReviewCount);
            Assert.Equal(4.0, summary.AverageRating);
        }
    }
}
