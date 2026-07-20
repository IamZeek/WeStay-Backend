using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WeStay.BookingService.Controllers
{
    /// <summary>
    /// Browser landing pages that SafePay redirects the guest to after hosted checkout. These carry
    /// NO WeStay JWT (they're a plain browser redirect), so both are [AllowAnonymous]. Placeholder
    /// JSON for sandbox testing; a real frontend would render a page here.
    ///
    /// IMPORTANT: the authoritative payment state change happens via the SIGNED WEBHOOK, not here —
    /// these endpoints only give the guest a friendly landing response.
    /// </summary>
    [ApiController]
    [Route("payment")]
    public class PaymentRedirectController : ControllerBase
    {
        [HttpGet("success")]
        [AllowAnonymous]
        public IActionResult Success() =>
            Ok(new { message = "Payment received. Your booking is being confirmed.", status = "success" });

        [HttpGet("cancel")]
        [AllowAnonymous]
        public IActionResult Cancel() =>
            Ok(new { message = "Payment cancelled. Your booking remains pending.", status = "cancelled" });
    }
}
