using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeStay.BookingService.DTOs;
using WeStay.BookingService.Models;
using WeStay.BookingService.Services.Interfaces;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using WeStay.BookingService.Services;

namespace WeStay.BookingService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BookingsController : ControllerBase
    {
        private readonly IBookingService _bookingService;
        private readonly IAvailabilityService _availabilityService;
        private readonly ILogger<BookingsController> _logger;
        public BookingsController(
            IBookingService bookingService,
            IAvailabilityService availabilityService,
            ILogger<BookingsController> logger)
        {
            _bookingService = bookingService;
            _availabilityService = availabilityService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid booking data", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

                var booking = new Booking
                {
                    ListingId = request.ListingId,
                    UserId = userId,
                    CheckInDate = request.CheckInDate,
                    CheckOutDate = request.CheckOutDate,
                    NumberOfGuests = request.NumberOfGuests,
                    SpecialRequests = request.SpecialRequests
                };

                var guests = request.Guests.Select(g => new BookingGuest
                {
                    FirstName = g.FirstName,
                    LastName = g.LastName,
                    Email = g.Email,
                    PhoneNumber = g.PhoneNumber,
                    DateOfBirth = g.DateOfBirth
                }).ToList();

                var createdBooking = await _bookingService.CreateBookingAsync(booking, guests);

                return Ok(new
                {
                    Message = "Booking created successfully",
                    Booking = new
                    {
                        createdBooking.Id,
                        createdBooking.BookingCode,
                        createdBooking.TotalPrice,
                        createdBooking.Currency,
                        Status = "Pending"
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating booking for user {UserId}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return StatusCode(500, new { Message = "An error occurred while creating booking" });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetBooking(int id)
        {
            try
            {
                var booking = await _bookingService.GetBookingByIdAsync(id);
                if (booking == null)
                {
                    return NotFound(new { Message = "Booking not found" });
                }

                // Check if user owns the booking or is admin
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                if (booking.UserId != userId && !User.IsInRole("Admin"))
                {
                    return Forbid();
                }

                return Ok(MapToBookingResponse(booking));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving booking {BookingId}", id);
                return StatusCode(500, new { Message = "An error occurred while retrieving booking" });
            }
        }

        [HttpGet("code/{bookingCode}")]
        public async Task<IActionResult> GetBookingByCode(string bookingCode)
        {
            try
            {
                var booking = await _bookingService.GetBookingByCodeAsync(bookingCode);
                if (booking == null)
                {
                    return NotFound(new { Message = "Booking not found" });
                }

                // Check if user owns the booking or is admin
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                if (booking.UserId != userId && !User.IsInRole("Admin"))
                {
                    return Forbid();
                }

                return Ok(MapToBookingResponse(booking));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving booking with code {BookingCode}", bookingCode);
                return StatusCode(500, new { Message = "An error occurred while retrieving booking" });
            }
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserBookings(int userId)
        {
            try
            {
                // Check if user is accessing their own bookings or is admin
                var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                if (userId != currentUserId && !User.IsInRole("Admin"))
                {
                    return Forbid();
                }

                var bookings = await _bookingService.GetUserBookingsAsync(userId);
                return Ok(bookings.Select(MapToBookingResponse));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving bookings for user {UserId}", userId);
                return StatusCode(500, new { Message = "An error occurred while retrieving bookings" });
            }
        }

        [HttpPost("availability")]
        [AllowAnonymous]
        public async Task<IActionResult> CheckAvailability([FromBody] AvailabilityCheckRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid availability check data", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                var isAvailable = await _availabilityService.IsListingAvailableAsync(
                    request.ListingId, request.CheckInDate, request.CheckOutDate);

                var price = await _bookingService.CalculateBookingPriceAsync(
                    request.ListingId, request.CheckInDate, request.CheckOutDate, request.NumberOfGuests);

                return Ok(new
                {
                    IsAvailable = isAvailable,
                    TotalPrice = price,
                    Nights = (request.CheckOutDate - request.CheckInDate).Days,
                    Currency = "USD"
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking availability for listing {ListingId}", request.ListingId);
                return StatusCode(500, new { Message = "An error occurred while checking availability" });
            }
        }

        /// <summary>
        /// Returns a day-by-day availability grid for a listing over a date range
        /// (each entry: date + IsAvailable), for rendering an availability calendar.
        /// </summary>
        [HttpGet("availability-calendar/{listingId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAvailabilityCalendar(int listingId, [FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
        {
            try
            {
                if (startDate == default || endDate == default)
                {
                    return BadRequest(new { Message = "startDate and endDate query parameters are required" });
                }

                if ((endDate.Date - startDate.Date).TotalDays > 366)
                {
                    return BadRequest(new { Message = "Date range too large; maximum is 366 days" });
                }

                var calendar = await _availabilityService.GetAvailabilityCalendarAsync(listingId, startDate, endDate);
                return Ok(new { ListingId = listingId, Calendar = calendar });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building availability calendar for listing {ListingId}", listingId);
                return StatusCode(500, new { Message = "An error occurred while building the availability calendar" });
            }
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelBooking(int id, [FromBody] CancelBookingRequest request)
        {
            try
            {
                var booking = await _bookingService.GetBookingByIdAsync(id);
                if (booking == null)
                {
                    return NotFound(new { Message = "Booking not found" });
                }

                // Check if user owns the booking or is admin
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                if (booking.UserId != userId && !User.IsInRole("Admin"))
                {
                    return Forbid();
                }

                var cancelledBooking = await _bookingService.CancelBookingAsync(id, request.Reason);

                return Ok(new
                {
                    Message = "Booking cancelled successfully",
                    Booking = MapToBookingResponse(cancelledBooking)
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling booking {BookingId}", id);
                return StatusCode(500, new { Message = "An error occurred while cancelling booking" });
            }
        }

        /// <summary>
        /// Confirm a pending booking. Only the host that owns the booking's listing (or an Admin)
        /// may confirm, and only while the booking is still Pending.
        /// </summary>
        [HttpPost("{id}/confirm")]
        public async Task<IActionResult> ConfirmBooking(int id)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                var isAdmin = User.IsInRole("Admin");

                var booking = await _bookingService.ConfirmBookingAsync(id, userId, isAdmin);

                return Ok(new { Message = "Booking confirmed successfully", Booking = MapToBookingResponse(booking) });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming booking {BookingId}", id);
                return StatusCode(500, new { Message = "An error occurred while confirming booking" });
            }
        }

        /// <summary>
        /// Reject a pending booking. Only the host that owns the booking's listing (or an Admin)
        /// may reject, and only while the booking is still Pending. Reason is optional.
        /// </summary>
        [HttpPost("{id}/reject")]
        public async Task<IActionResult> RejectBooking(int id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RejectBookingRequest request)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                var isAdmin = User.IsInRole("Admin");

                var booking = await _bookingService.RejectBookingAsync(id, userId, isAdmin, request?.Reason);

                return Ok(new { Message = "Booking rejected successfully", Booking = MapToBookingResponse(booking) });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting booking {BookingId}", id);
                return StatusCode(500, new { Message = "An error occurred while rejecting booking" });
            }
        }

        private BookingResponse MapToBookingResponse(Booking booking)
        {
            return new BookingResponse
            {
                Id = booking.Id,
                BookingCode = booking.BookingCode,
                ListingId = booking.ListingId,
                UserId = booking.UserId,
                CheckInDate = booking.CheckInDate,
                CheckOutDate = booking.CheckOutDate,
                NumberOfGuests = booking.NumberOfGuests,
                TotalPrice = booking.TotalPrice,
                Currency = booking.Currency,
                Status = booking.Status?.Name,
                SpecialRequests = booking.SpecialRequests,
                CreatedAt = booking.CreatedAt,
                Guests = booking.Guests?.Select(g => new GuestResponse
                {
                    FirstName = g.FirstName,
                    LastName = g.LastName,
                    Email = g.Email,
                    PhoneNumber = g.PhoneNumber
                }).ToList(),
                PaymentInfo = booking.Payments?.OrderByDescending(p => p.CreatedAt)
                    .Select(p => new PaymentInfoResponse
                    {
                        PaymentStatus = p.PaymentStatus,
                        Amount = p.Amount,
                        PaidAt = p.PaidAt
                    }).FirstOrDefault()
            };
        }
    }

    public class CancelBookingRequest
    {
        [Required]
        public string Reason { get; set; }
    }

    public class RejectBookingRequest
    {
        // Optional — a host may reject without giving a reason.
        public string Reason { get; set; }
    }
}