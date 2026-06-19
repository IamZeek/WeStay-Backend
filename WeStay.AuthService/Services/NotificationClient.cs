using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace WeStay.AuthService.Services
{
    /// <summary>
    /// Thin client that delegates Email/SMS sends to WeStay.NotificationService over direct HTTP.
    /// Mirrors the pattern in WeStay.MessagingService/Services/NotificationServices.cs (the
    /// authoritative example). Every send is best-effort: failures are logged and swallowed so a
    /// notification problem can never fail the business operation that triggered it.
    ///
    /// NOTE: AuthService still sends OTP email/SMS via its own SendGrid/Twilio integrations
    /// (EmailService / PhoneVerificationService) — those predate this client and are left as-is.
    /// Direct HTTP is a Phase 1 choice; a message bus would replace these calls later.
    /// </summary>
    public class NotificationClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NotificationClient> _logger;

        public NotificationClient(HttpClient httpClient, IConfiguration configuration, ILogger<NotificationClient> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(string? toEmail, string subject, string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                _logger.LogWarning("Skipping email '{Subject}': no recipient address.", subject);
                return false;
            }

            var baseUrl = _configuration["Services:NotificationService"];
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogError("Services:NotificationService is not configured; cannot send email to {To}", toEmail);
                return false;
            }

            try
            {
                // NotificationService requires a non-null TextContent (its DTO field is non-nullable),
                // so always derive a plaintext fallback from the HTML.
                var textContent = Regex.Replace(htmlContent, "<.*?>", string.Empty);
                var payload = new { ToEmail = toEmail, Subject = subject, HtmlContent = htmlContent, TextContent = textContent };
                var response = await _httpClient.PostAsJsonAsync($"{baseUrl}/api/notifications/email", payload);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Email '{Subject}' delegated to NotificationService for {To}", subject, toEmail);
                    return true;
                }

                _logger.LogWarning("NotificationService returned {Status} sending email to {To}", (int)response.StatusCode, toEmail);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delegate email to NotificationService for {To}", toEmail);
                return false;
            }
        }

        public async Task<bool> SendSmsAsync(string? phoneNumber, string message)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                _logger.LogWarning("Skipping SMS: no recipient number.");
                return false;
            }

            var baseUrl = _configuration["Services:NotificationService"];
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogError("Services:NotificationService is not configured; cannot send SMS to {To}", phoneNumber);
                return false;
            }

            try
            {
                var payload = new { PhoneNumber = phoneNumber, Message = message };
                var response = await _httpClient.PostAsJsonAsync($"{baseUrl}/api/notifications/sms", payload);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("SMS delegated to NotificationService for {To}", phoneNumber);
                    return true;
                }

                _logger.LogWarning("NotificationService returned {Status} sending SMS to {To}", (int)response.StatusCode, phoneNumber);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delegate SMS to NotificationService for {To}", phoneNumber);
                return false;
            }
        }
    }
}
