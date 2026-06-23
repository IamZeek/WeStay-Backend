using Microsoft.EntityFrameworkCore;
using WeStay.BookingService.Data;
using WeStay.BookingService.Models;
using WeStay.BookingService.Repositories.Interfaces;

namespace WeStay.BookingService.Repositories
{
    public class PlatformFeeConfigRepository : IPlatformFeeConfigRepository
    {
        private readonly BookingDbContext _context;

        public PlatformFeeConfigRepository(BookingDbContext context)
        {
            _context = context;
        }

        public async Task<PlatformFeeConfig> GetAsync()
        {
            var config = await _context.PlatformFeeConfigs.OrderBy(c => c.Id).FirstOrDefaultAsync();

            // Defensive: if the seeded row is somehow missing, fall back to ZERO fees (never overcharge).
            return config ?? new PlatformFeeConfig { Id = 1, GuestServiceFee = 0m, HostPlatformFee = 0m, UpdatedAt = DateTime.UtcNow };
        }

        public async Task<PlatformFeeConfig> UpdateAsync(decimal guestServiceFee, decimal hostPlatformFee)
        {
            var config = await _context.PlatformFeeConfigs.OrderBy(c => c.Id).FirstOrDefaultAsync();
            if (config == null)
            {
                config = new PlatformFeeConfig();
                _context.PlatformFeeConfigs.Add(config);
            }

            config.GuestServiceFee = guestServiceFee;
            config.HostPlatformFee = hostPlatformFee;
            config.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return config;
        }
    }
}
