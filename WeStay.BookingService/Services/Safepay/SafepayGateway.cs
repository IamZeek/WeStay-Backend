using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sdk = global::Safepay;   // official SafePay .NET SDK (SFPY.net); aliased to avoid clashing with this namespace

namespace WeStay.BookingService.Services.Safepay
{
    /// <summary>
    /// SafePay Hosted Checkout integration. Tracker creation + checkout-URL generation go through the
    /// OFFICIAL SafePay .NET SDK (SFPY.net) — it sends the current required fields (intent/mode) and
    /// builds the correct /checkout/pay URL. The SDK has NO refund API, so RefundAsync calls the REST
    /// endpoint directly; webhook signatures are HMAC-verified here.
    ///
    /// Secrets come from config (User Secrets) — never logged. Fail-closed: throws if unconfigured.
    /// </summary>
    public class SafepayGateway : ISafepayGateway
    {
        // The SDK holds API credentials + HttpClient in STATIC state; initialize its client once.
        private static readonly object _sdkLock = new();
        private static bool _sdkClientInitialized;

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

        // Push our credentials into the SDK's static config and initialize its HttpClient once.
        private void EnsureSdkConfigured()
        {
            lock (_sdkLock)
            {
                Sdk.SafepayConfiguration.ApiKey = _apiKey;
                Sdk.SafepayConfiguration.WebhookSecret = _webhookSecret;
                Sdk.SafepayConfiguration.Environment = _environment;   // sets the SDK's sandbox/prod ApiBase
                if (!_sdkClientInitialized)
                {
                    Sdk.SafepayClient.InitializeApiClient(false);       // false = not debug-logging
                    _sdkClientInitialized = true;
                }
            }
        }

        // ===================================================================================
        //  AMOUNT UNITS — single source of truth. VERIFIED BY OBSERVATION on the SDK's /checkout/pay
        //  page: it renders the amount in MAJOR units (rupees), NOT paisa. Sending 1,080,000 showed
        //  "Rs. 1,080,000"; sending 10,800 shows "Rs. 10,800". So pass the major-unit value as-is.
        //  ⚠️ If the checkout page ever shows a 100× discrepancy, change ONLY this method.
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
            EnsureSdkConfigured();

            // Official SDK builds the current /order/v1/init request (incl. the required intent/mode)
            // and returns the tracker token. Amount is in MAJOR units (see ToSafepayAmount) — the SDK
            // sends the value through as-is; /checkout/pay renders it in rupees.
            var response = await Sdk.Order.CreateTracker((double)ToSafepayAmount(amount), currency);

            var token = response?.Data?.Token;
            if (!string.Equals(response?.Status?.Message, "success", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(token))
            {
                // Never log the request (carries the apiKey). Status message only.
                _logger.LogError("SafePay tracker creation failed: {Status}", response?.Status?.Message);
                throw new InvalidOperationException("SafePay tracker creation failed.");
            }
            return token;
        }

        public string BuildCheckoutUrl(string tracker, string orderId, string redirectUrl, string cancelUrl)
        {
            EnsureSdkConfigured();
            // Official SDK generates the hosted-checkout URL (/checkout/pay?...&webhooks=true). NOTE the
            // SDK's parameter order is (trackerToken, orderId, cancelUrl, redirectUrl, source, usingWebhookVerification)
            // — cancelUrl precedes redirectUrl. usingWebhookVerification:true because the authoritative
            // state change is driven by the signed webhook, not the browser redirect.
            return Sdk.Checkout.CreateSession(tracker, orderId, cancelUrl, redirectUrl, _source, usingWebhookVerification: true);
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
