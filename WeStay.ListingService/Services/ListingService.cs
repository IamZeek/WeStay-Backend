using Microsoft.EntityFrameworkCore;
using WeStay.ListingService.Data;
using WeStay.ListingService.Models;
using WeStay.ListingService.Models.Requests;
using WeStay.ListingService.Services.Interfaces;
using WeStay.ListingService.Data;
using WeStay.ListingService.Models.Requests;
using WeStay.ListingService.Models;
using WeStay.ListingService.Services.Interfaces;

namespace WeStay.ListingService.Services
{
    public class ListingService : IListingService
    {
        private readonly ListingDbContext _context;
        private readonly ILogger<ListingService> _logger;

        public ListingService(ListingDbContext context, ILogger<ListingService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<Listing> GetListingByIdAsync(int id)
        {
            return await _context.Listings
                .Include(l => l.Amenities)
                .Include(l => l.Images)
                .FirstOrDefaultAsync(l => l.Id == id && l.Status == ListingStatus.Active);
        }

        public async Task<bool> UpdateRatingAsync(int listingId, double averageRating, int reviewCount)
        {
            var listing = await _context.Listings.FirstOrDefaultAsync(l => l.Id == listingId);
            if (listing == null)
            {
                return false;
            }

            // Derived cache from ReviewService — not a content edit, so UpdatedAt is left untouched.
            listing.AverageRating = averageRating;
            listing.ReviewCount = reviewCount;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int?> GetHostIdAsync(int listingId)
        {
            // Ownership lookup for service-to-service checks (e.g. BookingService confirm/reject).
            // Not filtered by status, so a host can still act on bookings if the listing is inactive.
            return await _context.Listings
                .Where(l => l.Id == listingId)
                .Select(l => (int?)l.HostId)
                .FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<Listing>> GetListingsByHostIdAsync(int hostId)
        {
            return await _context.Listings
                .Include(l => l.Amenities)
                .Include(l => l.Images)
                .Where(l => l.HostId == hostId)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();
        }

        public async Task<Listing> CreateListingAsync(int hostId, CreateListingRequest request)
        {
            var listing = new Listing
            {
                HostId = hostId,
                Title = request.Title,
                Description = request.Description,
                Type = request.Type,
                Category = request.Category,
                Guests = request.Guests,
                Bedrooms = request.Bedrooms,
                Beds = request.Beds,
                Bathrooms = request.Bathrooms,
                PricePerNight = request.PricePerNight,
                Address = request.Address,
                City = request.City,
                State = request.State,
                Country = request.Country,
                ZipCode = request.ZipCode,
                Status = ListingStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Add amenities
            if (request.AmenityIds != null && request.AmenityIds.Any())
            {
                var amenities = await _context.Amenities
                    .Where(a => request.AmenityIds.Contains(a.Id))
                    .ToListAsync();
                listing.Amenities = amenities;
            }

            // Add images
            if (request.ImageUrls != null && request.ImageUrls.Any())
            {
                listing.Images = request.ImageUrls.Select((url, index) => new ListingImage
                {
                    ImageUrl = url,
                    IsPrimary = index == 0,
                    DisplayOrder = index,
                    CreatedAt = DateTime.UtcNow
                }).ToList();
            }

            _context.Listings.Add(listing);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created listing {ListingId} for host {HostId}", listing.Id, hostId);

            return await GetListingByIdAsync(listing.Id);
        }

        public async Task<Listing> UpdateListingAsync(int listingId, int hostId, UpdateListingRequest request)
        {
            var listing = await _context.Listings
                .Include(l => l.Amenities)
                .Include(l => l.Images)
                .FirstOrDefaultAsync(l => l.Id == listingId && l.HostId == hostId);

            if (listing == null)
            {
                throw new KeyNotFoundException("Listing not found or you don't have permission to update it");
            }

            // Update properties if provided
            if (!string.IsNullOrEmpty(request.Title)) listing.Title = request.Title;
            if (!string.IsNullOrEmpty(request.Description)) listing.Description = request.Description;
            if (request.Category.HasValue) listing.Category = request.Category.Value;
            if (request.Guests.HasValue) listing.Guests = request.Guests.Value;
            if (request.Bedrooms.HasValue) listing.Bedrooms = request.Bedrooms.Value;
            if (request.Beds.HasValue) listing.Beds = request.Beds.Value;
            if (request.Bathrooms.HasValue) listing.Bathrooms = request.Bathrooms.Value;
            if (request.PricePerNight.HasValue) listing.PricePerNight = request.PricePerNight.Value;
            if (!string.IsNullOrEmpty(request.Address)) listing.Address = request.Address;
            if (!string.IsNullOrEmpty(request.City)) listing.City = request.City;
            if (!string.IsNullOrEmpty(request.State)) listing.State = request.State;
            if (!string.IsNullOrEmpty(request.Country)) listing.Country = request.Country;
            if (!string.IsNullOrEmpty(request.ZipCode)) listing.ZipCode = request.ZipCode;

            listing.UpdatedAt = DateTime.UtcNow;

            // Update amenities if provided
            if (request.AmenityIds != null)
            {
                var amenities = await _context.Amenities
                    .Where(a => request.AmenityIds.Contains(a.Id))
                    .ToListAsync();
                listing.Amenities = amenities;
            }

            // Update images if provided
            if (request.ImageUrls != null)
            {
                // Remove existing images
                _context.ListingImages.RemoveRange(listing.Images);

                // Add new images
                listing.Images = request.ImageUrls.Select((url, index) => new ListingImage
                {
                    ImageUrl = url,
                    IsPrimary = index == 0,
                    DisplayOrder = index,
                    CreatedAt = DateTime.UtcNow
                }).ToList();
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated listing {ListingId} for host {HostId}", listingId, hostId);

            return await GetListingByIdAsync(listingId);
        }

        public async Task<bool> DeleteListingAsync(int listingId, int hostId)
        {
            var listing = await _context.Listings
                .FirstOrDefaultAsync(l => l.Id == listingId && l.HostId == hostId);

            if (listing == null)
            {
                return false;
            }

            // A soft-delete flips status to Inactive, which would clear an admin Banned state — block it,
            // same as ChangeListingStatusAsync, so the owner can't delete their way around a ban.
            if (listing.Status == ListingStatus.Banned)
            {
                throw new InvalidOperationException(
                    "This listing was deactivated by an administrator and can only be reactivated by an admin.");
            }

            // Soft delete by changing status
            listing.Status = ListingStatus.Inactive;
            listing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted listing {ListingId} for host {HostId}", listingId, hostId);

            return true;
        }

        public async Task<bool> SetFeaturedStatusAsync(int listingId, int requestingUserId, bool isAdmin, bool isFeatured, DateTime? featuredUntil)
        {
            var listing = await _context.Listings.FirstOrDefaultAsync(l => l.Id == listingId);
            if (listing == null)
            {
                return false; // -> 404 Not Found
            }

            // Hosts may only feature their own listings; Admins may feature any listing.
            // Distinguish "exists but not yours" (403) from "doesn't exist" (404).
            if (!isAdmin && listing.HostId != requestingUserId)
            {
                throw new UnauthorizedAccessException("You can only feature your own listings.");
            }

            listing.IsFeatured = isFeatured;
            listing.FeaturedUntil = isFeatured ? featuredUntil : null;
            listing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Set IsFeatured={IsFeatured} (until {FeaturedUntil}) on listing {ListingId} by user {UserId} (admin={IsAdmin})",
                isFeatured, featuredUntil, listingId, requestingUserId, isAdmin);

            return true;
        }

        public async Task<bool> ChangeListingStatusAsync(int listingId, int hostId, ListingStatus status)
        {
            var listing = await _context.Listings
                .FirstOrDefaultAsync(l => l.Id == listingId && l.HostId == hostId);

            if (listing == null)
            {
                return false;
            }

            // An admin ban (Banned) can only be lifted by an admin (AdminSetStatusAsync). The owner
            // must not be able to change a Banned listing's status — reject loudly, never silent no-op.
            if (listing.Status == ListingStatus.Banned)
            {
                throw new InvalidOperationException(
                    "This listing was deactivated by an administrator and can only be reactivated by an admin.");
            }

            listing.Status = status;
            listing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Changed status of listing {ListingId} to {Status} for host {HostId}",
                listingId, status, hostId);

            return true;
        }

        // Admin oversight: ALL listings regardless of status or owner (unlike search/GetById which are
        // Active-only and GetListingsByHostId which is owner-scoped). Owner is the HostId on each row.
        public async Task<(IEnumerable<Listing> listings, int totalCount)> GetAllListingsAsync(int page, int pageSize, ListingStatus? status)
        {
            var query = _context.Listings.Include(l => l.Images).AsQueryable();
            if (status.HasValue)
            {
                query = query.Where(l => l.Status == status.Value);
            }

            var totalCount = await query.CountAsync();
            var listings = await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (listings, totalCount);
        }

        // Admin override: set any listing's status with NO ownership check (distinct from the
        // owner-initiated ChangeListingStatusAsync). The optional reason is logged (no schema field
        // for it yet — see PROJECT_STATUS.md if persistence is wanted).
        public async Task<bool> AdminSetStatusAsync(int listingId, ListingStatus status, string? reason)
        {
            var listing = await _context.Listings.FirstOrDefaultAsync(l => l.Id == listingId);
            if (listing == null)
            {
                return false;
            }

            listing.Status = status;
            listing.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("ADMIN set status of listing {ListingId} to {Status}. Reason: {Reason}",
                listingId, status, string.IsNullOrWhiteSpace(reason) ? "(none)" : reason);

            return true;
        }
    }
}