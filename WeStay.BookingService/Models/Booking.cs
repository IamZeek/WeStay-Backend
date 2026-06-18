using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WeStay.BookingService.Models
{
    public class Booking
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(12)]
        public string BookingCode { get; set; } // Unique booking reference (e.g., "WSTAYA1B2C3")

        [Required]
        public int ListingId { get; set; }

        [Required]
        public int UserId { get; set; } // Guest who made the booking (references AuthService Users table)

        [Required]
        [DataType(DataType.Date)]
        public DateTime CheckInDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime CheckOutDate { get; set; }

        [Required]
        [Range(1, 50)]
        public int NumberOfGuests { get; set; }

        [Required]
        [Column(TypeName = "decimal(10, 2)")]
        [Range(0.01, 100000)]
        public decimal TotalPrice { get; set; }

        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = "USD";

        [Required]
        public int StatusId { get; set; }

        [ForeignKey("StatusId")]
        public virtual BookingStatus Status { get; set; }

        // These columns are NOT NULL (non-nullable strings) but are optional at creation time
        // (CancellationReason is only set on cancel). Default to empty to avoid NULL-insert failures.
        [MaxLength(1000)]
        public string SpecialRequests { get; set; } = string.Empty;

        [MaxLength(500)]
        public string CancellationReason { get; set; } = string.Empty;

        public DateTime? CancelledAt { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<BookingGuest> Guests { get; set; } = new List<BookingGuest>();
        public virtual ICollection<BookingPayment> Payments { get; set; } = new List<BookingPayment>();
        // Review navigation moved to /Future (Phase 3 — Reviews). Not part of the active model.
    }

    public class BookingStatus
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Name { get; set; } // Pending, Confirmed, Cancelled, Completed, Refunded

        [MaxLength(255)]
        public string Description { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    }

    public class BookingGuest
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        [Required]
        [MaxLength(100)]
        public string FirstName { get; set; }

        [Required]
        [MaxLength(100)]
        public string LastName { get; set; }

        [EmailAddress]
        [MaxLength(255)]
        public string Email { get; set; }

        [Phone]
        [MaxLength(20)]
        public string PhoneNumber { get; set; }

        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("BookingId")]
        public virtual Booking Booking { get; set; }
    }

    public class BookingPayment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        [MaxLength(255)]
        public string PaymentIntentId { get; set; } // From payment gateway (Stripe, PayPal, etc.)

        [Required]
        [Column(TypeName = "decimal(10, 2)")]
        [Range(0.01, 100000)]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = "USD";

        [Required]
        [MaxLength(50)]
        public string PaymentStatus { get; set; } // pending, succeeded, failed, refunded

        [MaxLength(50)]
        public string PaymentMethod { get; set; } // card, paypal, etc.

        public DateTime? PaidAt { get; set; }

        public DateTime? RefundedAt { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal? RefundAmount { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("BookingId")]
        public virtual Booking Booking { get; set; }
    }

    // NOTE: BookingReview entity moved to /Future (Phase 3 — Reviews). It is preserved
    // there but excluded from compilation and from the active DbContext. Do not re-add here
    // without restoring the DbSet, relationship config, repository, and an EF migration.

    // Enums for better type safety (optional - can use string constants instead)
    public enum PaymentStatus
    {
        Pending,
        Succeeded,
        Failed,
        Refunded
    }

    public enum BookingStatusType
    {
        Pending = 1,
        Confirmed = 2,
        Cancelled = 3,
        Completed = 4,
        Refunded = 5
    }

    public enum PaymentMethod
    {
        Card,
        PayPal,
        BankTransfer,
        Other
    }
}