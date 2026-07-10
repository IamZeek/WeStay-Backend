using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WeStay.BookingService.Models
{
    /// <summary>
    /// The SafePay payment states. WeStay COLLECTS and HOLDS the money (SafePay is a gateway, not an
    /// escrow) — the "hold until checkout, then release to host" lifecycle lives here in our DB.
    ///
    ///   Pending ─► Paid ─► HeldForStay ─► ReleasableToHost ─► PaidOutToHost
    ///      │         │          │
    ///      ▼         ▼          ▼
    ///   Failed    Refunded   Refunded         (Refunded allowed ONLY pre-release)
    /// </summary>
    public enum PaymentState
    {
        Pending,          // created, awaiting SafePay checkout completion
        Paid,             // SafePay confirmed capture (via signed webhook)
        HeldForStay,      // captured funds held by WeStay until the stay completes
        ReleasableToHost, // booking Completed (checkout passed) → host payout is now owed
        PaidOutToHost,    // admin executed the payout (manual now; pluggable executor)
        Failed,           // payment failed/abandoned at SafePay
        Refunded          // guest cancelled a paid booking (pre-release) → refunded minus cancellation fee
    }

    /// <summary>
    /// One payment per booking (unique BookingId). Amounts are PKR major units (no FX). All monetary
    /// amounts are SNAPSHOTTED at their respective transitions and never recalculated.
    /// </summary>
    public class Payment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        [ForeignKey("BookingId")]
        public virtual Booking Booking { get; set; }

        // SafePay tracker token (track_<uuid>); null until /order/v1/init succeeds.
        [MaxLength(64)]
        public string Tracker { get; set; }

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = PaymentState.Pending.ToString();

        // = Booking.GuestTotalPrice snapshot (what the guest is charged).
        [Required]
        [Column(TypeName = "decimal(10, 2)")]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = "PKR";

        // = Booking.HostPayoutAmount snapshot (what the host is owed after the platform fee).
        [Column(TypeName = "decimal(10, 2)")]
        public decimal HostPayoutAmount { get; set; }

        // Set on refund: what the guest got back, and what WeStay kept (the cancellation fee).
        [Column(TypeName = "decimal(10, 2)")]
        public decimal? RefundAmount { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal? CancellationFeeAmount { get; set; }

        [MaxLength(64)]
        public string SafepayRefundRef { get; set; }

        // Payout tracking (the automation structure; execution is a manual admin step for now).
        public bool Releasable { get; set; }
        public bool PaidOut { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Per-state timestamps.
        public DateTime? PaidAt { get; set; }
        public DateTime? HeldAt { get; set; }
        public DateTime? ReleasableAt { get; set; }
        public DateTime? PaidOutAt { get; set; }
        public DateTime? RefundedAt { get; set; }
        public DateTime? FailedAt { get; set; }
    }
}
