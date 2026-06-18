namespace WeStay.Tests
{
    // Priority 5: booking flow — availability calendar grid + guest-capacity validation.
    // NOTE: creating a booking requires BookingService to reach ListingService over HTTPS
    // (price + capacity). The local dev cert must be trusted (dotnet dev-certs https --trust)
    // for that server-to-server call to succeed.
    public class BookingFlowTests
    {
        [Fact]
        public async Task AvailabilityCalendar_ReturnsDateGrid()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);
            var listing = await Flows.CreateListingAsync(api, token, ListingCategory.ShortTerm);

            var start = DateTime.UtcNow.Date.AddDays(1);
            var end = start.AddDays(10);
            var response = await api.GetAsync(
                $"/api/bookings/availability-calendar/{listing.Id}?startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}");
            response.EnsureSuccessStatusCode();

            var calendar = await Json.ReadAsync<CalendarResponse>(response);
            Assert.NotNull(calendar.Calendar);
            Assert.Equal((int)(end - start).TotalDays + 1, calendar.Calendar.Count);
            Assert.All(calendar.Calendar, day => Assert.True(day.IsAvailable, "Fresh listing should have all dates available"));
        }

        [Fact]
        public async Task CreateBooking_WithinCapacity_Succeeds()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);
            var listing = await Flows.CreateListingAsync(api, token, ListingCategory.ShortTerm, guests: 4);

            var response = await api.PostAsync("/api/bookings", TestData.BookingBody(listing.Id, numberOfGuests: 2), token);

            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                $"Expected success, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        [Fact]
        public async Task CreateBooking_ExceedingCapacity_FailsValidation()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);
            // Capacity 2, but request 5 guests (still within the DTO's [Range(1,20)] so it reaches
            // the service-level capacity check rather than being rejected by model validation).
            var listing = await Flows.CreateListingAsync(api, token, ListingCategory.ShortTerm, guests: 2);

            var response = await api.PostAsync("/api/bookings", TestData.BookingBody(listing.Id, numberOfGuests: 5), token);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
