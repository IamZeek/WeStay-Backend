using Microsoft.EntityFrameworkCore;
using WeStay.BookingService.Data;
using WeStay.BookingService.Models;
using WeStay.BookingService.Repositories.Interfaces;

namespace WeStay.BookingService.Repositories
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly BookingDbContext _context;

        public PaymentRepository(BookingDbContext context)
        {
            _context = context;
        }

        public async Task<Payment> GetByIdAsync(int id) =>
            await _context.Payments.Include(p => p.Booking).FirstOrDefaultAsync(p => p.Id == id);

        public async Task<Payment> GetByBookingIdAsync(int bookingId) =>
            await _context.Payments.Include(p => p.Booking).FirstOrDefaultAsync(p => p.BookingId == bookingId);

        public async Task<Payment> GetByTrackerAsync(string tracker) =>
            await _context.Payments.Include(p => p.Booking).FirstOrDefaultAsync(p => p.Tracker == tracker);

        public async Task<Payment> CreateAsync(Payment payment)
        {
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();
            return payment;
        }

        public async Task<Payment> UpdateAsync(Payment payment)
        {
            payment.UpdatedAt = DateTime.UtcNow;
            _context.Payments.Update(payment);
            await _context.SaveChangesAsync();
            return payment;
        }

        public async Task<(IEnumerable<Payment> payments, int totalCount)> GetAllAsync(int page, int pageSize, string status)
        {
            var query = _context.Payments.Include(p => p.Booking).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => p.Status == status);
            }

            var total = await query.CountAsync();
            var payments = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (payments, total);
        }
    }
}
