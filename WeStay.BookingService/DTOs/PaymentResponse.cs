using WeStay.BookingService.Models;

namespace WeStay.BookingService.DTOs
{
    public class PaymentResponse
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public string Tracker { get; set; }
        public string Status { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public decimal HostPayoutAmount { get; set; }
        public decimal? RefundAmount { get; set; }
        public decimal? CancellationFeeAmount { get; set; }
        public bool Releasable { get; set; }
        public bool PaidOut { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? ReleasableAt { get; set; }
        public DateTime? PaidOutAt { get; set; }
        public DateTime? RefundedAt { get; set; }

        public static PaymentResponse From(Payment p) => new()
        {
            Id = p.Id,
            BookingId = p.BookingId,
            Tracker = p.Tracker,
            Status = p.Status,
            Amount = p.Amount,
            Currency = p.Currency,
            HostPayoutAmount = p.HostPayoutAmount,
            RefundAmount = p.RefundAmount,
            CancellationFeeAmount = p.CancellationFeeAmount,
            Releasable = p.Releasable,
            PaidOut = p.PaidOut,
            CreatedAt = p.CreatedAt,
            PaidAt = p.PaidAt,
            ReleasableAt = p.ReleasableAt,
            PaidOutAt = p.PaidOutAt,
            RefundedAt = p.RefundedAt
        };
    }
}
