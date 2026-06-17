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
        private readonly ILogger<ListingsController> _logger;

        public ListingsController(IListingService listingService, ILogger<ListingsController> logger)
        {
            _listingService = listingService;
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

        private int GetUserId()
        {
            return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }
    }

    public class ChangeStatusRequest
    {
        public ListingStatus Status { get; set; }
    }
}