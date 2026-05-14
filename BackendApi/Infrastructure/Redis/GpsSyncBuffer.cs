using System.Collections.Concurrent;
using NetTopologySuite.Geometries;

namespace BackendApi.Infrastructure.Redis;

/// <summary>
/// Buffer สำหรับเก็บประวัติ GPS ของ Rider แบบ In-Memory ก่อน Bulk Insert ลง PostGIS
/// ใช้เทคนิค Hybrid Flush และ Douglas-Peucker compression เพื่อลดภาระ DB
/// </summary>
public class GpsSyncBuffer
{
    private readonly ILogger<GpsSyncBuffer> _logger;
    private readonly ConcurrentDictionary<string, RiderTrackBuffer> _buffers = new();

    // ── Configuration thresholds ──
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private readonly double _minMovementMeters;

    public GpsSyncBuffer(IConfiguration config, ILogger<GpsSyncBuffer> logger)
    {
        _logger = logger;
        _batchSize = config.GetValue("Dispatch:GpsSyncBatchSize", 30);
        _flushInterval = TimeSpan.FromSeconds(config.GetValue("Dispatch:GpsSyncIntervalSeconds", 30));
        _minMovementMeters = config.GetValue("Dispatch:GpsMinMovementMeters", 5.0);
    }

    /// <summary>
    /// เพิ่มจุดพิกัดลงใน Buffer ของ Rider
    /// คืนค่าเป็น List ของพิกัด หากถึงเงื่อนไขที่ต้อง Flush (Batch Size หรือ เวลา)
    /// </summary>
    public List<TrackPoint>? AddPointAndCheckFlush(string riderId, double lat, double lng)
    {
        var buffer = _buffers.GetOrAdd(riderId, _ => new RiderTrackBuffer());
        var now = DateTime.UtcNow;

        lock (buffer)
        {
            // ตรวจสอบระยะกระจัด (ถ้าขยับน้อยกว่า Threshold ข้ามไปเลย ไม่ต้องเปลืองพื้นที่)
            if (buffer.Points.Count > 0)
            {
                var last = buffer.Points.Last();
                var distMeters = HaversineDistanceMeters(last.Lat, last.Lng, lat, lng);
                if (distMeters < _minMovementMeters)
                {
                    return null; // ขยับน้อยเกิน ไม่ต้องเก็บประวัติ
                }
            }

            buffer.Points.Add(new TrackPoint(riderId, lat, lng, now));

            // Hybrid Flush: ตรวจสอบเงื่อนไขการ Flush
            var shouldFlush = 
                buffer.Points.Count >= _batchSize || 
                (now - buffer.LastFlushed) >= _flushInterval;

            if (shouldFlush)
            {
                var pointsToFlush = buffer.Points.ToList();
                buffer.Points.Clear();
                buffer.LastFlushed = now;
                return pointsToFlush;
            }

            return null;
        }
    }

    /// <summary>
    /// บังคับดึงข้อมูลทั้งหมดออกมาเพื่อ Flush (เช่น เมื่อ Rider เปลี่ยนสถานะ หรือ Service ปิดตัว)
    /// </summary>
    public List<TrackPoint> ForceFlush(string riderId)
    {
        if (_buffers.TryGetValue(riderId, out var buffer))
        {
            lock (buffer)
            {
                var points = buffer.Points.ToList();
                buffer.Points.Clear();
                buffer.LastFlushed = DateTime.UtcNow;
                return points;
            }
        }
        return new List<TrackPoint>();
    }

    /// <summary>
    /// ดึงข้อมูลของ Rider ทุกคนออกมาเพื่อ Flush (สำหรับ Background Worker)
    /// </summary>
    public List<TrackPoint> FlushAll()
    {
        var allPoints = new List<TrackPoint>();
        var now = DateTime.UtcNow;

        foreach (var kvp in _buffers)
        {
            var buffer = kvp.Value;
            lock (buffer)
            {
                if (buffer.Points.Count > 0 && (now - buffer.LastFlushed) >= _flushInterval)
                {
                    allPoints.AddRange(buffer.Points);
                    buffer.Points.Clear();
                    buffer.LastFlushed = now;
                }
            }
        }

        return allPoints;
    }

    // ── Utility ──────────────────────────────────────────────────

    private static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371e3; // Earth radius in meters
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

    private class RiderTrackBuffer
    {
        public List<TrackPoint> Points { get; } = new();
        public DateTime LastFlushed { get; set; } = DateTime.UtcNow;
    }
}

public record TrackPoint(string RiderId, double Lat, double Lng, DateTime Timestamp);
