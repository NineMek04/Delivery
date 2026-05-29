using System;
using System.Threading.Tasks;
using BackendApi.Core;
using BackendApi.Core.Models;
using BackendApi.Services.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Features.FleetTracking.Telemetry
{
    /// <summary>
    /// REST Telemetry Controller (Vertical Slice Feature)
    /// Exposes primary REST endpoints for client GPS telemetry with built-in Rate Limiting
    /// and HTTP dynamic fallback header controls.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/telemetry")]
    public class TelemetryController : DeliveryControllerBase
    {
        private readonly TelemetryService _telemetryService;
        private readonly GpsRedisRateLimiter _rateLimiter;
        private readonly GpsRabbitMqPublisher _publisher;

        public TelemetryController(
            TelemetryService telemetryService,
            GpsRedisRateLimiter rateLimiter,
            GpsRabbitMqPublisher publisher)
        {
            _telemetryService = telemetryService;
            _rateLimiter = rateLimiter;
            _publisher = publisher;
        }

        /// <summary>
        /// Level 2 REST Endpoint for sending GPS coordinate updates.
        /// Performs server-side rate-limiting and appends X-Recommended-Ping header
        /// to command the mobile client's tracking frequency.
        /// </summary>
        [HttpPost("gps")]
        public async Task<ActionResult<ApiResponse<string>>> PostGpsCoordinate([FromBody] GpsPointRequest request)
        {
            if (request == null)
            {
                return BadRequest(ApiResponse<string>.Fail("Request body cannot be null."));
            }

            var riderId = CurrentUserId;
            if (string.IsNullOrEmpty(riderId))
            {
                return Unauthorized(ApiResponse<string>.Fail("Rider ID could not be identified."));
            }

            int currentQueueSize = _publisher.PendingQueueCount;
            bool rateLimited = await _rateLimiter.ShouldRateLimitAsync(riderId, currentQueueSize);
            int recommendedInterval = _rateLimiter.GetRecommendedInterval(currentQueueSize);

            // Level 2 Fallback: Command mobile interval via custom HTTP response header
            Response.Headers["X-Recommended-Ping"] = recommendedInterval.ToString();

            if (rateLimited)
            {
                // Return 202 Accepted: Standard API pattern indicating request is received
                // but discarded/ignored because of current rate limits to conserve system bandwidth.
                return StatusCode(StatusCodes.Status202Accepted, 
                    ApiResponse<string>.Ok("Coordinate received but throttled by rate limit."));
            }

            // Normal ingestion path
            await _telemetryService.ProcessLocationUpdateAsync(riderId, request.Latitude, request.Longitude, request.Accuracy);

            return Ok(ApiResponse<string>.Ok("Coordinate accepted and processed."));
        }

        /// <summary>
        /// Level 3 Endpoint (Config On-Startup).
        /// Mobile clients call this upon launching to sync initial default interval seconds.
        /// </summary>
        [HttpGet("config/mobile")]
        [AllowAnonymous]
        public ActionResult<ApiResponse<MobileConfigResponse>> GetMobileConfig()
        {
            int currentQueueSize = _publisher.PendingQueueCount;
            int recommendedInterval = _rateLimiter.GetRecommendedInterval(currentQueueSize);

            var config = new MobileConfigResponse
            {
                IntervalSeconds = recommendedInterval,
                ServerTime = DateTime.UtcNow
            };

            return Ok(ApiResponse<MobileConfigResponse>.Ok(config));
        }
    }

    public class GpsPointRequest
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Accuracy { get; set; }
    }

    public class MobileConfigResponse
    {
        public int IntervalSeconds { get; set; }
        public DateTime ServerTime { get; set; }
    }
}
