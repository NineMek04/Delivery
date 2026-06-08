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
    private const string SpeedBufferPrefix = "riders:speed_buffer:"; // List for 5-point moving average
    private const int SpeedBufferSize = 5;                      // จำนวนจุด GPS สำหรับ Moving Average

    public RiderPresenceService(IConnectionMultiplexer redis, ILogger<RiderPresenceService> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    // ── GPS Operations ─────────────────────────────────────────────

    /// <summary>
    /// อัปเดตพิกัด GPS ของ Rider ใน Redis (GEOADD + Hash + Speed Buffer)
    /// </summary>
    public virtual async Task UpdateGpsAsync(string riderId, double lat, double lng, double speedKmh = 0.0)
    {
        try
        {
            var batch = _db.CreateBatch();

            // GEOADD สำหรับ spatial query (GEORADIUS)
            _ = batch.GeoAddAsync(GeoKey, lng, lat, riderId);

            // Hash สำหรับเก็บรายละเอียด (timestamp, lat, lng, speed_kmh)
            var gpsKey = GpsPrefix + riderId;
            _ = batch.HashSetAsync(gpsKey, new[]
            {
                new HashEntry("lat", lat),
                new HashEntry("lng", lng),
                new HashEntry("updated_at", DateTime.UtcNow.Ticks),
                new HashEntry("speed_kmh", speedKmh)
            });
            _ = batch.KeyExpireAsync(gpsKey, TimeSpan.FromHours(24));

            // เพิ่มค่าความเร็วลง Speed Buffer (5-point Moving Average)
            if (speedKmh > 0)
            {
                var bufferKey = SpeedBufferPrefix + riderId;
                _ = batch.ListRightPushAsync(bufferKey, speedKmh);
                _ = batch.ListTrimAsync(bufferKey, -SpeedBufferSize, -1); // เก็บแค่ 5 จุดล่าสุด
                _ = batch.KeyExpireAsync(bufferKey, TimeSpan.FromMinutes(5));
            }

            // Fire-and-forget: StackExchange.Redis IBatch.Execute() is inherently
            // non-awaitable. This is by design for hot-path GPS presence updates
            // where we trade consistency for throughput.
            batch.Execute();
            await Task.CompletedTask;

            _logger.LogDebug("GPS updated: Rider {RiderId} → ({Lat}, {Lng}), Speed: {Speed} km/h", riderId, lat, lng, speedKmh);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — Failed to update GPS coordinates for Rider {RiderId}", riderId);
        }
    }

    /// <summary>
    /// อัปเดต Heartbeat ของ Rider (แยกจาก GPS)
    /// </summary>
    public virtual async Task UpdateHeartbeatAsync(string riderId)
    {
        try
        {
            var key = HeartbeatPrefix + riderId;
            await _db.StringSetAsync(key, DateTime.UtcNow.Ticks, TimeSpan.FromMinutes(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — Failed to update Heartbeat for Rider {RiderId}", riderId);
        }
    }

    /// <summary>
    /// ดึง Rider ที่อยู่ใกล้จุดที่กำหนดภายในรัศมี (GEORADIUS)
    /// </summary>
    public virtual async Task<GeoRadiusResult[]> GetNearbyRidersAsync(double lat, double lng, double radiusKm)
    {
        try
        {
            return await _db.GeoRadiusAsync(
                GeoKey,
                lng, lat,
                radiusKm,
                GeoUnit.Kilometers,
                order: Order.Ascending,  // เรียงจากใกล้ไปไกล
                options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — Failed to get nearby riders from Redis spatial index");
            return Array.Empty<GeoRadiusResult>();
        }
    }

    /// <summary>
    /// ดึงตำแหน่ง GPS ล่าสุดของ Rider จาก Redis
    /// </summary>
    public virtual async Task<(double Lat, double Lng, DateTime UpdatedAt)?> GetLastKnownLocationAsync(string riderId)
    {
        try
        {
            var gpsKey = GpsPrefix + riderId;
            var entries = await _db.HashGetAllAsync(gpsKey);

            if (entries.Length == 0) return null;

            var lat = (double)entries.FirstOrDefault(e => e.Name == "lat").Value;
            var lng = (double)entries.FirstOrDefault(e => e.Name == "lng").Value;
            var ticks = (long)entries.FirstOrDefault(e => e.Name == "updated_at").Value;

            return (lat, lng, new DateTime(ticks, DateTimeKind.Utc));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — Failed to get last known location for Rider {RiderId}", riderId);
            return null;
        }
    }

    /// <summary>
    /// ดึงค่าความเร็วเฉลี่ยของ Rider จาก 5-point Moving Average buffer ใน Redis
    /// Fallback: ดึงจาก Hash field speed_kmh (instant speed)
    /// </summary>
    public virtual async Task<double> GetRiderSpeedAsync(string riderId)
    {
        try
        {
            // ลองดึงจาก Moving Average Buffer ก่อน
            var bufferKey = SpeedBufferPrefix + riderId;
            var speedValues = await _db.ListRangeAsync(bufferKey);

            if (speedValues.Length > 0)
            {
                double sum = 0;
                int count = 0;
                foreach (var val in speedValues)
                {
                    if (val.TryParse(out double parsedSpeed) && parsedSpeed > 0)
                    {
                        sum += parsedSpeed;
                        count++;
                    }
                }
                if (count > 0)
                    return sum / count;
            }

            // Fallback: ดึงจาก Hash field (instant speed ล่าสุด)
            var gpsKey = GpsPrefix + riderId;
            var speed = await _db.HashGetAsync(gpsKey, "speed_kmh");
            return speed.HasValue ? (double)speed : 0.0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — Failed to get speed buffer for Rider {RiderId}. Defaulting to 0.0", riderId);
            return 0.0;
        }
    }

    /// <summary>
    /// ดึงเวลา Heartbeat ล่าสุดของ Rider
    /// </summary>
    public async Task<DateTime?> GetLastHeartbeatAsync(string riderId)
    {
        try
        {
            var key = HeartbeatPrefix + riderId;
            var value = await _db.StringGetAsync(key);

            if (!value.HasValue) return null;

            return new DateTime((long)value, DateTimeKind.Utc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — Failed to get last heartbeat for Rider {RiderId}", riderId);
            return null;
        }
    }

    /// <summary>
    /// ลบข้อมูลทั้งหมดของ Rider ออกจาก Redis (เมื่อ OFFLINE)
    /// </summary>
    public async Task RemoveRiderAsync(string riderId)
    {
        try
        {
            var batch = _db.CreateBatch();

            _ = batch.GeoRemoveAsync(GeoKey, riderId);
            _ = batch.KeyDeleteAsync(GpsPrefix + riderId);
            _ = batch.KeyDeleteAsync(HeartbeatPrefix + riderId);
            _ = batch.KeyDeleteAsync("riders:status:" + riderId);
            _ = batch.KeyDeleteAsync("riders:active_order:" + riderId);

            batch.Execute();
            await Task.CompletedTask;

            _logger.LogInformation("Rider {RiderId} removed from Redis presence", riderId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable — Failed to remove Rider {RiderId} presence cache", riderId);
        }
    }
}
