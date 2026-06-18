namespace WeStay.Tests
{
    // Priority 4: the ListingService endpoints BookingService calls over HTTP (price + capacity).
    // These prove the previously non-existent /price endpoint (found during booking cleanup) works.
    public class CrossServiceTests
    {
        [Fact]
        public async Task GetListingPrice_ReturnsDecimal()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);
            var listing = await Flows.CreateListingAsync(api, token, ListingCategory.ShortTerm, pricePerNight: 123.45m);

            var response = await api.GetAsync($"/api/listings/{listing.Id}/price");
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            Assert.True(decimal.TryParse(body, NumberStyles.Any, CultureInfo.InvariantCulture, out var price),
                $"Expected a decimal price, got '{body}'");
            Assert.Equal(123.45m, price);
        }

        [Fact]
        public async Task GetListingCapacity_ReturnsInt()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);
            var listing = await Flows.CreateListingAsync(api, token, ListingCategory.ShortTerm, guests: 6);

            var response = await api.GetAsync($"/api/listings/{listing.Id}/capacity");
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            Assert.True(int.TryParse(body, NumberStyles.Any, CultureInfo.InvariantCulture, out var capacity),
                $"Expected an integer capacity, got '{body}'");
            Assert.Equal(6, capacity);
        }
    }
}
