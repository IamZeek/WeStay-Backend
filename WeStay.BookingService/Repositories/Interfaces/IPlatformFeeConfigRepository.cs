using WeStay.BookingService.Models;

namespace WeStay.BookingService.Repositories.Interfaces
{
    public interface IPlatformFeeConfigRepository
    {
        Task<PlatformFeeConfig> GetAsync();
        Task<PlatformFeeConfig> UpdateAsync(decimal guestServiceFee, decimal hostPlatformFee);
    }
}
