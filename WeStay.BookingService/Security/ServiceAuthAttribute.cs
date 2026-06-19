using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace WeStay.BookingService.Security
{
    /// <summary>
    /// Authorizes internal service-to-service calls with a shared static API key (the "service token").
    /// The caller must send header <c>X-Internal-Api-Key</c> equal to config <c>ServiceAuth:InternalApiKey</c>.
    ///
    /// Implemented as a custom IAuthorizationFilter attribute (not middleware) to match the codebase's
    /// attribute-based auth and apply per-endpoint. Runs even with [AllowAnonymous] (which only affects
    /// the built-in authorization), so these endpoints need NO user JWT — only the service key.
    ///
    /// Fail-closed: if the server has no key configured, the call is rejected (500) rather than allowed.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class ServiceAuthAttribute : Attribute, IAuthorizationFilter
    {
        public const string HeaderName = "X-Internal-Api-Key";

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var configured = context.HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()["ServiceAuth:InternalApiKey"];

            if (string.IsNullOrEmpty(configured))
            {
                context.Result = new ObjectResult(new { Message = "Internal service authentication is not configured." })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
                return;
            }

            var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
            if (string.IsNullOrEmpty(provided) || !FixedTimeEquals(provided, configured))
            {
                context.Result = new UnauthorizedObjectResult(new { Message = "Missing or invalid internal service credential." });
            }
        }

        private static bool FixedTimeEquals(string a, string b)
            => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }
}
