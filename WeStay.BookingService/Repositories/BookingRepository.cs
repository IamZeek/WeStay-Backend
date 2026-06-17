using Microsoft.EntityFrameworkCore;
using WeStay.BookingService.Data;
using WeStay.BookingService.Models;
using WeStay.BookingService.Repositories.Interfaces;

namespace WeStay.BookingService.Repositories
{
    public class BookingRepository : IBookingRepository
    {
        private readonly BookingDbContext _context;
        private readonly ILogger<BookingRepository> _logger;

        public BookingRepository(BookingDbContext context, ILogger<BookingRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<Booking> GetBookingByIdAsync(int id)
        {
            return await _context.Bookings
                .Include(b => b.Status)
                .Include(b => b.Guests)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.Id == id);
        }

        public async Task<Booking> GetBookingByCodeAsync(string bookingCode)
        {
            return await _context.Bookings
                .Include(b => b.Status)
                .Include(b => b.Guests)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.BookingCode == bookingCode);
        }

        public async Task<IEnumerable<Booking>> GetBookingsByUserIdAsync(int userId)
        {
            return await _context.Bookings
                .Include(b => b.Status)
                .Include(b => b.Guests)
                .Include(b => b.Payments)
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Booking>> GetBookingsByListingIdAsync(int listingId)
        {
            return await _context.Bookings
                .Include(b => b.Status)
                .Include(b => b.Guests)
                .Where(b => b.ListingId == listingId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<Booking>> GetBookingsByStatusAsync(int statusId)
        {
            return await _context.Bookings
                .Include(b => b.Status)
                .Include(b => b.Guests)
                .Where(b => b.StatusId == statusId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task<Booking> CreateBookingAsync(Booking booking)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Generate unique booking code
                booking.BookingCode = GenerateBookingCode();

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return booking;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<Booking> UpdateBookingAsync(Booking booking)
        {
            booking.UpdatedAt = DateTime.UtcNow;
            _context.Bookings.Update(booking);
            await _context.SaveChangesAsync();
            return booking;
        }

        public async Task<bool> DeleteBookingAsync(int id)
        {
            var booking = await GetBookingByIdAsync(id);
            if (booking == null) return false;

            _context.Bookings.Remove(booking);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> IsListingAvailableAsync(int listingId, DateTime checkInDate, DateTime checkOutDate, int? excludeBookingId = null)
        {
            // Check if there are any conflicting bookings
            var conflictingBookings = await _context.Bookings
                .Where(b => b.ListingId == listingId &&
                           b.StatusId != 3 && // Not cancelled
                           b.StatusId != 5 && // Not refunded
                           ((b.CheckInDate <= checkOutDate && b.CheckOutDate >= checkInDate)))
                .ToListAsync();

            if (excludeBookingId.HasValue)
            {
                conflictingBookings = conflictingBookings.Where(b => b.Id != excludeBookingId.Value).ToList();
            }

            return !conflictingBookings.Any();
        }

        private string GenerateBookingCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}