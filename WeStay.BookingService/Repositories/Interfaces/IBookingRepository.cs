using WeStay.BookingService.Models;

namespace WeStay.BookingService.Repositories.Interfaces
{
    public interface IBookingRepository
    {
        Task<Booking> GetBookingByIdAsync(int id);
        Task<Booking> GetBookingByCodeAsync(string bookingCode);
        Task<IEnumerable<Booking>> GetBookingsByUserIdAsync(int userId);
        Task<IEnumerable<Booking>> GetBookingsByListingIdAsync(int listingId);
        Task<IEnumerable<Booking>> GetBookingsByStatusAsync(int statusId);
        Task<Booking> CreateBookingAsync(Booking booking);
        Task<Booking> UpdateBookingAsync(Booking booking);
        Task<bool> DeleteBookingAsync(int id);
        Task<bool> IsListingAvailableAsync(int listingId, DateTime checkInDate, DateTime checkOutDate, int? excludeBookingId = null);
        Task<List<int>> GetBookingIdsByStatusPastCheckoutAsync(int statusId, DateTime asOfUtc);
        Task<List<int>> GetBookingIdsByStatusCreatedBeforeAsync(int statusId, DateTime cutoffUtc);
    }
}