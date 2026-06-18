namespace WeStay.Tests
{
    // Priority 1: the critical auth path (mirrors Prompt 3's curl test), all through the Gateway.
    public class AuthChainTests
    {
        [Fact]
        public async Task Register_NewUser_ReturnsSuccess()
        {
            using var api = new ApiClient();
            var response = await api.PostAsync("/api/auth/register", TestData.RegisterBody(TestData.RandomEmail()));
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                $"Expected 200/201, got {(int)response.StatusCode}");
        }

        [Fact]
        public async Task Login_AfterRegister_ReturnsToken()
        {
            using var api = new ApiClient();
            var (email, password, registerToken) = await Flows.RegisterAsync(api);
            Assert.False(string.IsNullOrWhiteSpace(registerToken));

            var loginToken = await Flows.LoginAsync(api, email, password);
            Assert.False(string.IsNullOrWhiteSpace(loginToken));
        }

        [Fact]
        public async Task ProtectedEndpoint_WithValidToken_Succeeds_Not401()
        {
            using var api = new ApiClient();
            var (_, _, token) = await Flows.RegisterAsync(api);

            var response = await api.PostAsync(
                "/api/listings",
                TestData.ListingBody(ListingCategory.ShortTerm, 4, 100m, TestData.UniqueCity()),
                token);

            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                $"Expected 200/201, got {(int)response.StatusCode}");
        }

        [Fact]
        public async Task ProtectedEndpoint_WithoutToken_Returns401()
        {
            using var api = new ApiClient();
            var response = await api.PostAsync(
                "/api/listings",
                TestData.ListingBody(ListingCategory.ShortTerm, 4, 100m, TestData.UniqueCity()));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task ProtectedEndpoint_WithTamperedToken_Returns401()
        {
            using var api = new ApiClient();
            var response = await api.PostAsync(
                "/api/listings",
                TestData.ListingBody(ListingCategory.ShortTerm, 4, 100m, TestData.UniqueCity()),
                token: "garbage.tampered.token");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
