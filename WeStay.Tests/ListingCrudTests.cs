namespace WeStay.Tests
{
    // Priority 3: listing CRUD + the new ListingCategory taxonomy + map fields present.
    public class ListingCrudTests
    {
        [Theory]
        [InlineData(ListingCategory.ShortTerm)]
        [InlineData(ListingCategory.LongTerm)]
        [InlineData(ListingCategory.Sale)]
        public async Task CreateListing_WithEachCategory_Succeeds(ListingCategory category)
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);

            var listing = await Flows.CreateListingAsync(api, token, category);

            Assert.Equal((int)category, listing.Category);
        }

        [Fact]
        public async Task Search_ByCategory_ReturnsOnlyThatCategory()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);
            // Guarantee at least one Sale listing exists for this assertion.
            await Flows.CreateListingAsync(api, token, ListingCategory.Sale);

            var response = await api.GetAsync($"/api/search?category={(int)ListingCategory.Sale}&pageSize=50");
            response.EnsureSuccessStatusCode();

            var search = await Json.ReadAsync<SearchResponse>(response);
            Assert.NotEmpty(search.Listings);
            Assert.All(search.Listings, l => Assert.Equal((int)ListingCategory.Sale, l.Category));
        }

        [Fact]
        public async Task GetListingById_IncludesLatitudeAndLongitude()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);
            var created = await Flows.CreateListingAsync(api, token, ListingCategory.ShortTerm);

            var response = await api.GetAsync($"/api/listings/{created.Id}");
            response.EnsureSuccessStatusCode();

            var element = await Json.ReadElementAsync(response);
            Assert.True(Json.HasProperty(element, "latitude"), "Listing response should include a latitude field");
            Assert.True(Json.HasProperty(element, "longitude"), "Listing response should include a longitude field");
        }
    }
}
