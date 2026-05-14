using StackExchange.Redis;

namespace BackendApi.Infrastructure.Redis;

/// <summary>
/// Rider Presence Service — จัดการ GPS Cache, Heartbeat, และ Spatial Query ผ่าน Redis
/// 
/// แยก 2 ระบบ (ตาม feedback):
///   - Heartbeat: ยังออนไลน์ไหม (last_heartbeat &lt; 10s = ONLINE)
///   - GPS Update: ตำแหน่งยังนิ่งไหม (last_gps &lt; 15s = TRACKABLE)
/// 
/// Redis ไม่ใช่ Source of Truth — ใช้เป็น Operational Cache เท่านั้น
/// PostgreSQL/PostGIS คือ Source of Truth สำหรับ Historical Data
/// </summary>
public class RiderPresenceService
{
    private readonly IDatabase _db;
    private readonly ILogger<RiderPresenceService> _logger;

    private const string GeoKey = "riders:locations";           // GEOADD key
    private const string HeartbeatPrefix = "riders:heartbeat:";  // Hash per rider
    private const string GpsPrefix = "riders:gps:";             // Hash per rider

    public RiderPresenceService(IConnectionMultiplexer redis, ILogger<RiderPresenceService> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    // ── GPS Operations ─────────────────────────────────────────────

    /// <summary>
    /// อัปเดตพิกัด GPS ของ Rider ใน Redis (GEOADD + Hash)
    /// </summary>
    public async Task UpdateGpsAsync(string riderId, double lat, double lng)
    {
        var batch = _db.CreateBatch();

        // GEOADD สำหรับ spatial query (GEORADIUS)
        batch.GeoAddAsync(GeoKey, lng, lat, riderId);

        // Hash สำหรับเก็บรายละเอียด (timestamp, lat, lng)
        var gpsKey = GpsPrefix + riderId;
        batch.HashSetAsync(gpsKey, new[]
        {
            new HashEntry("lat", lat),
            new HashEntry("lng", lng),
            new HashEntry("updated_at", DateTime.UtcNow.Ticks)
        });

        batch.Execute();
        await Task.CompletedTask;

        _logger.LogDebug("GPS updated: Rider {RiderId} → ({Lat}, {Lng})", riderId, lat, lng);
    }

    /// <summary>
    /// อัปเดต Heartbeat ของ Rider (แยกจาก GPS)
    /// </summary>
    public async Task UpdateHeartbeatAsync(string riderId)
    {
        var key = HeartbeatPrefix + riderId;
        await _db.StringSetAsync(key, DateTime.UtcNow.Ticks, TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// ดึง Rider ที่อยู่ใกล้จุดที่กำหนดภายในรัศมี (GEORADIUS)
    /// </summary>
    public async Task<GeoRadiusResult[]> GetNearbyRidersAsync(double lat, double lng, double radiusKm)
    {
        return await _db.GeoRadiusAsync(
            GeoKey,
            lng, lat,
            radiusKm,
            GeoUnit.Kilometers,
            order: Order.Ascending,  // เรียงจากใกล้ไปไกล
            options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance);
    }

    /// <summary>
    /// ดึงตำแหน่ง GPS ล่าสุดของ Rider จาก Redis
    /// </summary>
    public async Task<(double Lat, double Lng, DateTime UpdatedAt)?> GetLastKnownLocationAsync(string riderId)
    {
        var gpsKey = GpsPrefix + riderId;
        var entries = await _db.HashGetAllAsync(gpsKey);

        if (entries.Length == 0) return null;

        var lat = (double)entries.FirstOrDefault(e => e.Name == "lat").Value;
        var lng = (double)entries.FirstOrDefault(e => e.Name == "lng").Value;
        var ticks = (long)entries.FirstOrDefault(e => e.Name == "updated_at").Value;

        return (lat, lng, new DateTime(ticks, DateTimeKind.Utc));
    }

    /// <summary>
    /// ดึงเวลา Heartbeat ล่าสุดของ Rider
    /// </summary>
    public async Task<DateTime?> GetLastHeartbeatAsync(string riderId)
    {
        var key = HeartbeatPrefix + riderId;
        var value = await _db.StringGetAsync(key);

        if (!value.HasValue) return null;

        return new DateTime((long)value, DateTimeKind.Utc);
    }

    /// <summary>
    /// ลบข้อมูลทั้งหมดของ Rider ออกจาก Redis (เมื่อ OFFLINE)
    /// </summary>
    public async Task RemoveRiderAsync(string riderId)
    {
        var batch = _db.CreateBatch();

        batch.GeoRemoveAsync(GeoKey, riderId);
        batch.KeyDeleteAsync(GpsPrefix + riderId);
        batch.KeyDeleteAsync(HeartbeatPrefix + riderId);

        batch.Execute();
        await Task.CompletedTask;

        _logger.LogInformation("Rider {RiderId} removed from Redis presence", riderId);
    }
}
