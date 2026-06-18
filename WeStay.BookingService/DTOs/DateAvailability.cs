namespace WeStay.BookingService.DTOs
{
    /// <summary>
    /// One day in an availability calendar grid for a listing.
    /// </summary>
    public class DateAvailability
    {
        public DateTime Date { get; set; }
        public bool IsAvailable { get; set; }
    }
}
