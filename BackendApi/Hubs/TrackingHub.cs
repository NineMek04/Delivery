using System.Security.Claims;
using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using BackendApi.Security;
using BackendApi.Services.Dispatch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Hubs;

/// <summary>
/// Realtime Gateway — สื่อสารสองทางระหว่าง Rider App และ Backend
/// รับพิกัด, รับ Heartbeat, และจัดการการรับ-ปฏิเสธ Offer งาน
/// </summary>
[Authorize]
public class TrackingHub : Hub
{
    private readonly ApplicationDbContext _dbContext;
    private readonly RiderPresenceService _presenceService;
    private readonly GpsSyncBuffer _gpsBuffer;
    private readonly DispatchService _dispatchService;
    private readonly StateMachineService _stateMachine;
    private readonly IConfiguration _config;
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
        ILogger<TrackingHub> logger)
    {
        _dbContext = dbContext;
        _presenceService = presenceService;
        _gpsBuffer = gpsBuffer;
        _dispatchService = dispatchService;
        _stateMachine = stateMachine;
        _config = config;
        _logger = logger;
    }

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

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (role == AuthConstants.RiderRole && userId is not null)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.RiderId is not null)
            {
                // ไม่ได้ตัดเป็น OFFLINE ทันที ปล่อยให้ HeartbeatMonitor เช็คและให้เป็น STALE ก่อน
                _logger.LogInformation("Rider {RiderId} SignalR disconnected. Waiting for heartbeat timeout.", user.RiderId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ── 1. GPS & Heartbeat ─────────────────────────────────────────

    public async Task UpdateHeartbeat()
    {
        var riderId = await GetRiderIdAsync();
        if (riderId is null) return;

        await _presenceService.UpdateHeartbeatAsync(riderId);

        // ดึงสถานะปัจจุบันกลับมาให้ Rider เผื่อหลุดและกลับมา (State Sync)
        var rider = await _dbContext.Riders.FindAsync(riderId);
        if (rider is not null && rider.State == RiderState.STALE)
        {
            var newState = await HasActiveJobAsync(riderId) ? RiderState.BUSY : RiderState.IDLE;
            await _stateMachine.TransitionRiderAsync(rider, newState);
        }
    }

    public async Task UpdateLocation(double lat, double lng, double accuracy)
    {
        var riderId = await GetRiderIdAsync();
        if (riderId is null) return;

        if (lat < -90 || lat > 90 || lng < -180 || lng > 180) return;

        if (accuracy > 50) return; // กรอง Drift เล็กๆ

        // Sanity Check: โดดไปไกลผิดปกติไหม (Teleport protection)
        var lastGps = await _presenceService.GetLastKnownLocationAsync(riderId);
        if (lastGps is not null)
        {
            var maxDriftKm = _config.GetValue("Dispatch:MaxGpsDriftKm", 5.0);
            var distMeters = HaversineDistance(lastGps.Value.Lat, lastGps.Value.Lng, lat, lng);
            var timeDiffSeconds = (DateTime.UtcNow - lastGps.Value.UpdatedAt).TotalSeconds;
            
            // ความเร็ว > 18,000 km/h (5 km in 1 sec)
            if (timeDiffSeconds > 0 && (distMeters / 1000.0) / (timeDiffSeconds / 3600.0) > 18000)
            {
                _logger.LogWarning("GPS Teleport detected for Rider {RiderId}", riderId);
                return;
            }
        }

        // อัปเดตลง Redis Cache
        await _presenceService.UpdateGpsAsync(riderId, lat, lng);

        // เก็บลง Buffer เพื่อเขียนลง PostGIS
        _gpsBuffer.AddPointAndCheckFlush(riderId, lat, lng);

        // อัปเดตใน Entity
        var rider = await _dbContext.Riders.FindAsync(riderId);
        if (rider is not null)
        {
            rider.CurrentLocation = new NetTopologySuite.Geometries.Point(lng, lat) { SRID = 4326 };
            rider.LastGpsUpdate = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Broadcast ไปให้ Admin
            await Clients.Group(AdminGroup).SendAsync("RiderLocationUpdated", new
            {
                RiderId = riderId,
                Lat = lat,
                Lng = lng,
                Status = rider.State.ToString(),
                Timestamp = rider.LastGpsUpdate
            });
        }
    }

    // ── 2. Dispatch Offer Handling ─────────────────────────────────

    public async Task AcceptOffer(string offerId, int version)
    {
        var riderId = await GetRiderIdAsync();
        if (riderId is null) return;

        var success = await _dispatchService.AcceptOfferAsync(riderId, offerId, version);

        if (success)
        {
            await Clients.Caller.SendAsync("OfferAcceptedResult", new { Success = true });
        }
        else
        {
            await Clients.Caller.SendAsync("OfferAcceptedResult", new { Success = false, Message = "งานนี้หลุดไปแล้ว หรือมีผู้รับแล้ว" });
        }
    }

    public async Task RejectOffer(string offerId, string orderId)
    {
        var riderId = await GetRiderIdAsync();
        if (riderId is null) return;

        await _dispatchService.RejectOrTimeoutAsync(orderId, riderId, offerId);
    }

    // ── Utility ────────────────────────────────────────────────────

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
