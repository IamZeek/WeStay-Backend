using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeStay.ListingService.Models.Requests;
using WeStay.ListingService.Security;
using WeStay.ListingService.Services.Interfaces;
using System.Security.Claims;
using WeStay.ListingService.Models.Requests;
using WeStay.ListingService.Models;
using WeStay.ListingService.Services.Interfaces;

namespace WeStay.ListingService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ListingsController : ControllerBase
    {
        private readonly IListingService _listingService;
        private readonly IImageStorageService _imageStorageService;
        private readonly ILogger<ListingsController> _logger;

        public ListingsController(IListingService listingService, IImageStorageService imageStorageService, ILogger<ListingsController> logger)
        {
            _listingService = listingService;
            _imageStorageService = imageStorageService;
            _logger = logger;
        }

        /// <summary>
        /// Get all listings for the authenticated host
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetMyListings()
        {
            try
            {
                var hostId = GetUserId();
                var listings = await _listingService.GetListingsByHostIdAsync(hostId);

                return Ok(listings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting listings for user {UserId}", GetUserId());
                return StatusCode(500, new { Message = "An error occurred while retrieving listings" });
            }
        }

        /// <summary>
        /// Get a specific listing by ID
        /// </summary>
        [HttpGet("{id}")]
        [AllowAnonymous] // Allow public access for listing details
        public async Task<IActionResult> GetListing(int id)
        {
            try
            {
                var listing = await _listingService.GetListingByIdAsync(id);
                if (listing == null)
                {
                    return NotFound(new { Message = "Listing not found" });
                }

                return Ok(listing);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting listing {ListingId}", id);
                return StatusCode(500, new { Message = "An error occurred while retrieving the listing" });
            }
        }

        /// <summary>
        /// Get the nightly price for a listing. Used by WeStay.BookingService for price lookups
        /// (GET /api/listings/{id}/price). Returns the bare decimal value in the response body.
        /// </summary>
        [HttpGet("{id}/price")]
        [AllowAnonymous] // No user JWT...
        [ServiceAuth]    // ...but a valid internal service key is required.
        public async Task<IActionResult> GetListingPrice(int id)
        {
            try
            {
                var listing = await _listingService.GetListingByIdAsync(id);
                if (listing == null)
                {
                    return NotFound(new { Message = "Listing not found" });
                }

                // BookingService reads the body via decimal.Parse, so return the raw value.
                return Ok(listing.PricePerNight);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting price for listing {ListingId}", id);
                return StatusCode(500, new { Message = "An error occurred while retrieving the listing price" });
            }
        }

        /// <summary>
        /// Get the max guest capacity for a listing. Used by WeStay.BookingService to validate
        /// guest counts (GET /api/listings/{id}/capacity). Returns the bare integer in the body.
        /// </summary>
        [HttpGet("{id}/capacity")]
        [AllowAnonymous] // No user JWT...
        [ServiceAuth]    // ...but a valid internal service key is required.
        public async Task<IActionResult> GetListingCapacity(int id)
        {
            try
            {
                var listing = await _listingService.GetListingByIdAsync(id);
                if (listing == null)
                {
                    return NotFound(new { Message = "Listing not found" });
                }

                return Ok(listing.Guests);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting capacity for listing {ListingId}", id);
                return StatusCode(500, new { Message = "An error occurred while retrieving the listing capacity" });
            }
        }

        /// <summary>
        /// Get the owner (HostId) of a listing. Used by WeStay.BookingService to verify a host owns
        /// the listing before confirming/rejecting its bookings (GET /api/listings/{id}/owner).
        /// Returns the bare integer HostId.
        /// </summary>
        [HttpGet("{id}/owner")]
        [AllowAnonymous] // No user JWT...
        [ServiceAuth]    // ...but a valid internal service key is required.
        public async Task<IActionResult> GetListingOwner(int id)
        {
            try
            {
                var hostId = await _listingService.GetHostIdAsync(id);
                if (hostId == null)
                {
                    return NotFound(new { Message = "Listing not found" });
                }

                return Ok(hostId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting owner for listing {ListingId}", id);
                return StatusCode(500, new { Message = "An error occurred while retrieving the listing owner" });
            }
        }

        /// <summary>
        /// Get a listing's category (0=ShortTerm, 1=LongTerm, 2=Sale). Used by WeStay.BookingService
        /// to decide whether platform fees apply (ShortTerm only). Returns the bare integer value.
        /// </summary>
        [HttpGet("{id}/category")]
        [AllowAnonymous] // No user JWT...
        [ServiceAuth]    // ...but a valid internal service key is required.
        public async Task<IActionResult> GetListingCategory(int id)
        {
            try
            {
                var category = await _listingService.GetCategoryAsync(id);
                if (category == null)
                {
                    return NotFound(new { Message = "Listing not found" });
                }

                return Ok((int)category.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting category for listing {ListingId}", id);
                return StatusCode(500, new { Message = "An error occurred while retrieving the listing category" });
            }
        }

        /// <summary>
        /// Refresh a listing's cached review aggregates (AverageRating, ReviewCount). Called
        /// service-to-service by WeStay.ReviewService after each review mutation
        /// (PUT /api/listings/{id}/rating).
        /// </summary>
        [HttpPut("{id}/rating")]
        [AllowAnonymous] // No user JWT...
        [ServiceAuth]    // ...but a valid internal service key is required.
        public async Task<IActionResult> UpdateRating(int id, [FromBody] UpdateRatingRequest request)
        {
            try
            {
                var updated = await _listingService.UpdateRatingAsync(id, request.AverageRating, request.ReviewCount);
                if (!updated)
                {
                    return NotFound(new { Message = "Listing not found" });
                }

                return Ok(new { Message = "Rating cache updated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating rating cache for listing {ListingId}", id);
                return StatusCode(500, new { Message = "An error occurred while updating the rating cache" });
            }
        }

        /// <summary>
        /// Upload a listing image to blob storage and return its public URL.
        /// The returned URL is then passed in CreateListing/UpdateListing's ImageUrls
        /// (stored as ListingImage.ImageUrl). Multipart/form-data field name: "file".
        /// </summary>
        [HttpPost("upload-image")]
        [Authorize]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { Message = "No file provided" });
                }

                // Validate file size (max 10MB)
                if (file.Length > 10 * 1024 * 1024)
                {
                    return BadRequest(new { Message = "File size must be less than 10MB" });
                }

                // Validate image type
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                {
                    return BadRequest(new { Message = "File type not allowed. Allowed: jpg, jpeg, png, gif, webp" });
                }

                var imageUrl = await _imageStorageService.UploadImageAsync(file);
                return Ok(new { ImageUrl = imageUrl });
            }
            catch (InvalidOperationException ex)
            {
                // Storage not configured (missing connection string).
                _logger.LogError(ex, "Image storage is not configured");
                return StatusCode(500, new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading listing image");
                return StatusCode(500, new { Message = "An error occurred while uploading the image" });
            }
        }

        /// <summary>
        /// Create a new listing
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateListing([FromBody] CreateListingRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid listing data", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                var hostId = GetUserId();
                var listing = await _listingService.CreateListingAsync(hostId, request);

                return CreatedAtAction(nameof(GetListing), new { id = listing.Id }, listing);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating listing for user {UserId}", GetUserId());
                return StatusCode(500, new { Message = "An error occurred while creating the listing" });
            }
        }

        /// <summary>
        /// Update a listing
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateListing(int id, [FromBody] UpdateListingRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid listing data", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                var hostId = GetUserId();
                var listing = await _listingService.UpdateListingAsync(id, hostId, request);

                return Ok(listing);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating listing {ListingId} for user {UserId}", id, GetUserId());
                return StatusCode(500, new { Message = "An error occurred while updating the listing" });
            }
        }

        /// <summary>
        /// Delete a listing
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteListing(int id)
        {
            try
            {
                var hostId = GetUserId();
                var result = await _listingService.DeleteListingAsync(id, hostId);

                if (!result)
                {
                    return NotFound(new { Message = "Listing not found or you don't have permission to delete it" });
                }

                return Ok(new { Message = "Listing deleted successfully" });
            }
            catch (InvalidOperationException ex)
            {
                // Listing is admin-Banned — the owner cannot delete/soft-deactivate it.
                return StatusCode(403, new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting listing {ListingId} for user {UserId}", id, GetUserId());
                return StatusCode(500, new { Message = "An error occurred while deleting the listing" });
            }
        }

        /// <summary>
        /// Change listing status
        /// </summary>
        [HttpPatch("{id}/status")]
        public async Task<IActionResult> ChangeListingStatus(int id, [FromBody] ChangeStatusRequest request)
        {
            try
            {
                var hostId = GetUserId();
                var result = await _listingService.ChangeListingStatusAsync(id, hostId, request.Status);

                if (!result)
                {
                    return NotFound(new { Message = "Listing not found or you don't have permission to update it" });
                }

                return Ok(new { Message = $"Listing status changed to {request.Status}" });
            }
            catch (InvalidOperationException ex)
            {
                // Listing is admin-Banned — the owner cannot change its status (only Admin can reactivate).
                return StatusCode(403, new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing status for listing {ListingId} for user {UserId}", id, GetUserId());
                return StatusCode(500, new { Message = "An error occurred while changing listing status" });
            }
        }

        /// <summary>
        /// Toggle a listing's featured/boosted status. Host (own listings) or Admin (any listing).
        /// No payment is involved yet — this just sets the data model.
        /// </summary>
        [HttpPost("{id}/feature")]
        [Authorize(Roles = "Host,Admin")]
        public async Task<IActionResult> SetFeatured(int id, [FromBody] SetFeaturedRequest request)
        {
            try
            {
                var userId = GetUserId();
                var isAdmin = User.IsInRole("Admin");

                var result = await _listingService.SetFeaturedStatusAsync(
                    id, userId, isAdmin, request.IsFeatured, request.FeaturedUntil);

                if (!result)
                {
                    return NotFound(new { Message = "Listing not found" });
                }

                return Ok(new { Message = request.IsFeatured ? "Listing featured" : "Listing unfeatured" });
            }
            catch (UnauthorizedAccessException)
            {
                // Listing exists but the requesting Host is not its owner.
                return StatusCode(403, new { Message = "You can only feature your own listings." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting featured status for listing {ListingId}", id);
                return StatusCode(500, new { Message = "An error occurred while updating featured status" });
            }
        }

        // ===== Admin moderation (Admin-only; gateway forwards via the existing /api/listings/* routes,
        // the service enforces the role — same pattern as BookingService's /jobs/* admin triggers). =====

        /// <summary>
        /// Admin oversight: paginated list of ALL listings regardless of status or owner, optionally
        /// filtered by status. Each item carries its owner (HostId).
        /// </summary>
        [HttpGet("admin/all")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminGetAllListings([FromQuery] ListingStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20;

                var (listings, totalCount) = await _listingService.GetAllListingsAsync(page, pageSize, status);
                var items = listings.Select(l => new
                {
                    l.Id,
                    l.HostId,
                    l.Title,
                    l.City,
                    l.Country,
                    Status = l.Status.ToString(),
                    l.PricePerNight,
                    l.IsFeatured,
                    l.CreatedAt
                });

                return Ok(new { Page = page, PageSize = pageSize, TotalCount = totalCount, Items = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing all listings (admin)");
                return StatusCode(500, new { Message = "An error occurred while listing listings" });
            }
        }

        /// <summary>
        /// Admin force-deactivate (e.g. policy violation) — sets status to Banned with NO ownership
        /// check. Distinct from the owner-initiated status change. Optional reason (logged).
        /// </summary>
        [HttpPost("{id}/admin/deactivate")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminDeactivate(int id, [FromBody] AdminListingActionRequest? request)
        {
            try
            {
                var ok = await _listingService.AdminSetStatusAsync(id, ListingStatus.Banned, request?.Reason);
                if (!ok) return NotFound(new { Message = "Listing not found" });
                return Ok(new { Message = "Listing deactivated by admin", Id = id, Status = ListingStatus.Banned.ToString() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error admin-deactivating listing {ListingId}", id);
                return StatusCode(500, new { Message = "An error occurred while deactivating the listing" });
            }
        }

        /// <summary>Admin reactivate — reverse of the force-deactivate; sets status back to Active.</summary>
        [HttpPost("{id}/admin/reactivate")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminReactivate(int id)
        {
            try
            {
                var ok = await _listingService.AdminSetStatusAsync(id, ListingStatus.Active, null);
                if (!ok) return NotFound(new { Message = "Listing not found" });
                return Ok(new { Message = "Listing reactivated by admin", Id = id, Status = ListingStatus.Active.ToString() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error admin-reactivating listing {ListingId}", id);
                return StatusCode(500, new { Message = "An error occurred while reactivating the listing" });
            }
        }

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }
    }

    public class ChangeStatusRequest
    {
        public ListingStatus Status { get; set; }
    }

    public class AdminListingActionRequest
    {
        public string? Reason { get; set; }
    }

    public class SetFeaturedRequest
    {
        public bool IsFeatured { get; set; }
        public DateTime? FeaturedUntil { get; set; }
    }

    public class UpdateRatingRequest
    {
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
    }
}