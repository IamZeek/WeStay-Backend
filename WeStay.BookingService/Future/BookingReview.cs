// =============================================================================
// NOT USED YET — Phase 3 (Reviews).
//
// This entity was moved out of WeStay.BookingService's active model during the
// Phase 1 de-duplication. Reviews are deferred to Phase 3 (WeStay.ReviewService
// is not built for Phase 1). It has been removed from BookingDbContext (DbSet +
// OnModelCreating relationship) and from the Booking model's navigation, and the
// /Future folder is excluded from compilation via <Compile Remove="Future\**" />
// in the .csproj.
//
// DO NOT DELETE — Phase 3 will reactivate this. To reactivate: move this file
// back to Models/, re-add `public virtual BookingReview Review { get; set; }` to
// Booking, re-add the DbSet<BookingReview> + relationship config in
// BookingDbContext, restore the repository + DI registration, and create an EF
// migration. (The original migration 20250824003951_AddInitialBooking still
// creates the BookingReviews table.)
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WeStay.BookingService.Models
{
    public class BookingReview
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        [Required]
        [Range(1, 5)]
        public int Rating { get; set; } // 1-5 stars

        [MaxLength(200)]
        public string Title { get; set; }

        [MaxLength(2000)]
        public string Comment { get; set; }

        [MaxLength(2000)]
        public string HostResponse { get; set; }

        public bool IsPublished { get; set; } = false;

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("BookingId")]
        public virtual Booking Booking { get; set; }
    }
}
