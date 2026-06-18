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
        Task<bool> SetFeaturedStatusAsync(int listingId, int requestingUserId, bool isAdmin, bool isFeatured, DateTime? featuredUntil);
        Task<int?> GetHostIdAsync(int listingId);
    }
}