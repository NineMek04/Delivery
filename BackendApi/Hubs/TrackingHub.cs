using System.Security.Claims;
using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using BackendApi.Security;
using BackendApi.Services.Dispatch;
using BackendApi.Services.Telemetry;
using BackendApi.Services.Tracking;
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
public partial class TrackingHub : Hub
{
    private readonly IRiderPresenceManager _presenceManager;
    private readonly GpsSyncBuffer _gpsBuffer;
    private readonly DispatchService _dispatchService;
    private readonly DispatchOfferHandler _offerHandler;
    private readonly IConfiguration _config;
    private readonly IEventBus _eventBus;
    private readonly TelemetryAggregator _aggregator;
    private readonly ILogger<TrackingHub> _logger;

    private const string AdminGroup = "admins";
    private static string RiderGroup(string riderId) => $"rider:{riderId}";

    public TrackingHub(
        IRiderPresenceManager presenceManager,
        GpsSyncBuffer gpsBuffer,
        DispatchService dispatchService,
        DispatchOfferHandler offerHandler,
        IConfiguration config,
        IEventBus eventBus,
        TelemetryAggregator aggregator,
        ILogger<TrackingHub> logger)
    {
        _presenceManager = presenceManager;
        _gpsBuffer = gpsBuffer;
        _dispatchService = dispatchService;
        _offerHandler = offerHandler;
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
            // Allow anonymous connections from localhost for the local testing E2E map
            var httpContext = Context.GetHttpContext();
            var host = httpContext?.Request.Host.Host;
            var isLocal = host == "localhost" || host == "127.0.0.1" || host == "0.0.0.0";
            if (isLocal)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup);
                await base.OnConnectedAsync();
                return;
            }

            Context.Abort();
            return;
        }

        if (role == AuthConstants.AdminRole || role == AuthConstants.DispatcherRole)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup);
        }
        else if (role == AuthConstants.RiderRole)
        {
            var connectResult = await _presenceManager.HandleRiderConnectAsync(userId);
            if (connectResult is not null)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, RiderGroup(connectResult.RiderId));

                // แจ้ง Admin Dashboard ว่ามีไรเดอร์ออนไลน์ใหม่
                await Clients.Group(AdminGroup).SendAsync("RiderStatusUpdated", new
                {
                    RiderId = connectResult.RiderId,
                    NewStatus = connectResult.State.ToString(),
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
            var disconnectResult = await _presenceManager.HandleRiderConnectionDisconnectAsync(userId);
            if (disconnectResult is not null)
            {
                _logger.LogWarning(
                    "Rider {RiderId} disconnected unexpectedly ({OldState} → STALE). Waiting for reconnect or heartbeat timeout.",
                    disconnectResult.RiderId, disconnectResult.PreviousState);

                // แจ้ง Admin Dashboard ว่าไรเดอร์หลุด
                await Clients.Group(AdminGroup).SendAsync("RiderStatusUpdated", new
                {
                    RiderId = disconnectResult.RiderId,
                    NewStatus = RiderState.STALE.ToString(),
                    PreviousStatus = disconnectResult.PreviousState?.ToString(),
                    Reason = "network_disconnect",
                    Timestamp = DateTime.UtcNow
                });
            }
            else
            {
                _logger.LogInformation("Rider User {UserId} SignalR disconnected (no active rider session or already OFFLINE).", userId);
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

        return await _presenceManager.GetRiderIdByUserIdAsync(userId);
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
