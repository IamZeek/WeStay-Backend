using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WeStay.ListingService.Models;

namespace WeStay.ListingService.Models
{
    public class ListingImage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ListingId { get; set; }

        [ForeignKey("ListingId")]
        public virtual Listing Listing { get; set; }

        [Required]
        [Url]
        [MaxLength(500)]
        public string ImageUrl { get; set; }

        // The DB column is NOT NULL (non-nullable string), but listing creation supplies only
        // image URLs (no captions), so default to empty to avoid a NULL-insert failure.
        [MaxLength(200)]
        public string Caption { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }

        public int DisplayOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}