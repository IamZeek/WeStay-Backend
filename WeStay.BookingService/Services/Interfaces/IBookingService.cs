using WeStay.BookingService.Models;

namespace WeStay.BookingService.Services.Interfaces
{
    public interface IBookingService
    {
        Task<Booking> CreateBookingAsync(Booking booking, List<BookingGuest> guests);
        Task<Booking> GetBookingByIdAsync(int id);
        Task<Booking> GetBookingByCodeAsync(string bookingCode);
        Task<IEnumerable<Booking>> GetUserBookingsAsync(int userId);
        Task<IEnumerable<Booking>> GetListingBookingsAsync(int listingId);
        Task<Booking> CancelBookingAsync(int bookingId, string reason);
        Task<Booking> ConfirmBookingAsync(int bookingId, int requestingUserId, bool isAdmin);
        Task<Booking> RejectBookingAsync(int bookingId, int requestingUserId, bool isAdmin, string reason);
        Task<Booking> CompleteBookingAsync(int bookingId, int requestingUserId, bool isAdmin);
        Task<int> AutoCompleteExpiredBookingsAsync(DateTime asOfUtc);
        Task<int> AutoCancelUnconfirmedBookingsAsync(DateTime asOfUtc);
        Task<bool> IsListingAvailableAsync(int listingId, DateTime checkInDate, DateTime checkOutDate, int? excludeBookingId = null);
        Task<decimal> CalculateBookingPriceAsync(int listingId, DateTime checkInDate, DateTime checkOutDate, int numberOfGuests);
    }
}