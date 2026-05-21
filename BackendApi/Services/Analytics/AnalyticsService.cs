using BackendApi.Core.DataHandlers;
using BackendApi.Core.StateMachines;
using BackendApi.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Services.Analytics;

public class AnalyticsService : IAnalyticsService
{
    private readonly DBHandlerCore _db;

    public AnalyticsService(DBHandlerCore db)
    {
        _db = db;
    }

    public async Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;

        var riders = await _db.GetQuery<Models.Rider>(asNoTracking: true).ToListAsync(cancellationToken);
        
        var activeRiders = riders.Count(r => r.State == RiderState.BUSY);
        var idleRiders = riders.Count(r => r.State == RiderState.IDLE);

        var orders = await _db.GetQuery<Models.Order>(asNoTracking: true)
            .Where(o => o.CreatedAt >= today || o.State == OrderState.CREATED || o.State == OrderState.MATCHING || o.State == OrderState.ASSIGNED || o.State == OrderState.PICKING_UP || o.State == OrderState.DELIVERING)
            .ToListAsync(cancellationToken);

        var ongoingOrders = orders.Count(o => 
            o.State == OrderState.ASSIGNED || 
            o.State == OrderState.PICKING_UP || 
            o.State == OrderState.DELIVERING);
            
        var pendingOrders = orders.Count(o => 
            o.State == OrderState.CREATED || 
            o.State == OrderState.MATCHING);

        var completedToday = orders.Where(o => 
            o.State == OrderState.COMPLETED && 
            o.CompletedAt.HasValue && 
            o.CompletedAt.Value.Date == today).ToList();

        var totalRevenue = completedToday.Sum(o => o.DeliveryFee);

        return new DashboardStatsDto
        {
            ActiveRiders = activeRiders,
            IdleRiders = idleRiders,
            OngoingOrders = ongoingOrders,
            PendingOrders = pendingOrders,
            CompletedOrdersToday = completedToday.Count,
            TotalRevenueToday = totalRevenue
        };
    }

    public async Task<List<OrderTrendDto>> GetOrderTrendsAsync(int days = 7, CancellationToken cancellationToken = default)
    {
        var startDate = DateTime.UtcNow.Date.AddDays(-days + 1);

        var orders = await _db.GetQuery<Models.Order>(asNoTracking: true)
            .Where(o => o.CreatedAt >= startDate)
            .ToListAsync(cancellationToken);

        var trends = Enumerable.Range(0, days)
            .Select(i => startDate.AddDays(i))
            .Select(date => new OrderTrendDto
            {
                Date = date,
                TotalOrders = orders.Count(o => o.CreatedAt.Date == date),
                CompletedOrders = orders.Count(o => o.State == OrderState.COMPLETED && o.CompletedAt?.Date == date)
            })
            .ToList();

        return trends;
    }

    public async Task<List<RiderPerformanceDto>> GetTopPerformingRidersAsync(int count = 5, CancellationToken cancellationToken = default)
    {
        var riders = await _db.GetQuery<Models.Rider>(asNoTracking: true).ToListAsync(cancellationToken);
        
        var users = await _db.GetQuery<Models.User>(asNoTracking: true)
            .Where(u => u.RiderId != null)
            .ToListAsync(cancellationToken);

        var orders = await _db.GetQuery<Models.Order>(asNoTracking: true)
            .Where(o => o.State == OrderState.COMPLETED && o.AssignedRiderId != null)
            .ToListAsync(cancellationToken);

        var result = riders.Select(r =>
        {
            var user = users.FirstOrDefault(u => u.RiderId == r.Id);
            var riderOrders = orders.Where(o => o.AssignedRiderId == r.Id).ToList();
            var totalTimeSec = riderOrders.Sum(o => 
                (o.CompletedAt.HasValue && o.AssignedAt.HasValue) ? 
                (o.CompletedAt.Value - o.AssignedAt.Value).TotalSeconds : 0);

            var avgTimeMin = riderOrders.Count > 0 ? (totalTimeSec / 60.0) / riderOrders.Count : 0;
            
            return new RiderPerformanceDto
            {
                RiderId = r.Id,
                Name = user?.FullName ?? r.Name ?? "Unknown",
                CompletedDeliveries = riderOrders.Count,
                TotalEarned = riderOrders.Sum(o => o.DeliveryFee * 0.8m), // 80% to rider
                AverageDeliveryTimeMinutes = Math.Round(avgTimeMin, 1)
            };
        })
        .OrderByDescending(r => r.CompletedDeliveries)
        .Take(count)
        .ToList();

        return result;
    }
}
