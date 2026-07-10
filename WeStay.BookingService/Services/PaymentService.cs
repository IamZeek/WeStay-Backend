using WeStay.BookingService.Models;
using WeStay.BookingService.Repositories.Interfaces;
using WeStay.BookingService.Services.Interfaces;
using WeStay.BookingService.Services.Safepay;

namespace WeStay.BookingService.Services
{
    /// <summary>
    /// Payment lifecycle + guarded state machine. WeStay collects and holds the money; SafePay is only
    /// the gateway. Illegal transitions throw with a clear message (like the booking state machine).
    /// </summary>
    public class PaymentService : IPaymentService
    {
        private readonly IPaymentRepository _payments;
        private readonly IBookingRepository _bookings;
        private readonly IPlatformFeeConfigRepository _feeConfig;
        private readonly ISafepayGateway _safepay;
        private readonly IHostPayoutExecutor _payoutExecutor;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(
            IPaymentRepository payments,
            IBookingRepository bookings,
            IPlatformFeeConfigRepository feeConfig,
            ISafepayGateway safepay,
            IHostPayoutExecutor payoutExecutor,
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<PaymentService> logger)
        {
            _payments = payments;
            _bookings = bookings;
            _feeConfig = feeConfig;
            _safepay = safepay;
            _payoutExecutor = payoutExecutor;
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<InitiateResult> InitiatePaymentAsync(int bookingId, int requestingUserId)
        {
            // Validate the booking first (so validation errors don't depend on SafePay config).
            var booking = await _bookings.GetBookingByIdAsync(bookingId);
            if (booking == null) throw new KeyNotFoundException("Booking not found.");
            if (booking.UserId != requestingUserId)
                throw new UnauthorizedAccessException("Only the guest who made this booking can pay for it.");

            var statusName = booking.Status?.Name ?? "Unknown";
            if (!string.Equals(statusName, "Confirmed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Payment can only be initiated on a Confirmed booking (status is '{statusName}').");

            var existing = await _payments.GetByBookingIdAsync(bookingId);
            if (existing != null && existing.Status != PaymentState.Pending.ToString() && existing.Status != PaymentState.Failed.ToString())
                throw new InvalidOperationException($"This booking already has a payment in status '{existing.Status}'.");

            var amount = booking.GuestTotalPrice;
            var currency = string.IsNullOrWhiteSpace(booking.Currency) ? "PKR" : booking.Currency;

            var payment = existing ?? new Payment { BookingId = bookingId };
            payment.Amount = amount;                        // snapshot: what the guest pays
            payment.Currency = currency;
            payment.HostPayoutAmount = booking.HostPayoutAmount; // snapshot: what the host is owed
            payment.Status = PaymentState.Pending.ToString();

            if (!_safepay.IsConfigured)
            {
                throw new InvalidOperationException("Payments are not available: SafePay is not configured.");
            }

            var tracker = await _safepay.CreateTrackerAsync(amount, currency);
            payment.Tracker = tracker;

            payment = existing == null ? await _payments.CreateAsync(payment) : await _payments.UpdateAsync(payment);

            var redirectUrl = _configuration["Safepay:RedirectUrl"] ?? "https://westay.local/payment/success";
            var cancelUrl = _configuration["Safepay:CancelUrl"] ?? "https://westay.local/payment/cancel";
            var checkoutUrl = _safepay.BuildCheckoutUrl(tracker, booking.BookingCode, redirectUrl, cancelUrl);

            _logger.LogInformation("Payment {PaymentId} initiated for booking {BookingId} ({Amount} {Currency}).",
                payment.Id, bookingId, amount, currency);

            return new InitiateResult(checkoutUrl, payment.Id, tracker);
        }

        public async Task<WebhookResult> HandleWebhookAsync(string tracker, bool success, string signature)
        {
            // Verify BEFORE trusting anything.
            if (!_safepay.VerifyWebhookSignature(tracker, signature))
            {
                _logger.LogWarning("Rejected webhook with invalid signature for a tracker.");
                return WebhookResult.InvalidSignature;
            }

            var payment = await _payments.GetByTrackerAsync(tracker);
            if (payment == null) return WebhookResult.UnknownTracker;

            // Idempotency: if already progressed past Pending, do nothing.
            if (payment.Status != PaymentState.Pending.ToString())
            {
                return WebhookResult.AlreadyProcessed;
            }

            var now = DateTime.UtcNow;
            if (!success)
            {
                payment.Status = PaymentState.Failed.ToString();
                payment.FailedAt = now;
                await _payments.UpdateAsync(payment);
                return WebhookResult.MarkedFailed;
            }

            // Pending -> Paid -> HeldForStay (captured funds now held by WeStay until checkout).
            payment.Status = PaymentState.HeldForStay.ToString();
            payment.PaidAt = now;
            payment.HeldAt = now;
            await _payments.UpdateAsync(payment);

            _logger.LogInformation("Payment {PaymentId} (booking {BookingId}) confirmed Paid → HeldForStay.",
                payment.Id, payment.BookingId);
            return WebhookResult.Processed;
        }

        public async Task<Payment> GetPaymentForBookingAsync(int bookingId, int requestingUserId, bool isAdmin)
        {
            var payment = await _payments.GetByBookingIdAsync(bookingId);
            if (payment == null) throw new KeyNotFoundException("No payment for this booking.");

            if (isAdmin || payment.Booking?.UserId == requestingUserId)
            {
                return payment;
            }

            // Host of the listing may also view it.
            var hostId = await GetListingHostIdAsync(payment.Booking?.ListingId ?? 0);
            if (hostId.HasValue && hostId.Value == requestingUserId)
            {
                return payment;
            }

            throw new UnauthorizedAccessException("You do not have access to this payment.");
        }

        public async Task<Payment> RefundAsync(int paymentId, int requestingUserId, bool isAdmin)
        {
            var payment = await _payments.GetByIdAsync(paymentId);
            if (payment == null) throw new KeyNotFoundException("Payment not found.");

            if (!isAdmin && payment.Booking?.UserId != requestingUserId)
                throw new UnauthorizedAccessException("Only the guest who paid, or an admin, can refund this payment.");

            return await RefundInternalAsync(payment);
        }

        // Best-effort refund on cancel (called from the booking cancel flow). Never throws.
        public async Task RefundForBookingIfPaidAsync(int bookingId)
        {
            try
            {
                var payment = await _payments.GetByBookingIdAsync(bookingId);
                if (payment == null) return;
                if (payment.Status != PaymentState.Paid.ToString() && payment.Status != PaymentState.HeldForStay.ToString())
                {
                    return; // nothing to refund (unpaid, already refunded, or already released)
                }
                await RefundInternalAsync(payment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-refund on cancel failed for booking {BookingId}; needs admin follow-up.", bookingId);
            }
        }

        private async Task<Payment> RefundInternalAsync(Payment payment)
        {
            // Refunds are only valid PRE-release.
            EnsureStatus(payment, "refunded", PaymentState.Paid, PaymentState.HeldForStay);

            var pct = (await _feeConfig.GetAsync()).CancellationFeePercent;
            var cancellationFee = Math.Round(payment.Amount * pct / 100m, 2, MidpointRounding.AwayFromZero);
            var refundAmount = payment.Amount - cancellationFee;

            var result = await _safepay.RefundAsync(payment.Tracker, refundAmount, payment.Currency);
            if (!result.Success)
            {
                throw new InvalidOperationException($"SafePay refund failed: {result.Error}");
            }

            payment.Status = PaymentState.Refunded.ToString();
            payment.CancellationFeeAmount = cancellationFee;
            payment.RefundAmount = refundAmount;
            payment.SafepayRefundRef = result.RefundRef;
            payment.RefundedAt = DateTime.UtcNow;
            await _payments.UpdateAsync(payment);

            _logger.LogInformation("Payment {PaymentId} refunded {Refund} {Currency} (kept {Fee} cancellation fee).",
                payment.Id, refundAmount, payment.Currency, cancellationFee);
            return payment;
        }

        // Called when a booking becomes Completed (checkout passed). HeldForStay -> ReleasableToHost.
        public async Task MarkReleasableForBookingAsync(int bookingId)
        {
            try
            {
                var payment = await _payments.GetByBookingIdAsync(bookingId);
                if (payment == null || payment.Status != PaymentState.HeldForStay.ToString())
                {
                    return; // no held payment (unpaid / refunded / already releasable)
                }

                payment.Status = PaymentState.ReleasableToHost.ToString();
                payment.Releasable = true;
                payment.ReleasableAt = DateTime.UtcNow;
                await _payments.UpdateAsync(payment);

                _logger.LogInformation("Payment {PaymentId} (booking {BookingId}) → ReleasableToHost on completion.",
                    payment.Id, bookingId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark payment releasable for booking {BookingId}.", bookingId);
            }
        }

        public async Task<(IEnumerable<Payment> payments, int totalCount)> GetAllAsync(int page, int pageSize, string status)
            => await _payments.GetAllAsync(page, pageSize, status);

        public async Task<Payment> MarkPaidOutAsync(int paymentId)
        {
            var payment = await _payments.GetByIdAsync(paymentId);
            if (payment == null) throw new KeyNotFoundException("Payment not found.");

            EnsureStatus(payment, "marked paid out", PaymentState.ReleasableToHost);

            var result = await _payoutExecutor.ExecuteAsync(payment);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Host payout execution failed: {result.Error}");
            }

            payment.Status = PaymentState.PaidOutToHost.ToString();
            payment.PaidOut = true;
            payment.PaidOutAt = DateTime.UtcNow;
            await _payments.UpdateAsync(payment);

            _logger.LogInformation("Payment {PaymentId} (booking {BookingId}) → PaidOutToHost (ref {Ref}).",
                payment.Id, payment.BookingId, result.Reference);
            return payment;
        }

        private static void EnsureStatus(Payment payment, string action, params PaymentState[] allowedFrom)
        {
            if (!allowedFrom.Any(s => string.Equals(s.ToString(), payment.Status, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Payment cannot be {action} from status '{payment.Status}'. Allowed only from: {string.Join(", ", allowedFrom)}.");
            }
        }

        private async Task<int?> GetListingHostIdAsync(int listingId)
        {
            try
            {
                var listingServiceUrl = _configuration["Services:ListingService"];
                var response = await _httpClient.GetAsync($"{listingServiceUrl}/api/listings/{listingId}/owner");
                if (response.IsSuccessStatusCode && int.TryParse(await response.Content.ReadAsStringAsync(), out var hostId))
                    return hostId;
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving listing owner for {ListingId}", listingId);
                return null;
            }
        }
    }
}
