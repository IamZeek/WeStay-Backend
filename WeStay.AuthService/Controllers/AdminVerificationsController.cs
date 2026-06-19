using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using WeStay.AuthService.Models;
using WeStay.AuthService.Services;
using WeStay.AuthService.Services.Interfaces;

namespace WeStay.AuthService.Controllers
{
    /// <summary>
    /// Admin-only KYC moderation. Exposes the existing VerificationService logic (submit/list-by-status
    /// /approve/reject) behind Admin-gated routes. Enforced at the controller via
    /// [Authorize(Roles="Admin")] AND at the gateway via RouteClaimsRequirement on /api/auth/admin/*
    /// (the same belt-and-suspenders pattern as /api/auth/users/*).
    /// </summary>
    [ApiController]
    [Route("api/auth/admin/verifications")]
    [Authorize(Roles = "Admin")]
    public class AdminVerificationsController : ControllerBase
    {
        private readonly IVerificationService _verificationService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AdminVerificationsController> _logger;

        public AdminVerificationsController(
            IVerificationService verificationService,
            IServiceScopeFactory scopeFactory,
            ILogger<AdminVerificationsController> logger)
        {
            _verificationService = verificationService;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// Paginated list of verifications, filtered by status. Defaults to the Pending working queue.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var filter = VerificationStatus.Pending; // default working queue
            if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse(status, ignoreCase: true, out filter))
            {
                return BadRequest(new { Message = $"Invalid status '{status}'. Valid: Pending, Approved, Rejected, UnderReview." });
            }

            var all = (await _verificationService.GetVerificationsByStatusAsync(filter)).ToList();
            var items = all
                .OrderByDescending(v => v.CreatedAt) // newest submissions first
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToListItem)
                .ToList();

            return Ok(new { Status = filter.ToString(), Page = page, PageSize = pageSize, TotalCount = all.Count, Items = items });
        }

        /// <summary>Single verification detail, including the owning user and submitted document.</summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var v = await _verificationService.GetVerificationByIdAsync(id);
            if (v == null) return NotFound(new { Message = "Verification not found" });
            return Ok(ToDetail(v));
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> Approve(int id)
        {
            try
            {
                var v = await _verificationService.UpdateVerificationStatusAsync(id, VerificationStatus.Approved);
                NotifyUser(v.User?.Email, "Identity verification approved",
                    $"<p>Hi {v.User?.FirstName},</p><p>Your identity verification has been <strong>approved</strong>.</p>");
                return Ok(new { Message = "Verification approved", v.Id, Status = v.Status.ToString() });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
        }

        [HttpPost("{id}/reject")]
        public async Task<IActionResult> Reject(int id, [FromBody] RejectVerificationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.RejectionReason))
            {
                return BadRequest(new { Message = "RejectionReason is required." });
            }

            try
            {
                var v = await _verificationService.UpdateVerificationStatusAsync(id, VerificationStatus.Rejected, request.RejectionReason);
                NotifyUser(v.User?.Email, "Identity verification not approved",
                    $"<p>Hi {v.User?.FirstName},</p><p>Your identity verification was <strong>not approved</strong>.</p><p>Reason: {request.RejectionReason}</p>");
                return Ok(new { Message = "Verification rejected", v.Id, Status = v.Status.ToString(), v.RejectionReason });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
        }

        // Event: KYC approve/reject → email the user. Same fire-and-forget background-dispatch pattern
        // (own DI scope) as the other notification events, so a notification problem never affects the
        // moderation action.
        private void NotifyUser(string? email, string subject, string html)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var notifications = scope.ServiceProvider.GetRequiredService<NotificationClient>();
                    await notifications.SendEmailAsync(email, subject, html);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background KYC-notification dispatch failed for {Email}", email);
                }
            });
        }

        private static object ToListItem(Verification v) => new
        {
            v.Id,
            v.UserId,
            UserEmail = v.User?.Email,
            UserName = v.User != null ? $"{v.User.FirstName} {v.User.LastName}".Trim() : null,
            DocumentType = v.DocumentType.ToString(),
            v.DocumentNumber,
            Status = v.Status.ToString(),
            v.CreatedAt
        };

        private static object ToDetail(Verification v) => new
        {
            v.Id,
            v.UserId,
            User = v.User == null ? null : new { v.User.Id, v.User.Email, v.User.FirstName, v.User.LastName, v.User.PhoneNumber },
            DocumentType = v.DocumentType.ToString(),
            v.DocumentNumber,
            v.ImageUrl,
            Status = v.Status.ToString(),
            v.RejectionReason,
            v.CreatedAt,
            v.UpdatedAt,
            v.VerifiedAt
        };
    }

    public class RejectVerificationRequest
    {
        public string RejectionReason { get; set; }
    }
}
