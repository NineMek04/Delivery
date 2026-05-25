using BackendApi.Models.DTOs;

namespace BackendApi.Services.Analytics;

public interface IAnalyticsService
{
    Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken cancellationToken = default);
    Task<List<OrderTrendDto>> GetOrderTrendsAsync(int days = 7, CancellationToken cancellationToken = default);
    Task<List<RiderPerformanceDto>> GetTopPerformingRidersAsync(int count = 5, CancellationToken cancellationToken = default);
    Task<AnalyticsSummaryDto> GetAnalyticsSummaryAsync(CancellationToken cancellationToken = default);
    Task<RealtimeTelemetryDto> GetRealtimeTelemetryAsync(CancellationToken cancellationToken = default);
    Task<RiderUtilizationDto> GetRiderUtilizationAsync(CancellationToken cancellationToken = default);
    Task<List<HeatmapPointDto>> GetHeatmapPointsAsync(CancellationToken cancellationToken = default);
}
