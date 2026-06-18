using WeStay.BookingService.DTOs;

namespace WeStay.BookingService.Services.Interfaces
{
    public interface IAvailabilityService
    {
        Task<bool> IsListingAvailableAsync(int listingId, DateTime checkInDate, DateTime checkOutDate, int? excludeBookingId = null);
        Task<IEnumerable<DateTime>> GetUnavailableDatesAsync(int listingId, DateTime startDate, DateTime endDate);
        Task<IEnumerable<DateAvailability>> GetAvailabilityCalendarAsync(int listingId, DateTime startDate, DateTime endDate);
    }
}