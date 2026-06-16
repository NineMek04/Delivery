using BackendApi.Core;
using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;
using BackendApi.Models.DTOs;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;
using BackendApi.Services.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackendApi.Controllers.Analytics;

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

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<AnalyticsSummaryDto>>> GetSummary(CancellationToken cancellationToken)
    {
        var summary = await _analyticsService.GetAnalyticsSummaryAsync(cancellationToken);
        return Ok(ApiResponse<AnalyticsSummaryDto>.Ok(summary));
    }

    [HttpGet("realtime")]
    public async Task<ActionResult<ApiResponse<RealtimeTelemetryDto>>> GetRealtime(CancellationToken cancellationToken)
    {
        var realtime = await _analyticsService.GetRealtimeTelemetryAsync(cancellationToken);
        return Ok(ApiResponse<RealtimeTelemetryDto>.Ok(realtime));
    }

    [HttpGet("rider-utilization")]
    public async Task<ActionResult<ApiResponse<RiderUtilizationDto>>> GetRiderUtilization(CancellationToken cancellationToken)
    {
        var utilization = await _analyticsService.GetRiderUtilizationAsync(cancellationToken);
        return Ok(ApiResponse<RiderUtilizationDto>.Ok(utilization));
    }

    [HttpGet("heatmap")]
    public async Task<ActionResult<ApiResponse<List<HeatmapPointDto>>>> GetHeatmap(CancellationToken cancellationToken)
    {
        var heatmap = await _analyticsService.GetHeatmapPointsAsync(cancellationToken);
        return Ok(ApiResponse<List<HeatmapPointDto>>.Ok(heatmap));
    }
}



