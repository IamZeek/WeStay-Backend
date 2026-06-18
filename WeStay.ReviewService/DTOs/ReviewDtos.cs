using System.ComponentModel.DataAnnotations;

namespace WeStay.ReviewService.DTOs
{
    public class CreateReviewRequest
    {
        [Required]
        public int BookingId { get; set; }

        [Required]
        [Range(1, 5)]
        public int Rating { get; set; }

        [MaxLength(2000)]
        public string? Comment { get; set; }
    }

    public class UpdateReviewRequest
    {
        [Required]
        [Range(1, 5)]
        public int Rating { get; set; }

        [MaxLength(2000)]
        public string? Comment { get; set; }
    }

    public class ReviewResponse
    {
        public int Id { get; set; }
        public int ListingId { get; set; }
        public int BookingId { get; set; }
        public int ReviewerId { get; set; }
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public string? HostReply { get; set; }
        public DateTime? HostReplyAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ReviewSummaryResponse
    {
        public int ListingId { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
    }

    // Shape of WeStay.BookingService's GET /api/bookings/{id}/info response.
    public class BookingInfoDto
    {
        public int BookingId { get; set; }
        public int ListingId { get; set; }
        public int UserId { get; set; }
        public string? Status { get; set; }
    }
}
