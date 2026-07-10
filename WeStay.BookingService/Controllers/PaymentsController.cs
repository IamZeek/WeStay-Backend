using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeStay.BookingService.DTOs;
using WeStay.BookingService.Services.Interfaces;

namespace WeStay.BookingService.Controllers
{
    [ApiController]
    [Route("api/payments")]
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentService _payments;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(IPaymentService payments, ILogger<PaymentsController> logger)
        {
            _payments = payments;
            _logger = logger;
        }

        private int UserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

        /// <summary>Guest initiates payment for their own Confirmed booking → returns SafePay checkout URL.</summary>
        [HttpPost("initiate")]
        public async Task<IActionResult> Initiate([FromBody] InitiatePaymentRequest request)
        {
            try
            {
                var result = await _payments.InitiatePaymentAsync(request.BookingId, UserId);
                return Ok(new { result.CheckoutUrl, result.PaymentId, result.Tracker });
            }
            catch (KeyNotFoundException ex) { return NotFound(new { Message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { Message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { Message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating payment for booking {BookingId}", request?.BookingId);
                return StatusCode(500, new { Message = "An error occurred while initiating payment" });
            }
        }

        /// <summary>
        /// SafePay webhook. NOT authenticated with our JWT — verified by HMAC signature instead.
        /// Idempotent (same event twice → processed once).
        /// </summary>
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> Webhook()
        {
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            var (tracker, success, signature) = ParseWebhook(rawBody, Request.Headers);
            if (string.IsNullOrEmpty(tracker) || string.IsNullOrEmpty(signature))
            {
                return BadRequest(new { Message = "Malformed webhook (missing tracker or signature)." });
            }

            var result = await _payments.HandleWebhookAsync(tracker, success, signature);
            return result switch
            {
                WebhookResult.InvalidSignature => Unauthorized(new { Message = "Invalid webhook signature." }),
                WebhookResult.UnknownTracker => Ok(new { Message = "Ignored (unknown tracker)." }), // 200 so SafePay stops retrying
                WebhookResult.AlreadyProcessed => Ok(new { Message = "Already processed." }),
                WebhookResult.MarkedFailed => Ok(new { Message = "Payment marked failed." }),
                _ => Ok(new { Message = "Processed." })
            };
        }

        /// <summary>Guest-owner / host-owner / admin view a booking's payment status.</summary>
        [HttpGet("booking/{bookingId}")]
        public async Task<IActionResult> GetForBooking(int bookingId)
        {
            try
            {
                var payment = await _payments.GetPaymentForBookingAsync(bookingId, UserId, User.IsInRole("Admin"));
                return Ok(PaymentResponse.From(payment));
            }
            catch (KeyNotFoundException ex) { return NotFound(new { Message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { Message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving payment for booking {BookingId}", bookingId);
                return StatusCode(500, new { Message = "An error occurred while retrieving the payment" });
            }
        }

        /// <summary>Refund a paid booking's payment (guest-owner or admin). Also auto-triggered on cancel.</summary>
        [HttpPost("{id}/refund")]
        public async Task<IActionResult> Refund(int id)
        {
            try
            {
                var payment = await _payments.RefundAsync(id, UserId, User.IsInRole("Admin"));
                return Ok(PaymentResponse.From(payment));
            }
            catch (KeyNotFoundException ex) { return NotFound(new { Message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { Message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { Message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refunding payment {PaymentId}", id);
                return StatusCode(500, new { Message = "An error occurred while refunding the payment" });
            }
        }

        // Defensive parse across the SafePay/webhook shapes: signature may be a header or a payload
        // field; tracker/state may be nested. Isolated so it's one place to adjust if the live shape
        // differs. (Verification of the signature itself happens in PaymentService/SafepayGateway.)
        private static (string tracker, bool success, string signature) ParseWebhook(string rawBody, IHeaderDictionary headers)
        {
            string tracker = null, signature = null, state = null, status = null;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
                var root = doc.RootElement;
                tracker = FirstString(root, "tracker", "token")
                          ?? Nested(root, "data", "tracker", "token")
                          ?? Nested(root, "data", "token");
                signature = FirstString(root, "signature") ?? Nested(root, "data", "signature");
                state = Nested(root, "data", "tracker", "state") ?? FirstString(root, "state");
                status = FirstString(root, "status");
            }
            catch { /* fall through to header signature / empty */ }

            if (string.IsNullOrEmpty(signature) && headers.TryGetValue("X-SFPY-Signature", out var h))
            {
                signature = h.ToString();
            }

            // Success determination: explicit status field, else a "paid/ended/complete" state.
            var success =
                (status != null && (status.Equals("success", StringComparison.OrdinalIgnoreCase) || status.Equals("paid", StringComparison.OrdinalIgnoreCase)))
                || (state != null && (state.Contains("PAID", StringComparison.OrdinalIgnoreCase) || state.Contains("ENDED", StringComparison.OrdinalIgnoreCase) || state.Contains("COMPLETE", StringComparison.OrdinalIgnoreCase)));

            return (tracker, success, signature);
        }

        private static string FirstString(JsonElement el, params string[] names)
        {
            foreach (var n in names)
                if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
            return null;
        }

        private static string Nested(JsonElement el, params string[] path)
        {
            var cur = el;
            foreach (var p in path)
            {
                if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(p, out cur)) return null;
            }
            return cur.ValueKind == JsonValueKind.String ? cur.GetString() : null;
        }
    }

    public class InitiatePaymentRequest
    {
        [Required]
        public int BookingId { get; set; }
    }
}
