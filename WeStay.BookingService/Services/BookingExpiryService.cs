using Microsoft.Extensions.DependencyInjection;
using WeStay.BookingService.Services.Interfaces;

namespace WeStay.BookingService.Services
{
    /// <summary>
    /// Background job: every interval, cancels Pending bookings the host never confirmed within the
    /// configured window. Same pattern as NotificationService's NotificationProcessorService.
    /// Interval is "Booking:AutoJobIntervalMinutes" (default 60); window is
    /// "Booking:AutoCancelPendingHours" (default 24).
    /// </summary>
    public class BookingExpiryService : BackgroundService
    {
        private readonly ILogger<BookingExpiryService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval;

        public BookingExpiryService(
            ILogger<BookingExpiryService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            var minutes = int.TryParse(configuration["Booking:AutoJobIntervalMinutes"], out var m) ? m : 60;
            _interval = TimeSpan.FromMinutes(minutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Booking Expiry Service is starting (interval {Interval}).", _interval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                        var count = await bookingService.AutoCancelUnconfirmedBookingsAsync(DateTime.UtcNow);
                        _logger.LogInformation("Auto-cancel run finished; {Count} unconfirmed booking(s) cancelled. Waiting for next interval...", count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during the booking auto-cancel batch");
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("Booking Expiry Service is stopping.");
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Booking Expiry Service is stopping.");
            await base.StopAsync(stoppingToken);
        }
    }
}
