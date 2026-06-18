using WeStay.ReviewService.DTOs;
using WeStay.ReviewService.Models;

namespace WeStay.ReviewService.Services
{
    public interface IReviewService
    {
        Task<Review> CreateReviewAsync(int reviewerId, CreateReviewRequest request);
        Task<(IEnumerable<Review> reviews, int totalCount)> GetListingReviewsAsync(int listingId, int page, int pageSize);
        Task<(double averageRating, int reviewCount)> GetListingSummaryAsync(int listingId);
        Task<IEnumerable<Review>> GetUserReviewsAsync(int userId);
        Task<Review> UpdateReviewAsync(int reviewId, int requestingUserId, UpdateReviewRequest request);
        Task DeleteReviewAsync(int reviewId, int requestingUserId, bool isAdmin);
    }

    /// <summary>Thrown when a review already exists for a booking (maps to HTTP 409).</summary>
    public class ReviewConflictException : Exception
    {
        public ReviewConflictException(string message) : base(message) { }
    }
}
