// =============================================================================
// NOT USED YET — Phase 3 (Reviews).
//
// Moved out of Repositories/Interfaces/ during the Phase 1 de-duplication.
// Reviews are deferred to Phase 3. The /Future folder is excluded from
// compilation via <Compile Remove="Future\**" /> in the .csproj, so this
// interface is preserved but not built or registered.
//
// DO NOT DELETE — see Future/BookingReview.cs for reactivation steps.
// =============================================================================

using WeStay.BookingService.Models;

namespace WeStay.BookingService.Repositories.Interfaces
{
    public interface IBookingReviewRepository
    {
        Task<BookingReview> GetReviewByIdAsync(int id);
        Task<BookingReview> GetReviewByBookingIdAsync(int bookingId);
        Task<IEnumerable<BookingReview>> GetReviewsByListingIdAsync(int listingId);
        Task<IEnumerable<BookingReview>> GetReviewsByUserIdAsync(int userId);
        Task<BookingReview> CreateReviewAsync(BookingReview review);
        Task<BookingReview> UpdateReviewAsync(BookingReview review);
        Task<bool> DeleteReviewAsync(int id);
    }
}
