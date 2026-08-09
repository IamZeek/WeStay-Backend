using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeStay.BookingService.Repositories.Interfaces;

namespace WeStay.BookingService.Controllers
{
    /// <summary>
    /// Public, read-only view of the platform fee percentages — same data as the Admin-only
    /// GET /api/admin/fees, but anonymous. The frontend uses it to show guests an accurate
    /// pre-booking fee breakdown; the authoritative snapshot amounts are still computed
    /// server-side at booking creation.
    /// </summary>
    [ApiController]
    [Route("api/fees")]
    public class PublicFeesController : ControllerBase
    {
        private readonly IPlatformFeeConfigRepository _repository;
        private readonly ILogger<PublicFeesController> _logger;

        public PublicFeesController(IPlatformFeeConfigRepository repository, ILogger<PublicFeesController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Get()
        {
            try
            {
                var config = await _repository.GetAsync();
                return Ok(new { config.GuestServiceFee, config.HostPlatformFee, config.CancellationFeePercent });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving public fee config");
                return StatusCode(500, new { Message = "An error occurred while retrieving fees" });
            }
        }
    }
}
