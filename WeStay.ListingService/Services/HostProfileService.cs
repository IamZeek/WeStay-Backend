using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using WeStay.ListingService.Models;
using WeStay.ListingService.Services.Interfaces;

namespace WeStay.ListingService.Services
{
    /// <summary>
    /// Enriches listings with host display info (name, KYC-verified flag, avatar) from AuthService.
    ///
    /// N+1 avoidance: a search page returns up to 20 listings, so per-listing HTTP calls would mean
    /// 20 serial round-trips. Instead this batches all UNCACHED host ids into ONE call to the
    /// internal POST /api/auth/users/profile-summaries endpoint, and caches each host's profile for
    /// 5 minutes (host names/KYC status change rarely). A steady-state search therefore costs zero
    /// extra HTTP calls; a cold one costs exactly one.
    ///
    /// Fail-soft: if AuthService is unreachable, listings get HostName="Host" / HostIsVerified=false
    /// rather than failing the request, and the fallback is negative-cached for 30s so an outage
    /// can't add the HTTP timeout to every search.
    /// </summary>
    public class HostProfileService : IHostProfileService
    {
        private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(30);
        private const string FallbackName = "Host";

        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<HostProfileService> _logger;

        public HostProfileService(
            HttpClient httpClient,
            IMemoryCache cache,
            IConfiguration configuration,
            ILogger<HostProfileService> logger)
        {
            _httpClient = httpClient;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
        }

        // Shape of AuthService's UserProfileSummary response (camelCase JSON).
        private sealed class HostProfile
        {
            public int Id { get; set; }
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public bool IsVerified { get; set; }
            public string? AvatarUrl { get; set; }
        }

        public async Task EnrichAsync(IEnumerable<Listing> listings)
        {
            var list = listings?.Where(l => l != null).ToList();
            if (list == null || list.Count == 0) return;

            var hostIds = list.Select(l => l.HostId).Distinct().ToList();
            var profiles = new Dictionary<int, HostProfile?>();
            var missing = new List<int>();

            foreach (var id in hostIds)
            {
                if (_cache.TryGetValue(CacheKey(id), out HostProfile? cached))
                {
                    profiles[id] = cached; // may be a cached null (unknown user / recent failure)
                }
                else
                {
                    missing.Add(id);
                }
            }

            if (missing.Count > 0)
            {
                var (fetched, success) = await FetchBatchAsync(missing);
                foreach (var p in fetched)
                {
                    profiles[p.Id] = p;
                    _cache.Set(CacheKey(p.Id), p, ProfileTtl);
                }

                // Ids not in the response: genuinely unknown users cache the fallback for the full
                // TTL; on a failed call, only briefly, so recovery is picked up fast.
                var unresolvedTtl = success ? ProfileTtl : FailureTtl;
                foreach (var id in missing.Where(id => !profiles.ContainsKey(id)))
                {
                    profiles[id] = null;
                    _cache.Set(CacheKey(id), (HostProfile?)null, unresolvedTtl);
                }
            }

            foreach (var listing in list)
            {
                var p = profiles.GetValueOrDefault(listing.HostId);
                if (p != null)
                {
                    var name = string.Join(" ",
                        new[] { p.FirstName, p.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    listing.HostName = string.IsNullOrWhiteSpace(name) ? FallbackName : name;
                    listing.HostIsVerified = p.IsVerified;
                    listing.HostAvatarUrl = p.AvatarUrl;
                }
                else
                {
                    listing.HostName = FallbackName;
                    listing.HostIsVerified = false;
                    listing.HostAvatarUrl = null;
                }
            }
        }

        private async Task<(List<HostProfile> profiles, bool success)> FetchBatchAsync(List<int> ids)
        {
            try
            {
                var authServiceUrl = _configuration["Services:AuthService"];
                if (string.IsNullOrWhiteSpace(authServiceUrl))
                {
                    _logger.LogWarning("Services:AuthService is not configured; host enrichment disabled");
                    return (new List<HostProfile>(), false);
                }

                var response = await _httpClient.PostAsJsonAsync(
                    $"{authServiceUrl}/api/auth/users/profile-summaries", new { ids });

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Profile-summary batch call failed with HTTP {Status}", (int)response.StatusCode);
                    return (new List<HostProfile>(), false);
                }

                var profiles = await response.Content.ReadFromJsonAsync<List<HostProfile>>();
                return (profiles ?? new List<HostProfile>(), true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching host profile summaries from AuthService");
                return (new List<HostProfile>(), false);
            }
        }

        private static string CacheKey(int hostId) => $"host-profile:{hostId}";
    }
}
