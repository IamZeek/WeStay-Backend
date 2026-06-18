using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeStay.ReviewService.DTOs;
using WeStay.ReviewService.Models;
using WeStay.ReviewService.Services;

namespace WeStay.ReviewService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReviewsController : ControllerBase
    {
        private readonly IReviewService _reviewService;
        private readonly ILogger<ReviewsController> _logger;

        public ReviewsController(IReviewService reviewService, ILogger<ReviewsController> logger)
        {
            _reviewService = reviewService;
            _logger = logger;
        }

        /// <summary>
        /// Create a review. Only the guest of a Completed booking may review, once per booking.
        /// </summary>
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateReview([FromBody] CreateReviewRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid review data", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                var reviewerId = GetUserId();
                var review = await _reviewService.CreateReviewAsync(reviewerId, request);

                return CreatedAtAction(nameof(GetListingReviews), new { listingId = review.ListingId }, Map(review));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { Message = ex.Message });
            }
            catch (ReviewConflictException ex)
            {
                return Conflict(new { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating review for booking {BookingId}", request.BookingId);
                return StatusCode(500, new { Message = "An error occurred while creating the review" });
            }
        }

        /// <summary>Paginated reviews for a listing.</summary>
        [HttpGet("listing/{listingId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetListingReviews(int listingId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var (reviews, total) = await _reviewService.GetListingReviewsAsync(listingId, page, pageSize);
                return Ok(new
                {
                    ListingId = listingId,
                    Reviews = reviews.Select(Map),
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = total,
                    TotalPages = (int)Math.Ceiling(total / (double)pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting reviews for listing {ListingId}", listingId);
                return StatusCode(500, new { Message = "An error occurred while retrieving reviews" });
            }
        }

        /// <summary>Average rating + review count for a listing (for search/detail display).</summary>
        [HttpGet("listing/{listingId}/summary")]
        [AllowAnonymous]
        public async Task<IActionResult> GetListingSummary(int listingId)
        {
            try
            {
                var (average, count) = await _reviewService.GetListingSummaryAsync(listingId);
                return Ok(new ReviewSummaryResponse
                {
                    ListingId = listingId,
                    AverageRating = average,
                    ReviewCount = count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting review summary for listing {ListingId}", listingId);
                return StatusCode(500, new { Message = "An error occurred while retrieving the review summary" });
            }
        }

        /// <summary>Reviews written by a user (self or admin).</summary>
        [HttpGet("user/{userId}")]
        [Authorize]
        public async Task<IActionResult> GetUserReviews(int userId)
        {
            try
            {
                if (userId != GetUserId() && !User.IsInRole("Admin"))
                {
                    return Forbid();
                }

                var reviews = await _reviewService.GetUserReviewsAsync(userId);
                return Ok(reviews.Select(Map));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting reviews for user {UserId}", userId);
                return StatusCode(500, new { Message = "An error occurred while retrieving user reviews" });
            }
        }

        /// <summary>Edit a review (original reviewer only, within the edit window).</summary>
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateReview(int id, [FromBody] UpdateReviewRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid review data", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                var review = await _reviewService.UpdateReviewAsync(id, GetUserId(), request);
                return Ok(Map(review));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating review {ReviewId}", id);
                return StatusCode(500, new { Message = "An error occurred while updating the review" });
            }
        }

        /// <summary>Delete a review (reviewer or admin).</summary>
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteReview(int id)
        {
            try
            {
                await _reviewService.DeleteReviewAsync(id, GetUserId(), User.IsInRole("Admin"));
                return Ok(new { Message = "Review deleted successfully" });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting review {ReviewId}", id);
                return StatusCode(500, new { Message = "An error occurred while deleting the review" });
            }
        }

        private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

        private static ReviewResponse Map(Review r) => new()
        {
            Id = r.Id,
            ListingId = r.ListingId,
            BookingId = r.BookingId,
            ReviewerId = r.ReviewerId,
            Rating = r.Rating,
            Comment = r.Comment,
            HostReply = r.HostReply,
            HostReplyAt = r.HostReplyAt,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }
}
