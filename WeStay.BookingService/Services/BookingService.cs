using WeStay.BookingService.Models;
using WeStay.BookingService.Repositories.Interfaces;
using WeStay.BookingService.Services.Interfaces;
using WeStay.BookingService.DTOs;
using WeStay.BookingService.Repositories;

namespace WeStay.BookingService.Services
{
    public class BookingService : IBookingService
    {
        private readonly IBookingRepository _bookingRepository;
        private readonly IBookingStatusRepository _statusRepository;
        private readonly IBookingPaymentRepository _paymentRepository;
        private readonly IAvailabilityService _availabilityService;
        private readonly ILogger<BookingService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public BookingService(
            IBookingRepository bookingRepository,
            IBookingStatusRepository statusRepository,
            IBookingPaymentRepository paymentRepository,
            IAvailabilityService availabilityService,
            ILogger<BookingService> logger,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _bookingRepository = bookingRepository;
            _statusRepository = statusRepository;
            _paymentRepository = paymentRepository;
            _availabilityService = availabilityService;
            _logger = logger;
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<Booking> CreateBookingAsync(Booking booking, List<BookingGuest> guests)
        {
            // Check availability
            var isAvailable = await _availabilityService.IsListingAvailableAsync(
                booking.ListingId, booking.CheckInDate, booking.CheckOutDate);

            if (!isAvailable)
            {
                throw new InvalidOperationException("The listing is not available for the selected dates");
            }

            // Validate guest count against the listing's max capacity (fetched from ListingService
            // via the same HTTP-client pattern used for pricing).
            var capacity = await GetListingCapacityAsync(booking.ListingId);
            if (capacity.HasValue && booking.NumberOfGuests > capacity.Value)
            {
                throw new InvalidOperationException($"This listing accommodates at most {capacity.Value} guests.");
            }

            // Get listing details to calculate price
            var listingPrice = await GetListingPriceAsync(booking.ListingId);
            if (listingPrice == null)
            {
                throw new InvalidOperationException("Listing not found");
            }

            // Calculate total price
            var nights = (booking.CheckOutDate - booking.CheckInDate).Days;
            booking.TotalPrice = listingPrice.Value * nights;

            // Set initial status to Pending
            var pendingStatus = await _statusRepository.GetStatusByNameAsync("Pending");
            if (pendingStatus == null)
            {
                throw new InvalidOperationException("Booking status configuration error");
            }
            booking.StatusId = pendingStatus.Id;

            // Create booking
            var createdBooking = await _bookingRepository.CreateBookingAsync(booking);

            // Add guests
            foreach (var guest in guests)
            {
                guest.BookingId = createdBooking.Id;
            }

            _logger.LogInformation("Booking created with ID: {BookingId}, Code: {BookingCode}",
                createdBooking.Id, createdBooking.BookingCode);

            return createdBooking;
        }

        public async Task<Booking> GetBookingByIdAsync(int id)
        {
            return await _bookingRepository.GetBookingByIdAsync(id);
        }

        public async Task<Booking> GetBookingByCodeAsync(string bookingCode)
        {
            return await _bookingRepository.GetBookingByCodeAsync(bookingCode);
        }

        public async Task<IEnumerable<Booking>> GetUserBookingsAsync(int userId)
        {
            return await _bookingRepository.GetBookingsByUserIdAsync(userId);
        }

        public async Task<IEnumerable<Booking>> GetListingBookingsAsync(int listingId)
        {
            return await _bookingRepository.GetBookingsByListingIdAsync(listingId);
        }

        public async Task<Booking> CancelBookingAsync(int bookingId, string reason)
        {
            var booking = await _bookingRepository.GetBookingByIdAsync(bookingId);
            if (booking == null)
            {
                throw new KeyNotFoundException("Booking not found");
            }

            var cancelledStatus = await _statusRepository.GetStatusByNameAsync("Cancelled");
            if (cancelledStatus == null)
            {
                throw new InvalidOperationException("Booking status configuration error");
            }

            booking.StatusId = cancelledStatus.Id;
            booking.CancellationReason = reason;
            booking.CancelledAt = DateTime.UtcNow;

            return await _bookingRepository.UpdateBookingAsync(booking);
        }

        public async Task<Booking> ConfirmBookingAsync(int bookingId)
        {
            var booking = await _bookingRepository.GetBookingByIdAsync(bookingId);
            if (booking == null)
            {
                throw new KeyNotFoundException("Booking not found");
            }

            var confirmedStatus = await _statusRepository.GetStatusByNameAsync("Confirmed");
            if (confirmedStatus == null)
            {
                throw new InvalidOperationException("Booking status configuration error");
            }

            booking.StatusId = confirmedStatus.Id;

            return await _bookingRepository.UpdateBookingAsync(booking);
        }

        public async Task<bool> IsListingAvailableAsync(int listingId, DateTime checkInDate, DateTime checkOutDate, int? excludeBookingId = null)
        {
            return await _bookingRepository.IsListingAvailableAsync(listingId, checkInDate, checkOutDate, excludeBookingId);
        }

        public async Task<decimal> CalculateBookingPriceAsync(int listingId, DateTime checkInDate, DateTime checkOutDate, int numberOfGuests)
        {
            var listingPrice = await GetListingPriceAsync(listingId);
            if (listingPrice == null)
            {
                throw new InvalidOperationException("Listing not found");
            }

            var nights = (checkOutDate - checkInDate).Days;
            return listingPrice.Value * nights;
        }

        private async Task<decimal?> GetListingPriceAsync(int listingId)
        {
            try
            {
                var listingServiceUrl = _configuration["Services:ListingService"];
                var response = await _httpClient.GetAsync($"{listingServiceUrl}/api/listings/{listingId}/price");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return decimal.Parse(content);
                }

                _logger.LogWarning("Failed to get listing price for ID: {ListingId}", listingId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting listing price for ID: {ListingId}", listingId);
                return null;
            }
        }

        private async Task<int?> GetListingCapacityAsync(int listingId)
        {
            try
            {
                var listingServiceUrl = _configuration["Services:ListingService"];
                var response = await _httpClient.GetAsync($"{listingServiceUrl}/api/listings/{listingId}/capacity");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (int.TryParse(content, out var capacity))
                    {
                        return capacity;
                    }
                }

                _logger.LogWarning("Failed to get listing capacity for ID: {ListingId}", listingId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting listing capacity for ID: {ListingId}", listingId);
                return null;
            }
        }
    }
}