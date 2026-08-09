using Microsoft.AspNetCore.Mvc;
using WeStay.ListingService.Models.Requests;
using WeStay.ListingService.Services.Interfaces;
using WeStay.ListingService.Models.Requests;
using WeStay.ListingService.Services.Interfaces;

namespace WeStay.ListingService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly ISearchService _searchService;
        private readonly IHostProfileService _hostProfileService;
        private readonly ILogger<SearchController> _logger;

        public SearchController(ISearchService searchService, IHostProfileService hostProfileService, ILogger<SearchController> logger)
        {
            _searchService = searchService;
            _hostProfileService = hostProfileService;
            _logger = logger;
        }

        /// <summary>
        /// Search listings with filters
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> SearchListings([FromQuery] SearchListingsRequest request)
        {
            try
            {
                var (listings, totalCount) = await _searchService.SearchListingsAsync(request);

                // One batched (and cached) AuthService call for the whole page — see HostProfileService.
                await _hostProfileService.EnrichAsync(listings);

                return Ok(new
                {
                    Listings = listings,
                    TotalCount = totalCount,
                    Page = request.Page,
                    PageSize = request.PageSize,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching listings with query: {Query}", request);
                return StatusCode(500, new { Message = "An error occurred while searching listings" });
            }
        }

        /// <summary>
        /// Get featured listings
        /// </summary>
        [HttpGet("featured")]
        public async Task<IActionResult> GetFeaturedListings()
        {
            try
            {
                var featuredListings = await _searchService.GetFeaturedListingsAsync();
                await _hostProfileService.EnrichAsync(featuredListings);
                return Ok(featuredListings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting featured listings");
                return StatusCode(500, new { Message = "An error occurred while retrieving featured listings" });
            }
        }

        /// <summary>
        /// Get similar listings
        /// </summary>
        [HttpGet("similar/{listingId}")]
        public async Task<IActionResult> GetSimilarListings(int listingId)
        {
            try
            {
                var similarListings = await _searchService.GetSimilarListingsAsync(listingId);
                await _hostProfileService.EnrichAsync(similarListings);
                return Ok(similarListings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting similar listings for listing {ListingId}", listingId);
                return StatusCode(500, new { Message = "An error occurred while retrieving similar listings" });
            }
        }

        /// <summary>
        /// Get popular search locations
        /// </summary>
        [HttpGet("popular-locations")]
        public async Task<IActionResult> GetPopularLocations([FromQuery] int count = 10)
        {
            try
            {
                var popularLocations = await _searchService.GetPopularSearchLocationsAsync(count);
                return Ok(popularLocations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting popular locations");
                return StatusCode(500, new { Message = "An error occurred while retrieving popular locations" });
            }
        }

        /// <summary>
        /// Get listing statistics
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetListingStats()
        {
            try
            {
                var stats = await _searchService.GetListingStatsAsync();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting listing stats");
                return StatusCode(500, new { Message = "An error occurred while retrieving listing statistics" });
            }
        }
    }
}