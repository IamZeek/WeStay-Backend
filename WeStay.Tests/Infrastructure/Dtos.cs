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
}
