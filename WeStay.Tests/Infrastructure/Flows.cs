namespace WeStay.Tests.Infrastructure
{
    /// <summary>
    /// Reusable end-to-end flows (all through the Gateway). Each returns the data later steps need.
    /// </summary>
    public static class Flows
    {
        public static async Task<(string email, string password, string token)> RegisterAsync(ApiClient api)
        {
            var email = TestData.RandomEmail();
            var response = await api.PostAsync("/api/auth/register", TestData.RegisterBody(email));
            response.EnsureSuccessStatusCode();
            var auth = await Json.ReadAsync<AuthResponse>(response);
            return (email, TestData.Password, auth.Token);
        }

        public static async Task<string> LoginAsync(ApiClient api, string email, string password)
        {
            var response = await api.PostAsync("/api/auth/login", new { Email = email, Password = password });
            response.EnsureSuccessStatusCode();
            var auth = await Json.ReadAsync<AuthResponse>(response);
            return auth.Token;
        }

        public static async Task<string> BecomeHostAsync(ApiClient api, string token)
        {
            var response = await api.PostAsync("/api/auth/become-host", null, token);
            response.EnsureSuccessStatusCode();
            var result = await Json.ReadAsync<BecomeHostResponse>(response);
            return result.Token;
        }

        public static async Task<ListingDto> CreateListingAsync(
            ApiClient api, string token, ListingCategory category,
            int guests = 4, decimal pricePerNight = 100m, string? city = null)
        {
            city ??= TestData.UniqueCity();
            var response = await api.PostAsync("/api/listings", TestData.ListingBody(category, guests, pricePerNight, city), token);
            response.EnsureSuccessStatusCode();
            return await Json.ReadAsync<ListingDto>(response);
        }
    }
}
