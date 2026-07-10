using WeStay.BookingService.Models;

namespace WeStay.BookingService.Repositories.Interfaces
{
    public interface IPaymentRepository
    {
        Task<Payment> GetByIdAsync(int id);
        Task<Payment> GetByBookingIdAsync(int bookingId);
        Task<Payment> GetByTrackerAsync(string tracker);
        Task<Payment> CreateAsync(Payment payment);
        Task<Payment> UpdateAsync(Payment payment);
        Task<(IEnumerable<Payment> payments, int totalCount)> GetAllAsync(int page, int pageSize, string status);
    }
}
