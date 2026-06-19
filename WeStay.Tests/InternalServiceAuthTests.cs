namespace WeStay.Tests
{
    // Service-to-service auth: the 7 internal endpoints require the shared X-Internal-Api-Key.
    // NOTE: these tests call NotificationService directly (port 7284), so the full set now also
    // needs NotificationService running alongside Auth/Listing/Booking/Review/Gateway.
    public class InternalServiceAuthTests
    {
        // Every internal endpoint rejects a direct call that omits the service key (401), before any
        // business logic — so a placeholder id (1) is fine; the filter fires first.
        [Fact]
        public async Task InternalEndpoints_RejectDirectCallsWithoutServiceKey()
        {
            var calls = new (string baseUrl, HttpMethod method, string path)[]
            {
                (TestConfig.Listing,      HttpMethod.Get,  "/api/listings/1/price"),
                (TestConfig.Listing,      HttpMethod.Get,  "/api/listings/1/capacity"),
                (TestConfig.Listing,      HttpMethod.Put,  "/api/listings/1/rating"),
                (TestConfig.Booking,      HttpMethod.Get,  "/api/bookings/1/info"),
                (TestConfig.Auth,         HttpMethod.Get,  "/api/auth/users/1/contact"),
                (TestConfig.Notification, HttpMethod.Post, "/api/notifications/email"),
                (TestConfig.Notification, HttpMethod.Post, "/api/notifications/sms"),
            };

            foreach (var (baseUrl, method, path) in calls)
            {
                using var api = new ApiClient(baseUrl);
                var resp = method == HttpMethod.Get ? await api.GetAsync(path)
                    : method == HttpMethod.Put ? await api.PutAsync(path, new { AverageRating = 4.0, ReviewCount = 1 })
                    : await api.PostAsync(path, new { });

                Assert.True(resp.StatusCode == HttpStatusCode.Unauthorized,
                    $"{method} {baseUrl}{path} without service key expected 401 but got {(int)resp.StatusCode}");
            }
        }

        // With a valid service key, a direct internal call is accepted (returns the value, not 401).
        [Fact]
        public async Task InternalEndpoint_WithServiceKey_IsAccepted()
        {
            using var gateway = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(gateway);
            var listing = await Flows.CreateListingAsync(gateway, token, ListingCategory.ShortTerm, pricePerNight: 77m);

            using var direct = new ApiClient(TestConfig.Listing);
            var resp = await direct.GetAsync($"/api/listings/{listing.Id}/price", internalKey: TestConfig.InternalApiKey);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("77.00", (await resp.Content.ReadAsStringAsync()).Trim());
        }

        // Item 5: with a normal user JWT (and no service key), these are NOT usable through the Gateway —
        // the routed ones fail the service-key check (401) and the notification endpoints aren't routed (404).
        [Fact]
        public async Task InternalEndpoints_NotUsableViaGatewayWithUserJwt()
        {
            using var gateway = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(gateway);
            var listing = await Flows.CreateListingAsync(gateway, token, ListingCategory.ShortTerm);

            // Routed by wildcards but service-key-gated → 401 even with a valid user JWT.
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await gateway.GetAsync($"/api/listings/{listing.Id}/price", token)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await gateway.GetAsync($"/api/bookings/1/info", token)).StatusCode);

            // NotificationService /email + /sms are deliberately not routed at the gateway → 404.
            Assert.Equal(HttpStatusCode.NotFound,
                (await gateway.PostAsync("/api/notifications/email", new { }, token)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await gateway.PostAsync("/api/notifications/sms", new { }, token)).StatusCode);
        }
    }
}
