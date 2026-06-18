using System.ComponentModel.DataAnnotations;
using WeStay.ListingService.Models;

namespace WeStay.ListingService.Models.Requests
{
    public class UpdateListingRequest
    {
        [MaxLength(200)]
        public string Title { get; set; }

        public ListingCategory? Category { get; set; }

        [MaxLength(1000)]
        public string Description { get; set; }

        [Range(1, 50)]
        public int? Guests { get; set; }

        [Range(0, 20)]
        public int? Bedrooms { get; set; }

        [Range(1, 50)]
        public int? Beds { get; set; }

        [Range(0, 20)]
        public int? Bathrooms { get; set; }

        [Range(0, 10000)]
        public decimal? PricePerNight { get; set; }

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

        public List<int> AmenityIds { get; set; }
        public List<string> ImageUrls { get; set; }
    }
}