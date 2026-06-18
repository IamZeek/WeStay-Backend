namespace WeStay.Tests
{
    // Priority 6: featured listings data model + Host/Admin toggle + featured query.
    public class FeaturedListingsTests
    {
        [Fact]
        public async Task OwnerHost_CanToggleFeatured()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, token);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm);

            var response = await api.PostAsync(
                $"/api/listings/{listing.Id}/feature",
                new { IsFeatured = true, FeaturedUntil = DateTime.UtcNow.AddYears(5) },
                hostToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task NonOwnerHost_CannotFeatureOthersListing_Returns403()
        {
            using var api = new ApiClient();

            // Host A owns the listing.
            var (_, _, tokenA) = await Flows.RegisterAsync(api);
            var hostA = await Flows.BecomeHostAsync(api, tokenA);
            var listing = await Flows.CreateListingAsync(api, hostA, ListingCategory.ShortTerm);

            // Host B (a different host) tries to feature it.
            var (_, _, tokenB) = await Flows.RegisterAsync(api);
            var hostB = await Flows.BecomeHostAsync(api, tokenB);

            var response = await api.PostAsync(
                $"/api/listings/{listing.Id}/feature",
                new { IsFeatured = true },
                hostB);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task FeaturedListing_AppearsInFeatured_NonFeaturedDoesNot()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, token);

            var featured = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm);
            var notFeatured = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm);

            // Far-future FeaturedUntil so it sorts to the top of the (max 8) featured list.
            var setResponse = await api.PostAsync(
                $"/api/listings/{featured.Id}/feature",
                new { IsFeatured = true, FeaturedUntil = DateTime.UtcNow.AddYears(10) },
                hostToken);
            setResponse.EnsureSuccessStatusCode();

            var response = await api.GetAsync("/api/search/featured");
            response.EnsureSuccessStatusCode();
            var listings = await Json.ReadAsync<List<ListingDto>>(response);

            Assert.Contains(listings, l => l.Id == featured.Id);
            Assert.DoesNotContain(listings, l => l.Id == notFeatured.Id);
        }
    }
}
