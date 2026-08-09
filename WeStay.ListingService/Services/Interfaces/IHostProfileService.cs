using WeStay.ListingService.Models;

namespace WeStay.ListingService.Services.Interfaces
{
    /// <summary>Fills Listing.HostName / HostIsVerified / HostAvatarUrl from AuthService.</summary>
    public interface IHostProfileService
    {
        Task EnrichAsync(IEnumerable<Listing> listings);
    }
}
