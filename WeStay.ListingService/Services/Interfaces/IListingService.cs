using WeStay.ListingService.Models;
using WeStay.ListingService.Models.Requests;
using WeStay.ListingService.Models.Requests;
using WeStay.ListingService.Models;

namespace WeStay.ListingService.Services.Interfaces
{
    public interface IListingService
    {
        Task<Listing> GetListingByIdAsync(int id);
        Task<IEnumerable<Listing>> GetListingsByHostIdAsync(int hostId);
        Task<Listing> CreateListingAsync(int hostId, CreateListingRequest request);
        Task<Listing> UpdateListingAsync(int listingId, int hostId, UpdateListingRequest request);
        Task<bool> DeleteListingAsync(int listingId, int hostId);
        Task<bool> ChangeListingStatusAsync(int listingId, int hostId, ListingStatus status);
        // Admin oversight: all listings regardless of status/owner, and an ownership-free status override.
        Task<(IEnumerable<Listing> listings, int totalCount)> GetAllListingsAsync(int page, int pageSize, ListingStatus? status);
        Task<bool> AdminSetStatusAsync(int listingId, ListingStatus status, string? reason);
        Task<bool> SetFeaturedStatusAsync(int listingId, int requestingUserId, bool isAdmin, bool isFeatured, DateTime? featuredUntil);
        Task<int?> GetHostIdAsync(int listingId);
        Task<ListingCategory?> GetCategoryAsync(int listingId);
        Task<bool> UpdateRatingAsync(int listingId, double averageRating, int reviewCount);
    }
}