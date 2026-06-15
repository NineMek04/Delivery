using BackendApi.Core;
using BackendApi.Core.Models;
using BackendApi.Security;
using BackendApi.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Features.AiRouting;

[Authorize(Policy = AuthConstants.RiderPolicy)]
[Route("api/v1/rider-routes")]
public sealed class RiderRoutesController : DeliveryControllerBase
{
    private readonly RiderRouteService _riderRouteService;

    public RiderRoutesController(RiderRouteService riderRouteService)
    {
        _riderRouteService = riderRouteService;
    }

    [HttpPost("resolve")]
    [RequestSizeLimit(4096)]
    public async Task<ActionResult<ApiResponse<RiderRouteResponse>>> Resolve(
        [FromBody] RiderRouteRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(ApiResponse<RiderRouteResponse>.Fail(
                StatusCodes.Status401Unauthorized,
                "User could not be identified."));
        }

        var route = await _riderRouteService.ResolveAsync(
            userId,
            request,
            CorrelationIdProvider.GetOrCreate(HttpContext),
            cancellationToken);

        if (route is null)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<RiderRouteResponse>.Fail(
                    StatusCodes.Status403Forbidden,
                    "Route is not available for this rider and order."));
        }

        return Ok(ApiResponse<RiderRouteResponse>.Ok(route));
    }
}
