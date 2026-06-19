using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using WeStay.NotificationService.Security;
using WeStay.NotificationService.Services.Interfaces;

namespace WeStay.NotificationService.Controllers
{
    /// <summary>
    /// Internal service-to-service trigger endpoints for sending Email and SMS.
    /// Added for Phase 1 so other services (e.g. WeStay.MessagingService) can request
    /// sends over direct HTTP. A message bus is intended for a later phase.
    ///
    /// SECURITY NOTE: these endpoints are [AllowAnonymous] because there is no shared
    /// service-auth mechanism yet. They must be restricted at the network layer (not
    /// exposed publicly through the gateway) or upgraded to a service token before
    /// production. Tracked in PROJECT_STATUS.md.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationsController : ControllerBase
    {
        private readonly IEmailService _emailService;
        private readonly ISMSService _smsService;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            IEmailService emailService,
            ISMSService smsService,
            ILogger<NotificationsController> logger)
        {
            _emailService = emailService;
            _smsService = smsService;
            _logger = logger;
        }

        /// <summary>
        /// Send an email. Called service-to-service.
        /// </summary>
        [HttpPost("email")]
        [AllowAnonymous] // No user JWT...
        [ServiceAuth]    // ...but a valid internal service key is required.
        public async Task<IActionResult> SendEmail([FromBody] SendEmailRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid email request", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                var success = await _emailService.SendEmailAsync(
                    request.ToEmail, request.Subject, request.HtmlContent, request.TextContent);

                return success
                    ? Ok(new { Message = "Email sent successfully" })
                    : StatusCode(500, new { Message = "Failed to send email" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email to {ToEmail}", request.ToEmail);
                return StatusCode(500, new { Message = "An error occurred while sending email" });
            }
        }

        /// <summary>
        /// Send an SMS. Called service-to-service.
        /// </summary>
        [HttpPost("sms")]
        [AllowAnonymous] // No user JWT...
        [ServiceAuth]    // ...but a valid internal service key is required.
        public async Task<IActionResult> SendSms([FromBody] SendSmsRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid SMS request", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                var success = await _smsService.SendSMSAsync(request.PhoneNumber, request.Message);

                return success
                    ? Ok(new { Message = "SMS sent successfully" })
                    : StatusCode(500, new { Message = "Failed to send SMS" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending SMS to {PhoneNumber}", request.PhoneNumber);
                return StatusCode(500, new { Message = "An error occurred while sending SMS" });
            }
        }
    }

    public class SendEmailRequest
    {
        [Required]
        [EmailAddress]
        public string ToEmail { get; set; }

        [Required]
        [MaxLength(200)]
        public string Subject { get; set; }

        [Required]
        public string HtmlContent { get; set; }

        public string TextContent { get; set; }
    }

    public class SendSmsRequest
    {
        [Required]
        [Phone]
        public string PhoneNumber { get; set; }

        [Required]
        [MaxLength(1600)]
        public string Message { get; set; }
    }
}
