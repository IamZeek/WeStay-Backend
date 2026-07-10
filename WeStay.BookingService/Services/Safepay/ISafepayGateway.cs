namespace WeStay.BookingService.Services.Safepay
{
    /// <summary>
    /// Isolates ALL SafePay HTTP. Swap/adjust SafePay specifics here without touching payment logic.
    /// </summary>
    public interface ISafepayGateway
    {
        /// <summary>True only when apiKey + webhookSecret are configured (fail-closed elsewhere).</summary>
        bool IsConfigured { get; }

        /// <summary>Create a payment tracker (POST /order/v1/init). Returns the tracker token (track_...).</summary>
        Task<string> CreateTrackerAsync(decimal amount, string currency);

        /// <summary>Build the hosted-checkout URL the guest is redirected to.</summary>
        string BuildCheckoutUrl(string tracker, string orderId, string redirectUrl, string cancelUrl);

        /// <summary>
        /// Verify a webhook: HMAC-SHA256 of the tracker keyed by the webhook secret (lowercase hex),
        /// constant-time compared to the signature SafePay sent. Returns false on any mismatch.
        /// </summary>
        bool VerifyWebhookSignature(string tracker, string providedSignature);

        /// <summary>Issue a (full or partial) refund against a tracker.</summary>
        Task<SafepayRefundResult> RefundAsync(string tracker, decimal amount, string currency);
    }

    public record SafepayRefundResult(bool Success, string RefundRef, string State, string Error);
}
