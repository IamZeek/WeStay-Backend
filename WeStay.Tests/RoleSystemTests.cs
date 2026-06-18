namespace WeStay.Tests
{
    // Priority 2: role system (Guest default, become-host, role-gated endpoints).
    public class RoleSystemTests
    {
        [Fact]
        public async Task BecomeHost_ReturnsNewTokenWithHostRole()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);

            var response = await api.PostAsync("/api/auth/become-host", null, token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await Json.ReadAsync<BecomeHostResponse>(response);
            Assert.False(string.IsNullOrWhiteSpace(result.Token));
            Assert.Equal("Host", result.Role);
        }

        [Fact]
        public async Task HostToken_CanFeatureOwnListing()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, token);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm);

            var response = await api.PostAsync(
                $"/api/listings/{listing.Id}/feature",
                new { IsFeatured = true, FeaturedUntil = DateTime.UtcNow.AddDays(30) },
                hostToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GuestToken_CannotCallHostOnlyFeature_Returns403()
        {
            using var api = new ApiClient();
            var (_, _, guestToken) = await Flows.RegisterAsync(api);
            // A Guest may create a listing, but may not feature it (Host/Admin only).
            var listing = await Flows.CreateListingAsync(api, guestToken, ListingCategory.ShortTerm);

            var response = await api.PostAsync(
                $"/api/listings/{listing.Id}/feature",
                new { IsFeatured = true },
                guestToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GuestToken_CannotCallAdminOnlySetRole_Returns403()
        {
            using var api = new ApiClient();
            var (_, _, guestToken) = await Flows.RegisterAsync(api);

            var response = await api.PutAsync("/api/auth/users/1/role", new { Role = 2 }, guestToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
