using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using BackendApi.Services.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Services.BackgroundWorkers;

/// <summary>
/// Heartbeat Monitor — ตรวจสอบ Rider ที่ "หายไป" (Ghost Rider)
/// 
/// Logic:
///   - ไม่ส่ง heartbeat เกิน HeartbeatTimeoutSeconds → STALE
///   - STALE เกิน StaleToOfflineSeconds → OFFLINE
///   - ถ้ามีงาน OFFERING อยู่ → ยกเลิก Offer + Re-dispatch
/// </summary>
public class HeartbeatMonitor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<HeartbeatMonitor> _logger;

    public HeartbeatMonitor(
        IServiceProvider serviceProvider,
        IConfiguration config,
        ILogger<HeartbeatMonitor> logger)
    {
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HeartbeatMonitor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckRiderHeartbeatsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown — stop the loop cleanly
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HeartbeatMonitor");
                // Wait before retrying, but still respect cancellation
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("HeartbeatMonitor stopped");
    }

    private async Task CheckRiderHeartbeatsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var presenceService = scope.ServiceProvider.GetRequiredService<RiderPresenceService>();
        var stateMachine = scope.ServiceProvider.GetRequiredService<StateMachineService>();
        var offerHandler = scope.ServiceProvider.GetRequiredService<DispatchOfferHandler>();

        var now = DateTime.UtcNow;
        var heartbeatTimeout = _config.GetValue("Dispatch:HeartbeatTimeoutSeconds", 20);
        var staleToOffline = _config.GetValue("Dispatch:StaleToOfflineSeconds", 120);

        // 1. Rider ที่ IDLE/RESERVED/BUSY แต่ heartbeat หมดอายุ → STALE
        var activeRiders = await dbContext.Riders
            .Where(r =>
                r.State == RiderState.IDLE ||
                r.State == RiderState.RESERVED ||
                r.State == RiderState.BUSY)
            .ToListAsync(ct);

        if (activeRiders.Count > 0)
        {
            // Pipelined concurrent fetch of all heartbeats from Redis
            var heartbeatTasks = activeRiders.Select(async rider =>
            {
                var lastHeartbeat = await presenceService.GetLastHeartbeatAsync(rider.Id);
                return (Rider: rider, LastHeartbeat: lastHeartbeat);
            }).ToList();

            var riderHeartbeats = await Task.WhenAll(heartbeatTasks);

            foreach (var item in riderHeartbeats)
            {
                if (ct.IsCancellationRequested) break;
                var rider = item.Rider;
                var lastHeartbeat = item.LastHeartbeat;

                if (lastHeartbeat is null || (now - lastHeartbeat.Value).TotalSeconds > heartbeatTimeout)
                {
                    if (rider.State != RiderState.STALE)
                    {
                        _logger.LogWarning(
                            "Rider {RiderId} went STALE — last heartbeat: {LastHB}",
                            rider.Id, lastHeartbeat?.ToString("HH:mm:ss") ?? "never");

                        var transitioned = await stateMachine.TransitionRiderAsync(rider, RiderState.STALE);

                        // ถ้ามี Offer ค้างอยู่ → Re-dispatch
                        if (transitioned)
                        {
                            var offeringOrder = await dbContext.Orders
                                .FirstOrDefaultAsync(o =>
                                    o.AssignedRiderId == rider.Id &&
                                    o.State == OrderState.OFFERING, ct);

                            if (offeringOrder?.CurrentOfferId is not null)
                            {
                                await offerHandler.RejectOrTimeoutAsync(
                                    offeringOrder.CurrentOfferId, rider.Id);
                            }
                        }
                    }
                }
            }
        }

        // 2. Rider ที่ STALE เกิน threshold → OFFLINE
        var staleRiders = await dbContext.Riders
            .Where(r => r.State == RiderState.STALE)
            .ToListAsync(ct);

        if (staleRiders.Count > 0)
        {
            // Pipelined concurrent fetch of all stale heartbeats from Redis
            var staleHeartbeatTasks = staleRiders.Select(async rider =>
            {
                var lastHeartbeat = await presenceService.GetLastHeartbeatAsync(rider.Id);
                return (Rider: rider, LastHeartbeat: lastHeartbeat);
            }).ToList();

            var staleRiderHeartbeats = await Task.WhenAll(staleHeartbeatTasks);

            foreach (var item in staleRiderHeartbeats)
            {
                if (ct.IsCancellationRequested) break;
                var rider = item.Rider;
                var lastHeartbeat = item.LastHeartbeat;

                var staleDuration = lastHeartbeat.HasValue
                    ? (now - lastHeartbeat.Value).TotalSeconds
                    : staleToOffline + 1; // ถ้าไม่เคยส่ง → ถือว่าเกิน

                if (staleDuration > staleToOffline)
                {
                    _logger.LogInformation(
                        "Rider {RiderId} offline — stale for {Duration}s",
                        rider.Id, staleDuration);

                    await stateMachine.TransitionRiderAsync(rider, RiderState.OFFLINE);
                    await presenceService.RemoveRiderAsync(rider.Id);
                }
            }
        }
    }
}
