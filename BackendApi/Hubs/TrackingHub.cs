using System.Security.Claims;
using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using BackendApi.Security;
using BackendApi.Services.Dispatch;
using BackendApi.Services.Telemetry;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Infrastructure.EventBus.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Hubs;

/// <summary>
/// Realtime Gateway — สื่อสารสองทางระหว่าง Rider App / Admin Dashboard และ Backend
/// 
/// Partial class structure:
///   - TrackingHub.cs          → Core (constructor, lifecycle, utilities)
///   - TrackingHub.Location.cs → GPS (UpdateLocation, UpdateRiderLocation, UpdateHeartbeat)
///   - TrackingHub.RiderStatus.cs → Status (UpdateRiderStatus, UpdateStatus)
///   - TrackingHub.Dispatch.cs → Offers (AcceptOffer, RejectOffer)
/// </summary>
[Authorize]
public partial class TrackingHub : Hub
{
    private readonly ApplicationDbContext _dbContext;
    private readonly RiderPresenceService _presenceService;
    private readonly GpsSyncBuffer _gpsBuffer;
    private readonly DispatchService _dispatchService;
    private readonly StateMachineService _stateMachine;
    private readonly IConfiguration _config;
    private readonly IEventBus _eventBus;
    private readonly TelemetryAggregator _aggregator;
    private readonly ILogger<TrackingHub> _logger;

    private const string AdminGroup = "admins";
    private static string RiderGroup(string riderId) => $"rider:{riderId}";

    public TrackingHub(
        ApplicationDbContext dbContext,
        RiderPresenceService presenceService,
        GpsSyncBuffer gpsBuffer,
        DispatchService dispatchService,
        StateMachineService stateMachine,
        IConfiguration config,
        IEventBus eventBus,
        TelemetryAggregator aggregator,
        ILogger<TrackingHub> logger)
    {
        _dbContext = dbContext;
        _presenceService = presenceService;
        _gpsBuffer = gpsBuffer;
        _dispatchService = dispatchService;
        _stateMachine = stateMachine;
        _config = config;
        _eventBus = eventBus;
        _aggregator = aggregator;
        _logger = logger;
    }

    // ── Connection Lifecycle ────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (role is null || userId is null)
        {
            Context.Abort();
            return;
        }

        if (role == AuthConstants.AdminRole || role == AuthConstants.DispatcherRole)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup);
        }
        else if (role == AuthConstants.RiderRole)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.RiderId is not null)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, RiderGroup(user.RiderId));
                await _presenceService.UpdateHeartbeatAsync(user.RiderId);

                var rider = await _dbContext.Riders.FindAsync(user.RiderId);
                if (rider is null) return;

                if (rider.State == RiderState.OFFLINE)
                {
                    await _stateMachine.TransitionRiderAsync(rider, RiderState.IDLE);
                }
                else if (rider.State == RiderState.STALE)
                {
                    // กู้คืนสถานะ
                    var newState = await HasActiveJobAsync(rider.Id) ? RiderState.BUSY : RiderState.IDLE;
                    await _stateMachine.TransitionRiderAsync(rider, newState);
                }

                // แจ้ง Admin Dashboard ว่ามีไรเดอร์ออนไลน์ใหม่
                await Clients.Group(AdminGroup).SendAsync("RiderStatusUpdated", new
                {
                    RiderId = user.RiderId,
                    NewStatus = rider.State.ToString(),
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        else if (role == AuthConstants.CustomerRole)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"customer:{userId}");
        }
        else if (role == AuthConstants.StorePartnerRole)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "stores");
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Network Drop Fallback — เมื่อไรเดอร์หลุดจากเครือข่ายกะทันหัน
    /// (เน็ตตัด, เข้าใต้ตึก, แบตหมด) ระบบจะ:
    /// 1. เปลี่ยนสถานะเป็น STALE ทันที (ชั่วคราว เผื่อกลับมาภายใน 15 วินาที)
    /// 2. ลบจาก GEO index เพื่อป้องกันไม่ให้ถูก dispatch งานใหม่
    /// 3. HeartbeatMonitor จะเก็บกวาดจาก STALE → OFFLINE หากไม่กลับมา
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (role == AuthConstants.RiderRole && userId is not null)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.RiderId is not null)
            {
                var rider = await _dbContext.Riders.FindAsync(user.RiderId);
                if (rider is not null && rider.State != RiderState.OFFLINE)
                {
                    var oldState = rider.State;

                    // เปลี่ยนเป็น STALE ทันที เพื่อหลีกเลี่ยงไม่ให้ถูก dispatch ใหม่
                    var transitioned = await _stateMachine.TransitionRiderAsync(rider, RiderState.STALE);

                    if (transitioned)
                    {
                        _logger.LogWarning(
                            "Rider {RiderId} disconnected unexpectedly ({OldState} → STALE). Waiting for reconnect or heartbeat timeout.",
                            user.RiderId, oldState);

                        // แจ้ง Admin Dashboard ว่าไรเดอร์หลุด
                        await Clients.Group(AdminGroup).SendAsync("RiderStatusUpdated", new
                        {
                            RiderId = user.RiderId,
                            NewStatus = RiderState.STALE.ToString(),
                            PreviousStatus = oldState.ToString(),
                            Reason = "network_disconnect",
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    _logger.LogInformation("Rider {RiderId} SignalR disconnected (already OFFLINE).", user.RiderId);
                }
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ── Shared Utility Methods ──────────────────────────────────────

    private async Task<string?> GetRiderIdAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (userId is null || role != AuthConstants.RiderRole) return null;

        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user?.RiderId;
    }

    private async Task<bool> HasActiveJobAsync(string riderId)
    {
        return await _dbContext.Orders.AnyAsync(o => 
            o.AssignedRiderId == riderId && 
            (o.State == OrderState.ASSIGNED || o.State == OrderState.PICKING_UP || o.State == OrderState.DELIVERING));
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371e3;
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return r * c;
    }
}
