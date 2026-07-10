using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WeStay.BookingService.Models
{
    /// <summary>
    /// Single-row, global platform-fee configuration for SHORT-TERM bookings. Percentages (0–100).
    /// Admin-editable via GET/PUT /api/admin/fees. Read at booking creation to snapshot fee amounts
    /// onto the Booking — changing these values affects only future bookings, never existing ones.
    /// (Long-term / sale verticals have separate, not-yet-built fee models.)
    /// </summary>
    public class PlatformFeeConfig
    {
        [Key]
        public int Id { get; set; }

        // Charged to the GUEST, added on top of the base price.
        [Column(TypeName = "decimal(5, 2)")]
        [Range(0, 100)]
        public decimal GuestServiceFee { get; set; }

        // Charged to the HOST, deducted from their payout.
        [Column(TypeName = "decimal(5, 2)")]
        [Range(0, 100)]
        public decimal HostPlatformFee { get; set; }

        // Kept by WeStay when a guest cancels a PAID booking; the guest is refunded GuestTotalPrice
        // minus this percentage of GuestTotalPrice. Read from DB at cancellation time.
        [Column(TypeName = "decimal(5, 2)")]
        [Range(0, 100)]
        public decimal CancellationFeePercent { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
