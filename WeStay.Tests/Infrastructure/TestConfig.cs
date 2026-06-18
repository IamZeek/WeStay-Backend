using Microsoft.Extensions.Configuration;

namespace WeStay.Tests.Infrastructure
{
    /// <summary>
    /// Base URLs for each service, read from appsettings.json (copied to output) and overridable
    /// by environment variables (e.g. BaseUrls__Gateway=https://...), defaulting to the local ports.
    /// </summary>
    public static class TestConfig
    {
        private static readonly IConfiguration Config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        public static string Gateway => Url("Gateway", "https://localhost:7087");
        public static string Auth => Url("Auth", "https://localhost:7019");
        public static string Listing => Url("Listing", "https://localhost:7002");
        public static string Booking => Url("Booking", "https://localhost:7292");
        public static string Messaging => Url("Messaging", "https://localhost:7179");
        public static string Notification => Url("Notification", "https://localhost:7284");

        private static string Url(string key, string fallback) => Config[$"BaseUrls:{key}"] ?? fallback;
    }
}
