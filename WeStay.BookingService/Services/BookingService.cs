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

        public async Task<Booking> ConfirmBookingAsync(int bookingId, int requestingUserId, bool isAdmin)
        {
            var booking = await GetBookingForHostActionAsync(bookingId, requestingUserId, isAdmin);
            EnsureStatus(booking, "Pending", "confirmed");

            var confirmedStatus = await _statusRepository.GetStatusByNameAsync("Confirmed");
            if (confirmedStatus == null)
            {
                throw new InvalidOperationException("Booking status configuration error");
            }

            booking.StatusId = confirmedStatus.Id;
            await _bookingRepository.UpdateBookingAsync(booking);

            // Re-fetch so the returned booking carries the fresh Status navigation.
            return await _bookingRepository.GetBookingByIdAsync(bookingId);
        }

        public async Task<Booking> RejectBookingAsync(int bookingId, int requestingUserId, bool isAdmin, string reason)
        {
            var booking = await GetBookingForHostActionAsync(bookingId, requestingUserId, isAdmin);
            EnsureStatus(booking, "Pending", "rejected");

            var rejectedStatus = await _statusRepository.GetStatusByNameAsync("Rejected");
            if (rejectedStatus == null)
            {
                throw new InvalidOperationException("Booking status configuration error");
            }

            booking.StatusId = rejectedStatus.Id;
            booking.CancellationReason = reason ?? string.Empty;
            booking.CancelledAt = DateTime.UtcNow;
            await _bookingRepository.UpdateBookingAsync(booking);

            return await _bookingRepository.GetBookingByIdAsync(bookingId);
        }

        public async Task<Booking> CompleteBookingAsync(int bookingId, int requestingUserId, bool isAdmin)
        {
            var booking = await GetBookingForHostActionAsync(bookingId, requestingUserId, isAdmin);
            EnsureStatus(booking, "Confirmed", "completed");

            var completedStatus = await _statusRepository.GetStatusByNameAsync("Completed");
            if (completedStatus == null)
            {
                throw new InvalidOperationException("Booking status configuration error");
            }

            booking.StatusId = completedStatus.Id;
            await _bookingRepository.UpdateBookingAsync(booking);

            return await _bookingRepository.GetBookingByIdAsync(bookingId);
        }

        // Loads a booking and verifies the caller is the host that owns the booking's listing
        // (or an Admin). Ownership is checked against ListingService over HTTP, reusing the same
        // HttpClient pattern as price/capacity lookups.
        private async Task<Booking> GetBookingForHostActionAsync(int bookingId, int requestingUserId, bool isAdmin)
        {
            var booking = await _bookingRepository.GetBookingByIdAsync(bookingId);
            if (booking == null)
            {
                throw new KeyNotFoundException("Booking not found");
            }

            if (!isAdmin)
            {
                var hostId = await GetListingHostIdAsync(booking.ListingId);
                if (hostId == null)
                {
                    throw new InvalidOperationException("Unable to verify listing ownership at this time.");
                }
                if (hostId.Value != requestingUserId)
                {
                    throw new UnauthorizedAccessException("Only the host who owns the listing can perform this action.");
                }
            }

            return booking;
        }

        private static void EnsureStatus(Booking booking, string expectedStatus, string action)
        {
            var current = booking.Status?.Name ?? "Unknown";
            if (!string.Equals(current, expectedStatus, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Booking cannot be {action} because its status is '{current}'. Only '{expectedStatus}' bookings can be {action}.");
            }
        }

        // Auto-complete: Confirmed bookings whose checkout has passed → Completed.
        // Reuses CompleteBookingAsync (acting as the system, isAdmin: true → skips ownership).
        public async Task<int> AutoCompleteExpiredBookingsAsync(DateTime asOfUtc)
        {
            var confirmed = await _statusRepository.GetStatusByNameAsync("Confirmed");
            if (confirmed == null)
            {
                _logger.LogError("Auto-complete batch aborted: 'Confirmed' status not found.");
                return 0;
            }

            var ids = await _bookingRepository.GetBookingIdsByStatusPastCheckoutAsync(confirmed.Id, asOfUtc);
            var completed = 0;
            foreach (var id in ids)
            {
                try
                {
                    var booking = await CompleteBookingAsync(id, requestingUserId: 0, isAdmin: true);
                    completed++;
                    _logger.LogInformation(
                        "Auto-completed booking {BookingId} (listing {ListingId}, checkout {CheckOutDate:u})",
                        booking.Id, booking.ListingId, booking.CheckOutDate);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Auto-complete failed for booking {BookingId}", id);
                }
            }

            return completed;
        }

        // Auto-cancel: Pending bookings the host never confirmed within the configured window → Cancelled.
        public async Task<int> AutoCancelUnconfirmedBookingsAsync(DateTime asOfUtc)
        {
            var pending = await _statusRepository.GetStatusByNameAsync("Pending");
            if (pending == null)
            {
                _logger.LogError("Auto-cancel batch aborted: 'Pending' status not found.");
                return 0;
            }

            var windowHours = int.TryParse(_configuration["Booking:AutoCancelPendingHours"], out var h) ? h : 24;
            var cutoffUtc = asOfUtc.AddHours(-windowHours);
            var reason = $"Auto-cancelled: host did not confirm within {windowHours} hours";

            var ids = await _bookingRepository.GetBookingIdsByStatusCreatedBeforeAsync(pending.Id, cutoffUtc);
            var cancelled = 0;
            foreach (var id in ids)
            {
                try
                {
                    var booking = await CancelBookingAsync(id, reason);
                    cancelled++;
                    _logger.LogInformation(
                        "Auto-cancelled booking {BookingId} (listing {ListingId}, created {CreatedAt:u})",
                        booking.Id, booking.ListingId, booking.CreatedAt);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Auto-cancel failed for booking {BookingId}", id);
                }
            }

            return cancelled;
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

        private async Task<int?> GetListingHostIdAsync(int listingId)
        {
            try
            {
                var listingServiceUrl = _configuration["Services:ListingService"];
                var response = await _httpClient.GetAsync($"{listingServiceUrl}/api/listings/{listingId}/owner");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (int.TryParse(content, out var hostId))
                    {
                        return hostId;
                    }
                }

                _logger.LogWarning("Failed to get listing owner for ID: {ListingId}", listingId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting listing owner for ID: {ListingId}", listingId);
                return null;
            }
        }
    }
}