using System;
using System.Threading.Tasks;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace BackendApi.Features.FleetTracking.Telemetry
{
    /// <summary>
    /// Level 1 Rate Limiting (Server-Side)
    /// Dynamic backpressure-aware rate limiter using Redis keys with TTL.
    /// </summary>
    public class GpsRedisRateLimiter
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<GpsRedisRateLimiter> _logger;

        public GpsRedisRateLimiter(IConnectionMultiplexer redis, ILogger<GpsRedisRateLimiter> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        /// <summary>
        /// Calculates rate-limiting interval based on current system load (pending queue count).
        /// </summary>
        public int GetRecommendedInterval(int pendingQueueCount)
        {
            if (pendingQueueCount >= 10_000)
                return 15; // Critical load: only allow every 15 seconds
            if (pendingQueueCount >= 5_000)
                return 10; // High load: only allow every 10 seconds
            if (pendingQueueCount >= 1_000)
                return 5;  // Moderate load: only allow every 5 seconds
            
            return 3;      // Normal load: allow every 3 seconds
        }

        /// <summary>
        /// Checks if the location update for the given rider should be rate limited.
        /// Returns true if it should be rate limited (blocked), or false if it is allowed.
        /// If Redis is unavailable, returns false (bypass) to maintain GPS update availability.
        /// </summary>
        public async Task<bool> ShouldRateLimitAsync(string riderId, int pendingQueueCount)
        {
            var db = _redis.GetDatabase();
            var key = $"rider_last_gps_limit:{riderId}";

            int intervalSeconds = GetRecommendedInterval(pendingQueueCount);

            bool rateLimited;
            try
            {
                // Attempt to set key atomically if it does not exist (meaning rate limit window has expired)
                rateLimited = !await db.StringSetAsync(key, "1", TimeSpan.FromSeconds(intervalSeconds), When.NotExists);
            }
            catch (Exception ex)
            {
                // [RESILIENCE FIX] Redis connection failure must not halt GPS updates.
                // Fail-open: bypass rate limiting temporarily so riders can still report location.
                _logger.LogWarning(ex, "Redis unavailable during GPS rate-limit check for Rider {RiderId}. Bypassing rate limit.", riderId);
                return false;
            }

            if (rateLimited)
            {
                _logger.LogDebug("GPS Rate Limit hit for Rider {RiderId}. Throttling to {Interval}s.", riderId, intervalSeconds);
                BackendApi.Security.Services.SecurityMetrics.RateLimitRejectionsTotal.WithLabels("gps").Inc();
            }

            // Return the actual rate limiting result
            return rateLimited;
        }
    }
}

