using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeStay.BookingService.DTOs;
using WeStay.BookingService.Services.Interfaces;

namespace WeStay.BookingService.Controllers
{
    /// <summary>
    /// Admin payments oversight + the manual host-payout execution step. Admin-gated at the controller
    /// and at the gateway (RouteClaimsRequirement on /api/admin/*). Feeds the future admin payout UI.
    /// </summary>
    [ApiController]
    [Route("api/admin/payments")]
    [Authorize(Roles = "Admin")]
    public class AdminPaymentsController : ControllerBase
    {
        private readonly IPaymentService _payments;
        private readonly ILogger<AdminPaymentsController> _logger;

        public AdminPaymentsController(IPaymentService payments, ILogger<AdminPaymentsController> logger)
        {
            _payments = payments;
            _logger = logger;
        }

        /// <summary>All payments, optionally filtered by status, paginated.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20;

                var (payments, totalCount) = await _payments.GetAllAsync(page, pageSize, status);
                var items = payments.Select(PaymentResponse.From).ToList();
                return Ok(new { Page = page, PageSize = pageSize, TotalCount = totalCount, Items = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing payments (admin)");
                return StatusCode(500, new { Message = "An error occurred while listing payments" });
            }
        }

        /// <summary>
        /// Manual payout execution: ReleasableToHost → PaidOutToHost. The actual money movement is a
        /// pluggable IHostPayoutExecutor (manual now; swappable for a real payout API later).
        /// </summary>
        [HttpPost("{id}/mark-paid-out")]
        public async Task<IActionResult> MarkPaidOut(int id)
        {
            try
            {
                var payment = await _payments.MarkPaidOutAsync(id);
                return Ok(PaymentResponse.From(payment));
            }
            catch (KeyNotFoundException ex) { return NotFound(new { Message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { Message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking payment {PaymentId} paid out", id);
                return StatusCode(500, new { Message = "An error occurred while marking the payout" });
            }
        }

        // ============================ SANDBOX TESTING ONLY ============================
        // Manually transitions a payment Pending → Paid → HeldForStay, exactly as the SafePay webhook
        // would, so the rest of the state machine (release cycle, mark-paid-out) can be verified without
        // depending on SafePay's sandbox webhook delivery (which is unreliable). Admin-only.
        // ⚠️ REMOVE THIS ENDPOINT BEFORE PRODUCTION — it fakes a payment with no money movement. ⚠️
        [HttpPost("{id}/mark-paid")]
        public async Task<IActionResult> MarkPaidForTesting(int id)
        {
            try
            {
                var payment = await _payments.MarkPaidForTestingAsync(id);
                return Ok(PaymentResponse.From(payment));
            }
            catch (KeyNotFoundException ex) { return NotFound(new { Message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { Message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking payment {PaymentId} paid (sandbox)", id);
                return StatusCode(500, new { Message = "An error occurred while marking the payment paid" });
            }
        }
    }
}
