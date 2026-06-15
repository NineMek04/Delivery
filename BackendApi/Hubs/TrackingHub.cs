using System.Security.Claims;
using BackendApi.Core.StateMachines;
using BackendApi.Security;
using BackendApi.Services.Dispatch;
using BackendApi.Services.Telemetry;
using BackendApi.Services.Tracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

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
    private readonly IRiderPresenceManager _presenceManager;
    private readonly DispatchOfferHandler _offerHandler;
    private readonly TelemetryService _telemetryService;
    private readonly ILogger<TrackingHub> _logger;

    private const string AdminGroup = "admins";
    private static string RiderGroup(string riderId) => $"rider:{riderId}";

    public TrackingHub(
        IRiderPresenceManager presenceManager,
        DispatchOfferHandler offerHandler,
        TelemetryService telemetryService,
        ILogger<TrackingHub> logger)
    {
        _presenceManager = presenceManager;
        _offerHandler = offerHandler;
        _telemetryService = telemetryService;
        _logger = logger;
    }

    // ── Connection Lifecycle ────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (role is null || userId is null)
        {
            // [SECURITY FIX] Anonymous loopback bypass removed.
            // Any connection without a valid JWT must be rejected — regardless of origin IP.
            // Rationale: loopback IP can be spoofed via misconfigured reverse-proxy X-Real-IP headers,
            // and any process on the same host would gain unrestricted admin-group access.
            _logger.LogWarning("SignalR connection rejected: missing role or userId claim. ConnectionId={ConnectionId}", Context.ConnectionId);
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
                Context.Items["RiderId"] = connectResult.RiderId;
                await Groups.AddToGroupAsync(Context.ConnectionId, RiderGroup(connectResult.RiderId));

                // ดึงพิกัดล่าสุดจาก Redis เพื่อส่งพร้อม status
                // ถ้าไม่มีพิกัด (Rider เพิ่ง register) ให้ส่ง null → Dashboard จะข้าม marker
                var lastLoc = await _presenceManager.GetLastKnownLocationForRiderAsync(connectResult.RiderId);

                // แจ้ง Admin Dashboard ว่ามีไรเดอร์ออนไลน์ใหม่ พร้อมพิกัดล่าสุด
                await Clients.Group(AdminGroup).SendAsync("RiderStatusUpdated", new
                {
                    RiderId = connectResult.RiderId,
                    NewStatus = connectResult.State.ToString(),
                    Lat = lastLoc?.Lat,
                    Lng = lastLoc?.Lng,
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
            var shopId = Context.User?.FindFirst("shop_id")?.Value;
            if (!string.IsNullOrWhiteSpace(shopId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"store:{shopId}");
                _logger.LogInformation("StorePartner {UserId} connected to shop group store:{ShopId}", userId, shopId);
            }
            else
            {
                _logger.LogWarning(
                    "StorePartner connection rejected because ShopId claim is missing. UserId={UserId}, ConnectionId={ConnectionId}",
                    userId,
                    Context.ConnectionId);
                Context.Abort();
                return;
            }
        }

        var roleLabel = MapRoleToLabel(role);
        OperationalMetrics.SignalrActiveConnections.WithLabels(roleLabel).Inc();

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

        var roleLabel = MapRoleToLabel(role);
        OperationalMetrics.SignalrActiveConnections.WithLabels(roleLabel).Dec();

        await base.OnDisconnectedAsync(exception);
    }

    // ── Shared Utility Methods ──────────────────────────────────────

    private async Task<string?> GetRiderIdAsync()
    {
        if (Context.Items.TryGetValue("RiderId", out var cachedRiderId) && cachedRiderId is string rId)
        {
            return rId;
        }

        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (userId is null || role != AuthConstants.RiderRole) return null;

        var riderId = await _presenceManager.GetRiderIdByUserIdAsync(userId);
        if (riderId is not null)
        {
            Context.Items["RiderId"] = riderId;
        }
        return riderId;
    }

    private static string MapRoleToLabel(string? role)
    {
        if (role == AuthConstants.AdminRole || role == AuthConstants.DispatcherRole) return "admin";
        if (role == AuthConstants.RiderRole) return "rider";
        if (role == AuthConstants.CustomerRole) return "customer";
        if (role == AuthConstants.StorePartnerRole) return "store";
        return "unknown";
    }
}
