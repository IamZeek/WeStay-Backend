namespace WeStay.Tests.Infrastructure
{
    // Mirrors WeStay.ListingService.Models.ListingCategory (tests are HTTP-only; no project ref).
    public enum ListingCategory
    {
        ShortTerm = 0,
        LongTerm = 1,
        Sale = 2
    }

    // Response shapes (subset). Deserialized case-insensitively, so camelCase JSON maps to these.
    public record AuthResponse(string Token);

    public record BecomeHostResponse(string Token, string Role, string Message);

    public record ListingDto(
        int Id,
        int HostId,
        int Type,
        int Category,
        int Guests,
        decimal PricePerNight,
        double? Latitude,
        double? Longitude,
        bool IsFeatured);

    public record SearchResponse(List<ListingDto> Listings, int TotalCount);

    public record CalendarDay(DateTime Date, bool IsAvailable);

    public record CalendarResponse(int ListingId, List<CalendarDay> Calendar);

    // Booking create response: { message, booking: { id, bookingCode, status, ... } }
    public record BookingCreatedEnvelope(BookingCreated Booking);
    public record BookingCreated(int Id, string BookingCode, string Status);

    // Confirm/reject response: { message, booking: { id, status, ... } }
    public record BookingActionResponse(string Message, BookingActionBooking Booking);
    public record BookingActionBooking(int Id, string Status);

    // Review responses
    public record ReviewDto(int Id, int ListingId, int BookingId, int ReviewerId, int Rating, string? Comment);
    public record ReviewSummaryDto(int ListingId, double AverageRating, int ReviewCount);

    // Subset of BookingService's GET /api/bookings/{id}/info
    public record BookingStatusInfo(int BookingId, string Status);
}
