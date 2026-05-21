using BackendApi.Models.DTOs;

namespace BackendApi.Services.Analytics;

public interface IAnalyticsService
{
    Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken cancellationToken = default);
    Task<List<OrderTrendDto>> GetOrderTrendsAsync(int days = 7, CancellationToken cancellationToken = default);
    Task<List<RiderPerformanceDto>> GetTopPerformingRidersAsync(int count = 5, CancellationToken cancellationToken = default);
}
