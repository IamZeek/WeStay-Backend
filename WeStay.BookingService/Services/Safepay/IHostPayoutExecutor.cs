using WeStay.BookingService.Models;

namespace WeStay.BookingService.Services.Safepay
{
    /// <summary>
    /// Executes the actual host payout. For Phase 1 the money movement is a MANUAL bank transfer done
    /// off-platform by an admin, so the default executor is a no-op that just records intent — the
    /// admin marks it paid out. Swap this for a real SafePay payout/disbursement API call later
    /// WITHOUT touching the payment state machine or endpoints.
    /// </summary>
    public interface IHostPayoutExecutor
    {
        Task<HostPayoutResult> ExecuteAsync(Payment payment);
    }

    public record HostPayoutResult(bool Success, string Reference, string Error);

    /// <summary>Manual payout: no external call; the admin has moved the money out-of-band.</summary>
    public class ManualHostPayoutExecutor : IHostPayoutExecutor
    {
        private readonly ILogger<ManualHostPayoutExecutor> _logger;

        public ManualHostPayoutExecutor(ILogger<ManualHostPayoutExecutor> logger)
        {
            _logger = logger;
        }

        public Task<HostPayoutResult> ExecuteAsync(Payment payment)
        {
            _logger.LogInformation(
                "Manual host payout recorded for payment {PaymentId} (booking {BookingId}), amount {Amount} {Currency}.",
                payment.Id, payment.BookingId, payment.HostPayoutAmount, payment.Currency);
            return Task.FromResult(new HostPayoutResult(true, $"manual-{payment.Id}", null));
        }
    }
}
