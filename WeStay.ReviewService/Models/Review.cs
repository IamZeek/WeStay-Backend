using System.ComponentModel.DataAnnotations;

namespace WeStay.ReviewService.Models
{
    public class Review
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ListingId { get; set; }

        // One review per booking (enforced by a unique index in ReviewDbContext).
        [Required]
        public int BookingId { get; set; }

        // The guest (UserId) who wrote the review.
        [Required]
        public int ReviewerId { get; set; }

        [Required]
        [Range(1, 5)]
        public int Rating { get; set; }

        [MaxLength(2000)]
        public string? Comment { get; set; }

        // Host reply to a review. Columns are in place now (forward-compatible); the host-reply
        // endpoint is a clean Phase-3.5 addition.
        [MaxLength(2000)]
        public string? HostReply { get; set; }

        public DateTime? HostReplyAt { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
