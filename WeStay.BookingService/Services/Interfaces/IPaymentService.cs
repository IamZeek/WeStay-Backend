using WeStay.BookingService.Models;

namespace WeStay.BookingService.Services.Interfaces
{
    public enum WebhookResult { Processed, AlreadyProcessed, MarkedFailed, InvalidSignature, UnknownTracker }

    public record InitiateResult(string CheckoutUrl, int PaymentId, string Tracker);

    public interface IPaymentService
    {
        Task<InitiateResult> InitiatePaymentAsync(int bookingId, int requestingUserId);
        Task<WebhookResult> HandleWebhookAsync(string tracker, bool success, string signature);
        Task<Payment> GetPaymentForBookingAsync(int bookingId, int requestingUserId, bool isAdmin);
        Task<Payment> RefundAsync(int paymentId, int requestingUserId, bool isAdmin);

        // Wired into the booking lifecycle / cancel flow (best-effort; never throws to the caller).
        Task MarkReleasableForBookingAsync(int bookingId);
        Task RefundForBookingIfPaidAsync(int bookingId);

        // Admin.
        Task<(IEnumerable<Payment> payments, int totalCount)> GetAllAsync(int page, int pageSize, string status);
        Task<Payment> MarkPaidOutAsync(int paymentId);

        // SANDBOX TESTING ONLY — remove before production. Simulates the SafePay webhook's success path
        // (Pending → Paid → HeldForStay) so the rest of the state machine can be exercised without
        // depending on sandbox webhook delivery. See PaymentService for details.
        Task<Payment> MarkPaidForTestingAsync(int paymentId);
    }
}
