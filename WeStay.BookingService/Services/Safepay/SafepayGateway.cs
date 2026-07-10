using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WeStay.BookingService.Services.Safepay
{
    /// <summary>
    /// Direct REST integration with SafePay's Hosted Checkout (the official .NET SDK is unmaintained
    /// and has no refund support, so we call the API directly and verify the HMAC ourselves).
    ///
    /// Secrets come from config (User Secrets) — never logged. Fail-closed: throws if unconfigured.
    /// </summary>
    public class SafepayGateway : ISafepayGateway
    {
        private readonly HttpClient _http;
        private readonly ILogger<SafepayGateway> _logger;

        private readonly string _apiKey;
        private readonly string _webhookSecret;
        private readonly string _environment;   // "sandbox" | "production"
        private readonly string _source;
        private readonly string _refundPath;

        public SafepayGateway(HttpClient http, IConfiguration configuration, ILogger<SafepayGateway> logger)
        {
            _http = http;
            _logger = logger;
            _apiKey = configuration["Safepay:ApiKey"];
            _webhookSecret = configuration["Safepay:WebhookSecret"];
            _environment = (configuration["Safepay:Environment"] ?? "sandbox").ToLowerInvariant();
            _source = configuration["Safepay:Source"] ?? "westay";
            // Refund endpoint path is configurable so it can be corrected from the SafePay API
            // reference without a rebuild (the refund path/auth is the least-documented piece).
            _refundPath = configuration["Safepay:RefundPath"] ?? "/order/v1/refund";
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_webhookSecret);

        private bool IsProduction => _environment == "production";
        private string ApiBase => IsProduction ? "https://api.getsafepay.com" : "https://sandbox.api.getsafepay.com";
        private string CheckoutBase => IsProduction ? "https://www.getsafepay.com/components" : "https://sandbox.api.getsafepay.com/components";

        // ===================================================================================
        //  AMOUNT UNITS — the single source of truth. SafePay Hosted Checkout (/order/v1/init)
        //  expects MAJOR units (decimal PKR, e.g. 1000.00 = PKR 1,000), per SafePay's own
        //  integration gist ("amount": 1000.00) and the official WooCommerce plugin (which passes
        //  the raw decimal order total). This is NOT the newer /payments/session/setup CYBERSOURCE
        //  flow, which uses smallest units (paisa). If a sandbox test EVER shows a 100× discrepancy
        //  on the checkout page, change ONLY this method (× 100m) — nothing else depends on units.
        // ===================================================================================
        private static decimal ToSafepayAmount(decimal majorUnits) => majorUnits;

        private void EnsureConfigured()
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    "SafePay is not configured. Set Safepay:ApiKey and Safepay:WebhookSecret in User Secrets.");
            }
        }

        public async Task<string> CreateTrackerAsync(decimal amount, string currency)
        {
            EnsureConfigured();

            var payload = new
            {
                client = _apiKey,
                amount = ToSafepayAmount(amount),
                currency,
                environment = _environment
            };

            var response = await _http.PostAsJsonAsync($"{ApiBase}/order/v1/init", payload);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                // Do not log the request body (contains the apiKey). Status + trimmed body only.
                _logger.LogError("SafePay init failed: {Status} {Body}", (int)response.StatusCode, Trim(body));
                throw new InvalidOperationException($"SafePay init failed ({(int)response.StatusCode}).");
            }

            var token = ExtractToken(body);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("SafePay init returned no token. Body: {Body}", Trim(body));
                throw new InvalidOperationException("SafePay init returned no tracker token.");
            }
            return token;
        }

        public string BuildCheckoutUrl(string tracker, string orderId, string redirectUrl, string cancelUrl)
        {
            var q = new Dictionary<string, string>
            {
                ["env"] = _environment,
                ["beacon"] = tracker,
                ["source"] = _source,
                ["order_id"] = orderId,
                ["redirect_url"] = redirectUrl,
                ["cancel_url"] = cancelUrl
            };
            var query = string.Join("&", q.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
            return $"{CheckoutBase}?{query}";
        }

        public bool VerifyWebhookSignature(string tracker, string providedSignature)
        {
            if (string.IsNullOrEmpty(_webhookSecret) || string.IsNullOrEmpty(tracker) || string.IsNullOrEmpty(providedSignature))
            {
                return false;
            }

            var computed = ComputeHmacHex(tracker, _webhookSecret);
            var a = Encoding.UTF8.GetBytes(computed);
            var b = Encoding.UTF8.GetBytes(providedSignature.Trim());
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        public async Task<SafepayRefundResult> RefundAsync(string tracker, decimal amount, string currency)
        {
            EnsureConfigured();
            try
            {
                // Documented refund shape: { tracker, payload: { currency, amount } }. The exact path
                // (Safepay:RefundPath) and auth are the least-confirmed part of SafePay's public docs —
                // isolated here and configurable so it can be corrected without touching payment logic.
                var payload = new
                {
                    tracker,
                    payload = new { currency, amount = ToSafepayAmount(amount) }
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}{_refundPath}")
                {
                    Content = JsonContent.Create(payload)
                };
                req.Headers.Add("X-SFPY-MERCHANT-SECRET", _apiKey);

                var response = await _http.SendAsync(req);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("SafePay refund failed: {Status} {Body}", (int)response.StatusCode, Trim(body));
                    return new SafepayRefundResult(false, null, null, $"HTTP {(int)response.StatusCode}");
                }

                var state = ExtractRefundState(body);
                return new SafepayRefundResult(true, tracker, state, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SafePay refund error for tracker.");
                return new SafepayRefundResult(false, null, null, ex.Message);
            }
        }

        private static string ComputeHmacHex(string message, string key)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var x in hash) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string ExtractToken(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                // Response shapes seen: { "data": { "token": "track_..." } } or { "token": "..." }.
                if (root.TryGetProperty("data", out var data) && data.TryGetProperty("token", out var t1))
                    return t1.GetString();
                if (root.TryGetProperty("token", out var t2))
                    return t2.GetString();
                return null;
            }
            catch { return null; }
        }

        private static string ExtractRefundState(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("tracker", out var tr) &&
                    tr.TryGetProperty("state", out var st))
                    return st.GetString();
            }
            catch { }
            return null;
        }

        private static string Trim(string s) => string.IsNullOrEmpty(s) ? s : (s.Length > 300 ? s.Substring(0, 300) : s);
    }
}
