using WeStay.ListingService.Models;

namespace WeStay.ListingService.Models.Requests
{
    public class SearchListingsRequest
    {
        // Nullable so it isn't treated as implicitly-required by [ApiController] + nullable refs
        // (location is an optional search filter).
        public string? Location { get; set; }
        public DateTime? CheckInDate { get; set; }
        public DateTime? CheckOutDate { get; set; }
        public int? Guests { get; set; }
        public int? Bedrooms { get; set; }
        public int? Beds { get; set; }
        public int? Bathrooms { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public ListingType? Type { get; set; }
        public ListingCategory? Category { get; set; }

        // Optional map bounding-box filter. Provide all four to restrict results to listings
        // whose coordinates fall inside the box (listings without coordinates are excluded).
        public double? MinLatitude { get; set; }
        public double? MaxLatitude { get; set; }
        public double? MinLongitude { get; set; }
        public double? MaxLongitude { get; set; }

        public List<int> AmenityIds { get; set; } = new List<int>();
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "createdAt";
        public bool SortDescending { get; set; } = true;
    }
}