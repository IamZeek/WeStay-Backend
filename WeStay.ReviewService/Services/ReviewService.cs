using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WeStay.ReviewService.DTOs;
using WeStay.ReviewService.Models;

namespace WeStay.ReviewService.Services
{
    public class ReviewService : IReviewService
    {
        // How long after creation a reviewer may still edit their review.
        private const int EditWindowDays = 30;

        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        private readonly Data.ReviewDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ReviewService> _logger;

        public ReviewService(
            Data.ReviewDbContext context,
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<ReviewService> logger)
        {
            _context = context;
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<Review> CreateReviewAsync(int reviewerId, CreateReviewRequest request)
        {
            // (a) Booking must exist (fetched from BookingService) and be Completed.
            var info = await GetBookingInfoAsync(request.BookingId);
            if (info == null)
            {
                throw new KeyNotFoundException("Booking not found.");
            }

            if (!string.Equals(info.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"You can only review a completed booking. This booking's status is '{info.Status}'.");
            }

            // (b) The caller must be the guest who made the booking.
            if (info.UserId != reviewerId)
            {
                throw new UnauthorizedAccessException("Only the guest who made this booking can review it.");
            }

            // (c) One review per booking.
            var alreadyReviewed = await _context.Reviews.AnyAsync(r => r.BookingId == request.BookingId);
            if (alreadyReviewed)
            {
                throw new ReviewConflictException("A review already exists for this booking.");
            }

            var review = new Review
            {
                ListingId = info.ListingId, // derived from the booking, not trusted from the client
                BookingId = request.BookingId,
                ReviewerId = reviewerId,
                Rating = request.Rating,
                Comment = request.Comment,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            await UpdateListingRatingCacheAsync(review.ListingId);

            _logger.LogInformation("Created review {ReviewId} for booking {BookingId} on listing {ListingId}",
                review.Id, review.BookingId, review.ListingId);
            return review;
        }

        public async Task<(IEnumerable<Review> reviews, int totalCount)> GetListingReviewsAsync(int listingId, int page, int pageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var query = _context.Reviews.Where(r => r.ListingId == listingId);
            var total = await query.CountAsync();
            var reviews = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (reviews, total);
        }

        public async Task<(double averageRating, int reviewCount)> GetListingSummaryAsync(int listingId)
        {
            var query = _context.Reviews.Where(r => r.ListingId == listingId);
            var count = await query.CountAsync();
            var average = count == 0 ? 0d : await query.AverageAsync(r => (double)r.Rating);
            return (Math.Round(average, 2), count);
        }

        public async Task<IEnumerable<Review>> GetUserReviewsAsync(int userId)
        {
            return await _context.Reviews
                .Where(r => r.ReviewerId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        public async Task<Review> UpdateReviewAsync(int reviewId, int requestingUserId, UpdateReviewRequest request)
        {
            var review = await _context.Reviews.FindAsync(reviewId);
            if (review == null)
            {
                throw new KeyNotFoundException("Review not found.");
            }

            if (review.ReviewerId != requestingUserId)
            {
                throw new UnauthorizedAccessException("Only the original reviewer can edit this review.");
            }

            if ((DateTime.UtcNow - review.CreatedAt).TotalDays > EditWindowDays)
            {
                throw new InvalidOperationException(
                    $"This review can no longer be edited; the {EditWindowDays}-day edit window has passed.");
            }

            review.Rating = request.Rating;
            review.Comment = request.Comment;
            review.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await UpdateListingRatingCacheAsync(review.ListingId);
            return review;
        }

        public async Task DeleteReviewAsync(int reviewId, int requestingUserId, bool isAdmin)
        {
            var review = await _context.Reviews.FindAsync(reviewId);
            if (review == null)
            {
                throw new KeyNotFoundException("Review not found.");
            }

            if (review.ReviewerId != requestingUserId && !isAdmin)
            {
                throw new UnauthorizedAccessException("Only the reviewer or an admin can delete this review.");
            }

            var listingId = review.ListingId;
            _context.Reviews.Remove(review);
            await _context.SaveChangesAsync();

            await UpdateListingRatingCacheAsync(listingId);
        }

        // --- Cross-service helpers (same HttpClient pattern as BookingService's price/capacity calls) ---

        private async Task<BookingInfoDto?> GetBookingInfoAsync(int bookingId)
        {
            try
            {
                var bookingServiceUrl = _configuration["Services:BookingService"];
                var response = await _httpClient.GetAsync($"{bookingServiceUrl}/api/bookings/{bookingId}/info");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<BookingInfoDto>(content, JsonOpts);
                }

                _logger.LogWarning("BookingService returned {Status} for booking info {BookingId}",
                    (int)response.StatusCode, bookingId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching booking info {BookingId}", bookingId);
                return null;
            }
        }

        // Best-effort: push the listing's fresh average/count to ListingService's cached fields so
        // search can sort by rating cheaply. Failures are logged, never bubbled (the review itself
        // is already persisted, and the listing summary endpoint remains the source of truth).
        private async Task UpdateListingRatingCacheAsync(int listingId)
        {
            try
            {
                var (average, count) = await GetListingSummaryAsync(listingId);
                var listingServiceUrl = _configuration["Services:ListingService"];
                var payload = JsonSerializer.Serialize(new { AverageRating = average, ReviewCount = count });
                using var body = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync($"{listingServiceUrl}/api/listings/{listingId}/rating", body);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ListingService returned {Status} updating rating cache for listing {ListingId}",
                        (int)response.StatusCode, listingId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating listing rating cache for listing {ListingId}", listingId);
            }
        }
    }
}
