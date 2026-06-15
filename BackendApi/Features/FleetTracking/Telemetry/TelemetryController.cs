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
using Microsoft.AspNetCore.RateLimiting;
using BackendApi.Setup;

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
        private readonly ClientRouteTelemetryService _clientRouteTelemetryService;
        private readonly GpsRedisRateLimiter _rateLimiter;
        private readonly GpsRabbitMqPublisher _publisher;

        public TelemetryController(
            TelemetryService telemetryService,
            ClientRouteTelemetryService clientRouteTelemetryService,
            GpsRedisRateLimiter rateLimiter,
            GpsRabbitMqPublisher publisher)
        {
            _telemetryService = telemetryService;
            _clientRouteTelemetryService = clientRouteTelemetryService;
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

            var user = await DB.GetQuery<BackendApi.Models.User>(asNoTracking: true)
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
            // The controller already consumed the rider's rate-limit window above.
            await _telemetryService.ProcessLocationUpdateAsync(
                riderId,
                request.Latitude,
                request.Longitude,
                request.Accuracy,
                bypassRateLimit: true);

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

            var user = await DB.GetQuery<BackendApi.Models.User>(asNoTracking: true)
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

        /// <summary>
        /// Records when the Rider client must render a straight-line route fallback.
        /// The report is accepted only for an order assigned to the authenticated rider.
        /// </summary>
        [HttpPost("client-route-fallback")]
        [Authorize(Policy = AuthConstants.RiderPolicy)]
        [RequestSizeLimit(4096)]
        public async Task<ActionResult<ApiResponse<string>>> PostClientRouteFallback(
            [FromBody] ClientRouteFallbackRequest request,
            CancellationToken cancellationToken)
        {
            var userId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(ApiResponse<string>.Fail(
                    StatusCodes.Status401Unauthorized,
                    "User could not be identified."));
            }

            var accepted = await _clientRouteTelemetryService.ReportFallbackAsync(
                userId,
                request,
                CorrelationIdProvider.GetOrCreate(HttpContext),
                cancellationToken);

            if (!accepted)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    ApiResponse<string>.Fail(
                        StatusCodes.Status403Forbidden,
                        "Route fallback report is not allowed for this order."));
            }

            return Ok(ApiResponse<string>.Ok("Route fallback report accepted."));
        }

        /// <summary>
        /// Ingests general client telemetry events (e.g. reconnections/network drops) from Angular or Flutter.
        /// Authenticated and strictly rate-limited to 5 requests per minute.
        /// </summary>
        [HttpPost("client-events")]
        [Authorize]
        [EnableRateLimiting(SecurityConfiguration.TelemetryRateLimitPolicy)]
        public ActionResult<ApiResponse<string>> PostClientEvents([FromBody] ClientEventRequest request)
        {
            if (request == null)
            {
                return BadRequest(ApiResponse<string>.Fail("Request body cannot be null."));
            }

            OperationalMetrics.ClientEventsTotal.WithLabels(request.EventType, request.ClientType).Inc();

            Logger.LogInformation(
                "Telemetry client event received: ClientType={ClientType}, EventType={EventType}, Details={Details}",
                request.ClientType,
                request.EventType,
                request.Details);

            return Ok(ApiResponse<string>.Ok("Client event accepted."));
        }
    }
}
