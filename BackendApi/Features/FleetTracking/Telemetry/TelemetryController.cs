using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendApi.Core;
using BackendApi.Core.Models;
using BackendApi.Services.Telemetry;
using BackendApi.Security;
using BackendApi.Features.FleetTracking.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Features.FleetTracking.Telemetry
{
    /// <summary>
    /// REST Telemetry Controller (Vertical Slice Feature)
    /// Exposes primary REST endpoints for client GPS telemetry with built-in Rate Limiting
    /// and HTTP dynamic fallback header controls.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v1/telemetry")]
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
        [Authorize(Policy = AuthConstants.RiderPolicy)]
        public async Task<ActionResult<ApiResponse<string>>> PostGpsCoordinate([FromBody] GpsPointRequest request)
        {
            if (request == null)
            {
                return BadRequest(ApiResponse<string>.Fail("Request body cannot be null."));
            }

            var userId = CurrentUserId;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(ApiResponse<string>.Fail("User could not be identified."));
            }

            var user = await DbContext.Users.AsNoTracking()
                .Select(u => new { u.Id, u.RiderId })
                .FirstOrDefaultAsync(u => u.Id == userId);

            var riderId = user?.RiderId;
            if (string.IsNullOrEmpty(riderId))
            {
                return Unauthorized(ApiResponse<string>.Fail("Rider ID could not be identified for this user."));
            }

            int currentQueueSize = _publisher.PendingQueueCount;
            bool rateLimited = await _rateLimiter.ShouldRateLimitAsync(riderId, currentQueueSize);
            int recommendedInterval = _rateLimiter.GetRecommendedInterval(currentQueueSize);

            // Level 2 Fallback: Command mobile interval via custom HTTP response header
            Response.Headers["X-Recommended-Ping"] = recommendedInterval.ToString();

            if (rateLimited)
            {
                // Return 429 Too Many Requests: Standard API pattern to instruct mobile client to hold off
                // and keep points locally in buffer to retry later.
                return StatusCode(StatusCodes.Status429TooManyRequests, 
                     ApiResponse<string>.Fail("Coordinate throttled by rate limit. Please retry later."));
            }

            // Normal ingestion path
            await _telemetryService.ProcessLocationUpdateAsync(riderId, request.Latitude, request.Longitude, request.Accuracy);

            return Ok(ApiResponse<string>.Ok("Coordinate accepted and processed."));
        }

        /// <summary>
        /// Level 2 REST Endpoint for sending batch GPS coordinate updates (Offline Buffering / Batch Ingestion).
        /// Performs server-side rate-limiting at the Batch-Level (1 Request = 1 Hit).
        /// Appends X-Recommended-Ping header to command the mobile client's tracking frequency.
        /// </summary>
        [HttpPost("gps/batch")]
        [Authorize(Policy = AuthConstants.RiderPolicy)]
        [RequestSizeLimit(32768)] // 32KB Payload size limit for Defense-in-depth against OOM attacks
        public async Task<ActionResult<ApiResponse<string>>> PostGpsBatch([FromBody] List<GpsBatchPointRequest> requests)
        {
            if (requests == null || requests.Count == 0)
            {
                return BadRequest(ApiResponse<string>.Fail("Request batch cannot be null or empty."));
            }

            if (requests.Count > 100)
            {
                return BadRequest(ApiResponse<string>.Fail("Batch size exceeds the maximum limit of 100 points."));
            }

            var userId = CurrentUserId;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(ApiResponse<string>.Fail("User could not be identified."));
            }

            var user = await DbContext.Users.AsNoTracking()
                .Select(u => new { u.Id, u.RiderId })
                .FirstOrDefaultAsync(u => u.Id == userId);

            var riderId = user?.RiderId;
            if (string.IsNullOrEmpty(riderId))
            {
                return Unauthorized(ApiResponse<string>.Fail("Rider ID could not be identified for this user."));
            }

            int currentQueueSize = _publisher.PendingQueueCount;
            bool rateLimited = await _rateLimiter.ShouldRateLimitAsync(riderId, currentQueueSize);
            int recommendedInterval = _rateLimiter.GetRecommendedInterval(currentQueueSize);

            // Level 2 Fallback: Command mobile interval via custom HTTP response header
            Response.Headers["X-Recommended-Ping"] = recommendedInterval.ToString();

            if (rateLimited)
            {
                // Return 429 Too Many Requests: Keep points locally and retry later
                return StatusCode(StatusCodes.Status429TooManyRequests, 
                     ApiResponse<string>.Fail("Batch received but throttled by rate limit. Please retry later."));
            }

            // Normal batch ingestion path
            await _telemetryService.ProcessLocationBatchAsync(riderId, requests);

            return Ok(ApiResponse<string>.Ok("Batch accepted and processed."));
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
}
