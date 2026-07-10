using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly NotificationClient _notifications;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IPlatformFeeConfigRepository _feeConfigRepository;
        private readonly IPaymentService _paymentService;

        public BookingService(
            IBookingRepository bookingRepository,
            IBookingStatusRepository statusRepository,
            IBookingPaymentRepository paymentRepository,
            IAvailabilityService availabilityService,
            ILogger<BookingService> logger,
            HttpClient httpClient,
            IConfiguration configuration,
            NotificationClient notifications,
            IServiceScopeFactory scopeFactory,
            IPlatformFeeConfigRepository feeConfigRepository,
            IPaymentService paymentService)
        {
            _bookingRepository = bookingRepository;
            _statusRepository = statusRepository;
            _paymentRepository = paymentRepository;
            _availabilityService = availabilityService;
            _logger = logger;
            _httpClient = httpClient;
            _configuration = configuration;
            _notifications = notifications;
            _scopeFactory = scopeFactory;
            _feeConfigRepository = feeConfigRepository;
            _paymentService = paymentService;
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

            // Base price (nights × nightly rate) — unchanged from the original logic.
            var nights = (booking.CheckOutDate - booking.CheckInDate).Days;
            var basePrice = listingPrice.Value * nights;

            // Platform fees apply to SHORT-TERM listings only (other verticals have separate,
            // not-yet-built fee models). Snapshot the AMOUNTS onto the booking so a later admin fee
            // change never alters this booking. Percentages come from the global config at this moment.
            var category = await GetListingCategoryAsync(booking.ListingId); // 0 = ShortTerm
            var isShortTerm = category == 0;
            var feeConfig = await _feeConfigRepository.GetAsync();
            var guestFeePct = isShortTerm ? feeConfig.GuestServiceFee : 0m;
            var hostFeePct = isShortTerm ? feeConfig.HostPlatformFee : 0m;

            var guestServiceFeeAmount = Math.Round(basePrice * guestFeePct / 100m, 2, MidpointRounding.AwayFromZero);
            var hostPlatformFeeAmount = Math.Round(basePrice * hostFeePct / 100m, 2, MidpointRounding.AwayFromZero);

            booking.BasePrice = basePrice;
            booking.GuestServiceFeeAmount = guestServiceFeeAmount;
            booking.GuestTotalPrice = basePrice + guestServiceFeeAmount;     // guest pays this
            booking.HostPlatformFeeAmount = hostPlatformFeeAmount;
            booking.HostPayoutAmount = basePrice - hostPlatformFeeAmount;    // host receives this
            booking.TotalPrice = booking.GuestTotalPrice;                    // legacy column = guest total

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

            // Event: booking created (Pending) → notify the host. Dispatched to the background so a
            // slow/unavailable NotificationService never adds latency to (or fails) the create.
            RunNotificationInBackground(svc => svc.NotifyHostBookingCreatedAsync(createdBooking, guests));

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

        // Admin oversight: all bookings across all users/listings, optionally filtered by status name.
        public async Task<(IEnumerable<Booking> bookings, int totalCount)> GetAllBookingsAsync(int page, int pageSize, string? status)
        {
            int? statusId = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                var s = await _statusRepository.GetStatusByNameAsync(status);
                if (s == null)
                {
                    // Unknown status filter → empty page rather than an error.
                    return (Enumerable.Empty<Booking>(), 0);
                }
                statusId = s.Id;
            }

            return await _bookingRepository.GetAllBookingsAsync(page, pageSize, statusId);
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
            var confirmed = await _bookingRepository.GetBookingByIdAsync(bookingId);

            // Event: booking confirmed → notify the guest (background dispatch).
            RunNotificationInBackground(svc => svc.NotifyGuestBookingConfirmedAsync(confirmed));

            return confirmed;
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

            var rejected = await _bookingRepository.GetBookingByIdAsync(bookingId);

            // Event: booking rejected → notify the guest (background dispatch).
            RunNotificationInBackground(svc => svc.NotifyGuestBookingRejectedAsync(rejected));

            return rejected;
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

            // Checkout passed → the held payment becomes releasable to the host (best-effort).
            await _paymentService.MarkReleasableForBookingAsync(bookingId);

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

                    // Event: booking auto-cancelled (expiry) → notify the guest (background dispatch).
                    RunNotificationInBackground(svc => svc.NotifyGuestBookingExpiredAsync(booking));
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

        // Listing vertical (0=ShortTerm, 1=LongTerm, 2=Sale) — drives whether platform fees apply.
        // Best-effort: if it can't be resolved, the caller treats the booking as non-ShortTerm (0 fees),
        // so a hiccup never over-charges.
        private async Task<int?> GetListingCategoryAsync(int listingId)
        {
            try
            {
                var listingServiceUrl = _configuration["Services:ListingService"];
                var response = await _httpClient.GetAsync($"{listingServiceUrl}/api/listings/{listingId}/category");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (int.TryParse(content, out var category))
                    {
                        return category;
                    }
                }

                _logger.LogWarning("Failed to get listing category for ID: {ListingId}", listingId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting listing category for ID: {ListingId}", listingId);
                return null;
            }
        }

        // ===== Notifications (Phase 1: direct HTTP to NotificationService via NotificationClient) =====
        // Every method here is best-effort and swallows all failures — a notification problem must
        // never fail the booking operation that triggered it. Recipient contact (email/phone) is
        // resolved from AuthService; notification *content* stays within booking-local data (codes,
        // ids, dates) rather than enriching across services, per the Phase 1 constraint.
        //
        // Dispatch is fire-and-forget on a background task with its OWN DI scope, so notification I/O
        // (which can be slow or unavailable) is fully decoupled from the request that triggered it —
        // it adds zero latency and cannot fail the operation. An event bus would replace this later.

        // Runs notification work on a background task in a fresh scope (the request scope, with its
        // scoped HttpClient/DbContext, may be disposed by the time this runs). Resolves a fresh
        // BookingService from the new scope so the work uses non-disposed dependencies.
        private void RunNotificationInBackground(Func<BookingService, Task> work)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var svc = (BookingService)scope.ServiceProvider.GetRequiredService<IBookingService>();
                    await work(svc);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background notification dispatch failed");
                }
            });
        }

        private record UserContact(int Id, string? Email, string? PhoneNumber, string? FirstName, string? LastName);

        private async Task<UserContact?> GetUserContactAsync(int userId)
        {
            try
            {
                var authUrl = _configuration["Services:AuthService"];
                if (string.IsNullOrEmpty(authUrl))
                {
                    _logger.LogWarning("Services:AuthService is not configured; cannot resolve contact for user {UserId}", userId);
                    return null;
                }

                var response = await _httpClient.GetAsync($"{authUrl}/api/auth/users/{userId}/contact");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<UserContact>();
                }

                _logger.LogWarning("AuthService returned {Status} resolving contact for user {UserId}", (int)response.StatusCode, userId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving contact for user {UserId}", userId);
                return null;
            }
        }

        private static string DateRange(Booking booking) =>
            $"{booking.CheckInDate:yyyy-MM-dd} to {booking.CheckOutDate:yyyy-MM-dd}";

        private async Task NotifyHostBookingCreatedAsync(Booking booking, List<BookingGuest> guests)
        {
            try
            {
                var hostId = await GetListingHostIdAsync(booking.ListingId);
                if (hostId == null) { _logger.LogWarning("Booking {Id}: host unresolved; skipping created-notification.", booking.Id); return; }

                var host = await GetUserContactAsync(hostId.Value);
                if (host == null) return;

                var lead = guests?.FirstOrDefault();
                var guestName = lead != null ? $"{lead.FirstName} {lead.LastName}".Trim() : "a guest";
                var msg = $"You have a new booking request ({booking.BookingCode}) for listing #{booking.ListingId} from {guestName} for {DateRange(booking)}.";

                await _notifications.SendSmsAsync(host.PhoneNumber, msg);
                await _notifications.SendEmailAsync(host.Email, "New booking request", $"<p>{msg}</p>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed host created-notification for booking {Id}", booking.Id);
            }
        }

        private async Task NotifyGuestBookingConfirmedAsync(Booking booking)
        {
            try
            {
                var guest = await GetUserContactAsync(booking.UserId);
                if (guest == null) return;

                var msg = $"Your booking is confirmed for {DateRange(booking)}. Booking code: {booking.BookingCode}.";
                await _notifications.SendSmsAsync(guest.PhoneNumber, msg);
                await _notifications.SendEmailAsync(guest.Email, "Your booking is confirmed", $"<p>{msg}</p>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed guest confirmed-notification for booking {Id}", booking.Id);
            }
        }

        private async Task NotifyGuestBookingRejectedAsync(Booking booking)
        {
            try
            {
                var guest = await GetUserContactAsync(booking.UserId);
                if (guest == null) return;

                var msg = $"Your booking request {booking.BookingCode} for listing #{booking.ListingId} was not approved.";
                await _notifications.SendEmailAsync(guest.Email, "Booking request not approved", $"<p>{msg}</p>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed guest rejected-notification for booking {Id}", booking.Id);
            }
        }

        private async Task NotifyGuestBookingExpiredAsync(Booking booking)
        {
            try
            {
                var guest = await GetUserContactAsync(booking.UserId);
                if (guest == null) return;

                var msg = $"Your booking request {booking.BookingCode} expired because the host did not confirm in time.";
                await _notifications.SendEmailAsync(guest.Email, "Booking request expired", $"<p>{msg}</p>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed guest expired-notification for booking {Id}", booking.Id);
            }
        }

        // Event: booking cancelled → notify whoever did NOT cancel (an Admin cancel notifies both).
        // Public entry point (called by the controller); dispatches to the background and returns
        // immediately so the cancel response is never delayed by notification I/O.
        public Task NotifyBookingCancelledAsync(Booking booking, int cancellingUserId, bool cancellerIsAdmin)
        {
            RunNotificationInBackground(svc => svc.DoNotifyBookingCancelledAsync(booking, cancellingUserId, cancellerIsAdmin));
            return Task.CompletedTask;
        }

        private async Task DoNotifyBookingCancelledAsync(Booking booking, int cancellingUserId, bool cancellerIsAdmin)
        {
            try
            {
                var hostId = await GetListingHostIdAsync(booking.ListingId);
                var msg = $"Booking {booking.BookingCode} for listing #{booking.ListingId} ({DateRange(booking)}) has been cancelled.";

                if (cancellerIsAdmin || booking.UserId != cancellingUserId)
                {
                    var guest = await GetUserContactAsync(booking.UserId);
                    if (guest != null) await _notifications.SendEmailAsync(guest.Email, "Booking cancelled", $"<p>{msg}</p>");
                }

                if (hostId != null && (cancellerIsAdmin || hostId.Value != cancellingUserId))
                {
                    var host = await GetUserContactAsync(hostId.Value);
                    if (host != null) await _notifications.SendEmailAsync(host.Email, "Booking cancelled", $"<p>{msg}</p>");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed cancellation-notifications for booking {Id}", booking.Id);
            }
        }
    }
}