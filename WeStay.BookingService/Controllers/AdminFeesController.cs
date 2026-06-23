using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeStay.BookingService.Repositories.Interfaces;

namespace WeStay.BookingService.Controllers
{
    /// <summary>
    /// Admin-only platform-fee configuration (single global config). Read at booking creation to
    /// snapshot fee amounts; changes apply to NEW bookings only. Admin-gated at the controller and
    /// at the gateway (RouteClaimsRequirement on /api/admin/*).
    /// </summary>
    [ApiController]
    [Route("api/admin/fees")]
    [Authorize(Roles = "Admin")]
    public class AdminFeesController : ControllerBase
    {
        private readonly IPlatformFeeConfigRepository _repository;
        private readonly ILogger<AdminFeesController> _logger;

        public AdminFeesController(IPlatformFeeConfigRepository repository, ILogger<AdminFeesController> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var config = await _repository.GetAsync();
            return Ok(new { config.GuestServiceFee, config.HostPlatformFee, config.UpdatedAt });
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] UpdateFeesRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { Message = "Each fee must be a percentage between 0 and 100.", Errors = ModelState.Values.SelectMany(v => v.Errors) });
            }

            var config = await _repository.UpdateAsync(request.GuestServiceFee, request.HostPlatformFee);
            _logger.LogInformation("Platform fees updated: guest {Guest}% / host {Host}%", config.GuestServiceFee, config.HostPlatformFee);

            return Ok(new { Message = "Fees updated", config.GuestServiceFee, config.HostPlatformFee, config.UpdatedAt });
        }
    }

    public class UpdateFeesRequest
    {
        [Range(0, 100, ErrorMessage = "GuestServiceFee must be between 0 and 100.")]
        public decimal GuestServiceFee { get; set; }

        [Range(0, 100, ErrorMessage = "HostPlatformFee must be between 0 and 100.")]
        public decimal HostPlatformFee { get; set; }
    }
}
