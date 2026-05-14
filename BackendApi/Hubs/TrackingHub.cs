using System.Security.Claims;
using BackendApi.Data;
using BackendApi.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace BackendApi.Hubs;

/// <summary>
/// SignalR Hub สำหรับ real-time GPS tracking
/// รองรับการส่งพิกัดจาก Rider App → Server → Admin Dashboard
/// </summary>
[Authorize]
public class TrackingHub : Hub
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<TrackingHub> _logger;

    // ===== ชื่อ Groups =====
    private const string AdminGroup = "admins";
    private static string RiderGroup(string riderId) => $"rider:{riderId}";

    public TrackingHub(ApplicationDbContext dbContext, ILogger<TrackingHub> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// เมื่อ client เชื่อมต่อ → เพิ่มเข้า group ตาม role
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (role is null || userId is null)
        {
            _logger.LogWarning("Connection rejected — missing role or userId");
            Context.Abort();
            return;
        }

        // Admin / Dispatcher → เข้า admin group เพื่อรับ broadcast ตำแหน่ง Rider ทุกคน
        if (role == AuthConstants.AdminRole || role == AuthConstants.DispatcherRole)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup);
            _logger.LogInformation("Admin/Dispatcher {UserId} connected to tracking hub", userId);
        }

        // Rider → ค้นหา RiderId จาก User แล้วเข้า rider group
        if (role == AuthConstants.RiderRole)
        {
            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user?.RiderId is not null)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, RiderGroup(user.RiderId));

                // อัปเดตสถานะ Rider เป็น AVAILABLE เมื่อเชื่อมต่อ
                var rider = await _dbContext.Riders.FindAsync(user.RiderId);
                if (rider is not null && rider.Status == "OFFLINE")
                {
                    rider.Status = "AVAILABLE";
                    rider.LastUpdated = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                }

                _logger.LogInformation("Rider {RiderId} connected to tracking hub", user.RiderId);
            }
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// เมื่อ client ตัดการเชื่อมต่อ → ถ้าเป็น Rider ให้ตั้งสถานะเป็น OFFLINE
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (role == AuthConstants.RiderRole && userId is not null)
        {
            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user?.RiderId is not null)
            {
                var rider = await _dbContext.Riders.FindAsync(user.RiderId);
                if (rider is not null)
                {
                    rider.Status = "OFFLINE";
                    rider.LastUpdated = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                }

                _logger.LogInformation("Rider {RiderId} disconnected from tracking hub", user.RiderId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Rider ส่งพิกัด GPS มาอัปเดต
    /// Client เรียก: hubConnection.invoke("UpdateLocation", lat, lng, accuracy)
    /// </summary>
    public async Task UpdateLocation(double lat, double lng, double accuracy)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (userId is null || role != AuthConstants.RiderRole)
        {
            await Clients.Caller.SendAsync("Error", "ไม่มีสิทธิ์ส่งพิกัด");
            return;
        }

        // ตรวจสอบค่าพิกัดเบื้องต้น
        if (lat < -90 || lat > 90 || lng < -180 || lng > 180)
        {
            await Clients.Caller.SendAsync("Error", "พิกัดไม่ถูกต้อง");
            return;
        }

        // กรองค่าความแม่นยำ GPS ที่ต่ำเกินไป (GPS Drift protection)
        if (accuracy > 50)
        {
            _logger.LogDebug("GPS update from user {UserId} rejected — accuracy {Accuracy}m too low", userId, accuracy);
            return; // เงียบ ๆ ไม่ต้อง error กลับไป
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.RiderId is null) return;

        // อัปเดตพิกัดใน DB
        var rider = await _dbContext.Riders.FindAsync(user.RiderId);
        if (rider is null) return;

        rider.CurrentLocation = new Point(lng, lat) { SRID = 4326 }; // PostGIS: X=Lng, Y=Lat
        rider.LastUpdated = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Broadcast ไปยัง Admin Dashboard
        var locationUpdate = new
        {
            RiderId = user.RiderId,
            RiderName = rider.Name,
            Lat = lat,
            Lng = lng,
            Accuracy = accuracy,
            Status = rider.Status,
            Timestamp = rider.LastUpdated
        };

        await Clients.Group(AdminGroup).SendAsync("RiderLocationUpdated", locationUpdate);

        _logger.LogDebug("Rider {RiderId} location updated: ({Lat}, {Lng})", user.RiderId, lat, lng);
    }

    /// <summary>
    /// Rider อัปเดตสถานะการทำงาน (AVAILABLE / DELIVERING / OFFLINE)
    /// Client เรียก: hubConnection.invoke("UpdateStatus", "AVAILABLE")
    /// </summary>
    public async Task UpdateStatus(string status)
    {
        var allowedStatuses = new[] { "AVAILABLE", "DELIVERING", "OFFLINE" };
        if (!allowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            await Clients.Caller.SendAsync("Error", "สถานะไม่ถูกต้อง");
            return;
        }

        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return;

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user?.RiderId is null) return;

        var rider = await _dbContext.Riders.FindAsync(user.RiderId);
        if (rider is null) return;

        rider.Status = status.ToUpperInvariant();
        rider.LastUpdated = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // แจ้ง Admin
        await Clients.Group(AdminGroup).SendAsync("RiderStatusChanged", new
        {
            RiderId = user.RiderId,
            RiderName = rider.Name,
            Status = rider.Status,
            Timestamp = rider.LastUpdated
        });

        _logger.LogInformation("Rider {RiderId} status changed to {Status}", user.RiderId, rider.Status);
    }
}
