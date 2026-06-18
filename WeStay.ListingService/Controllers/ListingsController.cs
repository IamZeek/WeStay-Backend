using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeStay.ListingService.Models.Requests;
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
        [AllowAnonymous] // Called service-to-service by BookingService without a user token
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
        [AllowAnonymous] // Called service-to-service by BookingService without a user token
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

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }
    }

    public class ChangeStatusRequest
    {
        public ListingStatus Status { get; set; }
    }

    public class SetFeaturedRequest
    {
        public bool IsFeatured { get; set; }
        public DateTime? FeaturedUntil { get; set; }
    }
}