using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WeStay.ListingService.Models;

namespace WeStay.ListingService.Models
{
    public enum ListingType
    {
        Appartment,
        Home,
        Room,
        Farmhouse,
        Villa

    }

    public enum ListingStatus
    {
        Active,
        Inactive,
        UnderReview,
        Banned
    }

    public class Listing
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int HostId { get; set; } // User ID from AuthService

        [Required]
        [MaxLength(200)]
        public string Title { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Description { get; set; }

        [Required]
        public ListingType Type { get; set; }

        [Required]
        public int Guests { get; set; }

        [Required]
        public int Bedrooms { get; set; }

        [Required]
        public int Beds { get; set; }

        [Required]
        public int Bathrooms { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PricePerNight { get; set; }

        [Required]
        [MaxLength(200)]
        public string Address { get; set; }

        [MaxLength(100)]
        public string City { get; set; }

        [MaxLength(100)]
        public string State { get; set; }

        [MaxLength(100)]
        public string Country { get; set; }

        [MaxLength(20)]
        public string ZipCode { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        [Required]
        public ListingStatus Status { get; set; } = ListingStatus.Active;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<Amenity> Amenities { get; set; }
        public virtual ICollection<ListingImage> Images { get; set; }
        // Bookings navigation removed: booking ownership moved to WeStay.BookingService.
    }
}