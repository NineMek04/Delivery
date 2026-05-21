using BackendApi.Core;
using BackendApi.Core.Models;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Services.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Controllers.Business;

/// <summary>
/// API สำหรับ Dashboard Analytics
/// สงวนไว้สำหรับ Admin/Dispatcher เท่านั้น
/// </summary>
[Authorize(Policy = AuthConstants.OperationsPolicy)]
public class AnalyticsController : DeliveryControllerBase
{
    private readonly IAnalyticsService _analyticsService;

    public AnalyticsController(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<ApiResponse<DashboardStatsDto>>> GetDashboardStats(CancellationToken cancellationToken)
    {
        var stats = await _analyticsService.GetDashboardStatsAsync(cancellationToken);
        return Ok(ApiResponse<DashboardStatsDto>.Ok(stats));
    }

    [HttpGet("trends")]
    public async Task<ActionResult<ApiResponse<List<OrderTrendDto>>>> GetOrderTrends(
        [FromQuery] int days = 7, 
        CancellationToken cancellationToken = default)
    {
        var trends = await _analyticsService.GetOrderTrendsAsync(days, cancellationToken);
        return Ok(ApiResponse<List<OrderTrendDto>>.Ok(trends));
    }

    [HttpGet("top-riders")]
    public async Task<ActionResult<ApiResponse<List<RiderPerformanceDto>>>> GetTopRiders(
        [FromQuery] int count = 5,
        CancellationToken cancellationToken = default)
    {
        var riders = await _analyticsService.GetTopPerformingRidersAsync(count, cancellationToken);
        return Ok(ApiResponse<List<RiderPerformanceDto>>.Ok(riders));
    }
}
