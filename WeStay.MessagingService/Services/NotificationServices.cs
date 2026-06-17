using System.Net.Http.Json;
using WeStay.MessagingService.Models;

namespace WeStay.MessagingService.Services
{
    /// <summary>
    /// Adapter that delegates notification sending to WeStay.NotificationService (the
    /// authoritative notification implementation) over direct HTTP. The previous in-process
    /// stub sending logic (fake Email/SMS/Push sends) was removed in the Phase 1 de-duplication.
    ///
    /// Push and broadcast are intentionally NOT delegated: NotificationService does not expose
    /// trigger endpoints for them yet (only Email and SMS were added). A message bus is intended
    /// for a later phase — see PROJECT_STATUS.md.
    /// </summary>
    public class NotificationServices : INotificationServices
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NotificationServices> _logger;

        public NotificationServices(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<NotificationServices> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(EmailMessage emailMessage)
        {
            var baseUrl = _configuration["Services:NotificationService"];
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogError("Services:NotificationService is not configured; cannot send email to {To}", emailMessage.To);
                return false;
            }

            try
            {
                var payload = new
                {
                    ToEmail = emailMessage.To,
                    emailMessage.Subject,
                    HtmlContent = emailMessage.Body,
                    TextContent = (string?)null
                };

                var response = await _httpClient.PostAsJsonAsync($"{baseUrl}/api/notifications/email", payload);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Email delegated to NotificationService for {To}", emailMessage.To);
                    return true;
                }

                _logger.LogWarning("NotificationService returned {Status} sending email to {To}",
                    (int)response.StatusCode, emailMessage.To);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delegate email to NotificationService for {To}", emailMessage.To);
                return false;
            }
        }

        public async Task<bool> SendSmsAsync(SmsMessage smsMessage)
        {
            var baseUrl = _configuration["Services:NotificationService"];
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogError("Services:NotificationService is not configured; cannot send SMS to {To}", smsMessage.To);
                return false;
            }

            try
            {
                var payload = new
                {
                    PhoneNumber = smsMessage.To,
                    smsMessage.Message
                };

                var response = await _httpClient.PostAsJsonAsync($"{baseUrl}/api/notifications/sms", payload);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("SMS delegated to NotificationService for {To}", smsMessage.To);
                    return true;
                }

                _logger.LogWarning("NotificationService returned {Status} sending SMS to {To}",
                    (int)response.StatusCode, smsMessage.To);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delegate SMS to NotificationService for {To}", smsMessage.To);
                return false;
            }
        }

        public Task<bool> SendPushNotificationAsync(PushNotification pushNotification)
        {
            // Not delegated: NotificationService does not expose a push trigger endpoint yet.
            // Returning false (rather than a fake success) until push is wired up.
            _logger.LogWarning(
                "Push notification requested for device {DeviceToken} but NotificationService has no push endpoint yet; skipping.",
                pushNotification.DeviceToken);
            return Task.FromResult(false);
        }

        public Task<bool> SendBroadcastNotificationAsync(BroadcastMessage broadcastMessage)
        {
            // Broadcast remains a MessagingService concern (SignalR) and is not delegated to
            // NotificationService. Real broadcast delivery is not implemented yet.
            _logger.LogWarning(
                "Broadcast requested for channel {Channel} but broadcast delivery is not implemented yet; skipping.",
                broadcastMessage.Channel);
            return Task.FromResult(false);
        }
    }
}
