namespace WeStay.Tests.Infrastructure
{
    /// <summary>
    /// Thin HTTP wrapper used by the tests. Defaults to the API Gateway (so tests exercise the
    /// real end-to-end path); pass a different base URL only when no gateway route exists.
    /// Accepts the local dev HTTPS certificate. The bearer token is set per-request so a single
    /// client is safe to reuse across parallel tests.
    /// </summary>
    public sealed class ApiClient : IDisposable
    {
        private readonly HttpClient _http;

        public ApiClient() : this(TestConfig.Gateway) { }

        public ApiClient(string baseUrl)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public Task<HttpResponseMessage> GetAsync(string path, string? token = null, string? internalKey = null)
            => Send(HttpMethod.Get, path, null, token, internalKey);

        public Task<HttpResponseMessage> PostAsync(string path, object? body = null, string? token = null, string? internalKey = null)
            => Send(HttpMethod.Post, path, body, token, internalKey);

        public Task<HttpResponseMessage> PutAsync(string path, object? body = null, string? token = null, string? internalKey = null)
            => Send(HttpMethod.Put, path, body, token, internalKey);

        public Task<HttpResponseMessage> PatchAsync(string path, object? body = null, string? token = null)
            => Send(HttpMethod.Patch, path, body, token, null);

        public Task<HttpResponseMessage> DeleteAsync(string path, string? token = null)
            => Send(HttpMethod.Delete, path, null, token, null);

        private Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, string? token, string? internalKey)
        {
            var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            if (!string.IsNullOrEmpty(internalKey))
            {
                request.Headers.Add("X-Internal-Api-Key", internalKey);
            }
            return _http.SendAsync(request);
        }

        public void Dispose() => _http.Dispose();
    }
}
