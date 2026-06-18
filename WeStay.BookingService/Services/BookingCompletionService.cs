using Microsoft.Extensions.DependencyInjection;
using WeStay.BookingService.Services.Interfaces;

namespace WeStay.BookingService.Services
{
    /// <summary>
    /// Background job: every interval, transitions Confirmed bookings whose checkout has passed
    /// to Completed. Same pattern as NotificationService's NotificationProcessorService.
    /// Interval is configurable via "Booking:AutoJobIntervalMinutes" (default 60).
    /// </summary>
    public class BookingCompletionService : BackgroundService
    {
        private readonly ILogger<BookingCompletionService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval;

        public BookingCompletionService(
            ILogger<BookingCompletionService> logger,
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
            _logger.LogInformation("Booking Completion Service is starting (interval {Interval}).", _interval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                        var count = await bookingService.AutoCompleteExpiredBookingsAsync(DateTime.UtcNow);
                        _logger.LogInformation("Auto-complete run finished; {Count} booking(s) completed. Waiting for next interval...", count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during the booking auto-complete batch");
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("Booking Completion Service is stopping.");
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Booking Completion Service is stopping.");
            await base.StopAsync(stoppingToken);
        }
    }
}
