using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Hubs;
using BackendApi.Models;
using BackendApi.Services.Telemetry;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using StackExchange.Redis;

namespace BackendApi.Services.BackgroundWorkers;

/// <summary>
/// Background worker that acts as a coordinator for Backend Controlled Aggregation.
/// Every 2 seconds, it broadcasts the aggregated realtime telemetry to the Admin dashboard.
/// Every 5 seconds, it queries snapshot statistics from the PostgreSQL database thread-safely
/// and populates the TelemetryAggregator.
/// </summary>
public class TelemetryBroadcastWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TelemetryAggregator _aggregator;
    private readonly IHubContext<TrackingHub> _hubContext;
    private readonly ILogger<TelemetryBroadcastWorker> _logger;

    // Cache to prevent redundant broadcasts when data has not changed
    private int _lastActiveRidersCount = -1;
    private double _lastGpsUpdatesPerSecond = -1.0;
    private int _lastDispatchQueueSize = -1;
    private int _lastRidersBusyCount = -1;
    private int _lastRidersIdleCount = -1;
    private int _lastRidersOfflineCount = -1;
    private double _lastAverageDeliveriesPerRider = -1.0;

    public TelemetryBroadcastWorker(
        IServiceProvider serviceProvider,
        TelemetryAggregator aggregator,
        IHubContext<TrackingHub> hubContext,
        ILogger<TelemetryBroadcastWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _aggregator = aggregator;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TelemetryBroadcastWorker started");

        // Warm up: Perform an initial database query to populate the aggregator immediately at startup
        try
        {
            await RefreshDatabaseSnapshotsAsync(stoppingToken, refreshHotspots: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial Telemetry Database warm-up failed.");
        }

        ulong tickCount = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    tickCount++;

                    // Every 5 seconds, query PostgreSQL to update active riders count, queue size, and rider utilization.
                    // We run demand hotspots analysis every 60 seconds (every 60 ticks) to avoid massive CPU overhead.
                    if (tickCount % 5 == 0)
                    {
                        await RefreshDatabaseSnapshotsAsync(stoppingToken, refreshHotspots: tickCount % 60 == 0);
                    }

                    // Every 2 seconds, broadcast the aggregated metrics via SignalR to Admin dashboards.
                    if (tickCount % 2 == 0)
                    {
                        await BroadcastTelemetryAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in TelemetryBroadcastWorker execution loop.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
    }

    private async Task RefreshDatabaseSnapshotsAsync(CancellationToken ct, bool refreshHotspots = false)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Optimized state count query to avoid pulling the entire Riders table into memory
        var stateCounts = await dbContext.Riders.AsNoTracking()
            .GroupBy(r => r.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var activeRiders = stateCounts.Where(s => s.State != RiderState.OFFLINE).Sum(s => s.Count);
        var busy = stateCounts.Where(s => s.State == RiderState.BUSY).Sum(s => s.Count);
        var idle = stateCounts.Where(s => s.State == RiderState.IDLE || s.State == RiderState.RESERVED).Sum(s => s.Count);
        var offline = stateCounts.Where(s => s.State == RiderState.OFFLINE || s.State == RiderState.STALE).Sum(s => s.Count);

        var queueSize = await dbContext.Orders.AsNoTracking()
            .Where(o => o.State == OrderState.MATCHING || o.State == OrderState.OFFERING)
            .CountAsync(ct);

        var completedOrdersCount = await dbContext.Orders.AsNoTracking()
            .Where(o => o.State == OrderState.COMPLETED)
            .CountAsync(ct);

        var totalRidersCount = stateCounts.Sum(s => s.Count);
        double avgDeliveries = totalRidersCount > 0 ? completedOrdersCount / (double)totalRidersCount : 0.0;

        _aggregator.UpdateSnapshot(
            activeRidersCount: activeRiders,
            dispatchQueueSize: queueSize,
            ridersBusyCount: busy,
            ridersIdleCount: idle,
            ridersOfflineCount: offline,
            averageDeliveriesPerRider: Math.Round(avgDeliveries, 1)
        );

        if (refreshHotspots)
        {
            // 1. ค้นหา Demand Hotspots (พิกัดร้านค้าที่มีออเดอร์หนาแน่นที่สุดในช่วงเวลา 1 ชั่วโมง)
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var recentOrders = await dbContext.Orders.AsNoTracking()
                .Where(o => o.CreatedAt >= oneHourAgo && o.PickupLocation != null)
                .Select(o => new { Lat = o.PickupLocation!.Y, Lng = o.PickupLocation!.X })
                .ToListAsync(ct);

            var hotspots = recentOrders
                .GroupBy(o => new { LatGrid = Math.Round(o.Lat, 3), LngGrid = Math.Round(o.Lng, 3) })
                .Select(g => new { Lat = g.Key.LatGrid, Lng = g.Key.LngGrid, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(10)
                .ToList();

            // 2. บันทึกลง Redis operational cache (Heatmap) เพื่อดึงไปแสดงฝั่ง Dashboard ทันที
            try
            {
                var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
                var db = redis.GetDatabase();
                var hotspotsJson = JsonSerializer.Serialize(hotspots);
                await db.StringSetAsync("riders:hotspots:heatmap", hotspotsJson, TimeSpan.FromHours(1));

                if (hotspots.Any())
                {
                    _logger.LogInformation("[Predictive Dispatch] Analyzed demand hotspots grid. Top hotspot count: {Count} orders at ({Lat}, {Lng})", 
                        hotspots[0].Count, hotspots[0].Lat, hotspots[0].Lng);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache Demand Hotspots to Redis.");
            }
        }
    }

    private async Task BroadcastTelemetryAsync(CancellationToken ct)
    {
        // Compute GPS/sec in a 2-second window
        var telemetry = _aggregator.GetTelemetry(windowSeconds: 2.0);
        var utilization = _aggregator.GetUtilization();

        // Check if anything has actually changed compared to the last broadcast
        bool activeRidersChanged = telemetry.ActiveRidersCount != _lastActiveRidersCount;
        
        // Enforce 0.5 Hz tolerance to suppress numeric noise/jitter, and skip evaluation if active fleet is empty
        bool gpsUpdatesChanged = telemetry.ActiveRidersCount > 0 && 
                                 Math.Abs(telemetry.GpsUpdatesPerSecond - _lastGpsUpdatesPerSecond) >= 0.5;

        bool queueSizeChanged = telemetry.DispatchQueueSize != _lastDispatchQueueSize;
        bool busyCountChanged = utilization.RidersBusyCount != _lastRidersBusyCount;
        bool idleCountChanged = utilization.RidersIdleCount != _lastRidersIdleCount;
        bool offlineCountChanged = utilization.RidersOfflineCount != _lastRidersOfflineCount;
        bool avgDeliveriesChanged = Math.Abs(utilization.AverageDeliveriesPerRider - _lastAverageDeliveriesPerRider) > 0.01;

        bool hasChanges = activeRidersChanged || gpsUpdatesChanged || queueSizeChanged || 
                          busyCountChanged || idleCountChanged || offlineCountChanged || avgDeliveriesChanged;

        if (!hasChanges)
        {
            // Skip broadcast if there are no telemetry changes to reduce SignalR message count & frontend DOM thrashes
            return;
        }

        _logger.LogInformation("Broadcasting Telemetry Updates due to changes: " +
            "ActiveRiders: {OldActive} -> {NewActive} ({ActiveChanged}), " +
            "GpsUpdates/s: {OldGps} -> {NewGps} ({GpsChanged}), " +
            "QueueSize: {OldQueue} -> {NewQueue} ({QueueChanged}), " +
            "Busy: {OldBusy} -> {NewBusy} ({BusyChanged}), " +
            "Idle: {OldIdle} -> {NewIdle} ({IdleChanged}), " +
            "Offline: {OldOffline} -> {NewOffline} ({OfflineChanged}), " +
            "AvgDeliveries: {OldAvg} -> {NewAvg} ({AvgChanged})",
            _lastActiveRidersCount, telemetry.ActiveRidersCount, activeRidersChanged,
            _lastGpsUpdatesPerSecond, telemetry.GpsUpdatesPerSecond, gpsUpdatesChanged,
            _lastDispatchQueueSize, telemetry.DispatchQueueSize, queueSizeChanged,
            _lastRidersBusyCount, utilization.RidersBusyCount, busyCountChanged,
            _lastRidersIdleCount, utilization.RidersIdleCount, idleCountChanged,
            _lastRidersOfflineCount, utilization.RidersOfflineCount, offlineCountChanged,
            _lastAverageDeliveriesPerRider, utilization.AverageDeliveriesPerRider, avgDeliveriesChanged);

        // Cache new values
        _lastActiveRidersCount = telemetry.ActiveRidersCount;
        _lastGpsUpdatesPerSecond = telemetry.GpsUpdatesPerSecond;
        _lastDispatchQueueSize = telemetry.DispatchQueueSize;
        _lastRidersBusyCount = utilization.RidersBusyCount;
        _lastRidersIdleCount = utilization.RidersIdleCount;
        _lastRidersOfflineCount = utilization.RidersOfflineCount;
        _lastAverageDeliveriesPerRider = utilization.AverageDeliveriesPerRider;

        // Broadcast to SignalR Admins group
        await _hubContext.Clients.Group("admins").SendAsync("TelemetryUpdated", new
        {
            Telemetry = telemetry,
            Utilization = utilization
        }, cancellationToken: ct);
    }
}
