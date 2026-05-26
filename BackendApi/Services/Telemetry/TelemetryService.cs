using System;
using System.Linq;
using System.Threading.Tasks;
using BackendApi.Data;
using BackendApi.Hubs;
using BackendApi.Infrastructure.Redis;
using BackendApi.Services.Ai;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using BackendApi.Core.StateMachines;

namespace BackendApi.Services.Telemetry
{
    /// <summary>
    /// Telemetry Service — จัดการการประมวลผลพิกัดความถี่สูงอย่างสมบูรณ์แบบ
    /// คอยทำ Snap-to-Road (OSRM), จัดเก็บบน Redis cache เท่านั้นใน Hot path,
    /// และทำการ Broadcast พิกัดผ่าน SignalR แบบ Dynamic Throttling
    /// </summary>
    public class TelemetryService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly RiderPresenceService _presenceService;
        private readonly GpsSyncBuffer _gpsBuffer;
        private readonly TelemetryAggregator _aggregator;
        private readonly OsrmRoutingService _routingService;
        private readonly IHubContext<TrackingHub> _hubContext;
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<TelemetryService> _logger;

        private const string AdminGroup = "admins";

        public TelemetryService(
            ApplicationDbContext dbContext,
            RiderPresenceService presenceService,
            GpsSyncBuffer gpsBuffer,
            TelemetryAggregator aggregator,
            OsrmRoutingService routingService,
            IHubContext<TrackingHub> hubContext,
            IConnectionMultiplexer redis,
            ILogger<TelemetryService> logger)
        {
            _dbContext = dbContext;
            _presenceService = presenceService;
            _gpsBuffer = gpsBuffer;
            _aggregator = aggregator;
            _routingService = routingService;
            _hubContext = hubContext;
            _redis = redis;
            _logger = logger;
        }

        /// <summary>
        /// ประมวลผลและกระจายพิกัด GPS เรียลไทม์
        /// </summary>
        public async Task ProcessLocationUpdateAsync(string riderId, double lat, double lng, double accuracy)
        {
            if (lat < -90 || lat > 90 || lng < -180 || lng > 180) return;

            // 1. กรองความคลาดเคลื่อนเบื้องต้น (Drift Protection)
            if (accuracy > 50) return;

            var now = DateTime.UtcNow;

            // 2. ทำ Snap-to-Road พิกัดให้ยึดติดกับโครงข่ายถนนผ่าน OSRM
            var (snappedLat, snappedLng) = await _routingService.SnapToRoadAsync(lat, lng);

            // 3. ป้องกันการวาร์ปกระโดดข้ามพิกัดระยะไกล (Teleport Protection)
            var lastGps = await _presenceService.GetLastKnownLocationAsync(riderId);
            if (lastGps is not null)
            {
                var distMeters = HaversineDistance(lastGps.Value.Lat, lastGps.Value.Lng, snappedLat, snappedLng);
                var timeDiffSeconds = (now - lastGps.Value.UpdatedAt).TotalSeconds;

                // ความเร็วเกิน 180 km/h (50 m/s) มีความท้าทายว่าสัญญาณ GPS ผิดเพี้ยน
                if (timeDiffSeconds > 0 && (distMeters / timeDiffSeconds) > 50.0)
                {
                    _logger.LogWarning("GPS Teleport anomaly detected for Rider {RiderId}. Movement of {Dist}m in {Time}s ignored.", 
                        riderId, Math.Round(distMeters, 1), Math.Round(timeDiffSeconds, 1));
                    return;
                }
            }

            // 4. บันทึกพิกัดเรียลไทม์ลงเฉพาะ Redis Presence Cache
            await _presenceService.UpdateGpsAsync(riderId, snappedLat, snappedLng);

            // 5. บันทึกลง In-memory Buffer เพื่อรอเขียน PostgreSQL แบบ Batch (History Ledger)
            _gpsBuffer.AddPointAndCheckFlush(riderId, snappedLat, snappedLng);

            // 6. เพิ่มตัวนับ GPS Tick สำหรับแสดงอัตราผ่านทางหน้าหลังบ้าน
            _aggregator.IncrementGpsTick();

            // 7. จัดการ Dynamic Throttling สำหรับ Broadcast ผ่าน SignalR
            var db = _redis.GetDatabase();
            var lastBroadcastKey = $"telemetry:last_broadcast:{riderId}";
            var lastBroadcast = await db.HashGetAllAsync(lastBroadcastKey);

            double throttleSeconds = 2.0; // ค่าเริ่มต้น

            if (lastBroadcast.Length > 0)
            {
                var lastLat = (double)lastBroadcast.FirstOrDefault(e => e.Name == "lat").Value;
                var lastLng = (double)lastBroadcast.FirstOrDefault(e => e.Name == "lng").Value;
                var lastTicks = (long)lastBroadcast.FirstOrDefault(e => e.Name == "ticks").Value;

                var timeDiff = (now - new DateTime(lastTicks, DateTimeKind.Utc)).TotalSeconds;
                var distanceMoved = HaversineDistance(lastLat, lastLng, snappedLat, snappedLng);

                if (timeDiff > 0)
                {
                    double speed = distanceMoved / timeDiff; // เมตรต่อวินาที

                    // คำนวณความถี่แบบ Dynamic ตามความเร็วของไรเดอร์
                    if (speed > 5.0)       // เคลื่อนที่เร็ว (> 18 km/h): Broadcast ทุกๆ 1 วินาที
                        throttleSeconds = 1.0;
                    else if (speed > 1.5)  // เคลื่อนที่ช้า (5 - 18 km/h): Broadcast ทุกๆ 2 วินาที
                        throttleSeconds = 2.0;
                    else                   // หยุดนิ่ง (< 5 km/h): Broadcast ทุกๆ 5 วินาที
                        throttleSeconds = 5.0;
                }
            }

            var lastTicksValue = lastBroadcast.Length > 0 
                ? (long)lastBroadcast.FirstOrDefault(e => e.Name == "ticks").Value 
                : 0;
            var secondsSinceLast = (now - new DateTime(lastTicksValue, DateTimeKind.Utc)).TotalSeconds;

            if (secondsSinceLast >= throttleSeconds)
            {
                // ดึงสถานะไรเดอร์ปัจจุบัน (จาก DB หรือ cache)
                var riderState = "IDLE";
                var rider = await _dbContext.Riders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == riderId);
                if (rider is not null)
                {
                    riderState = rider.State.ToString();
                }

                // A. ส่งพิกัดเรียลไทม์หา Admin Dashboard
                await _hubContext.Clients.Group(AdminGroup).SendAsync("RiderLocationUpdated", new
                {
                    RiderId = riderId,
                    Lat = snappedLat,
                    Lng = snappedLng,
                    Status = riderState,
                    Timestamp = now
                });

                // B. ค้นหาออเดอร์ที่ค้างอยู่ของไรเดอร์คนนี้เพื่อส่งพิกัดหาแอปลูกค้า
                var activeOrder = await _dbContext.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.AssignedRiderId == riderId && 
                        (o.State == OrderState.ASSIGNED || o.State == OrderState.PICKING_UP || o.State == OrderState.DELIVERING));

                if (activeOrder is not null && !string.IsNullOrEmpty(activeOrder.CustomerId))
                {
                    await _hubContext.Clients.Group($"customer:{activeOrder.CustomerId}").SendAsync("RiderLocationUpdated", new
                    {
                        RiderId = riderId,
                        Lat = snappedLat,
                        Lng = snappedLng,
                        Status = riderState,
                        Timestamp = now
                    });
                }

                // อัปเดตพิกัดส่งออกล่าสุดลงใน Redis
                await db.HashSetAsync(lastBroadcastKey, new[]
                {
                    new HashEntry("lat", snappedLat),
                    new HashEntry("lng", snappedLng),
                    new HashEntry("ticks", now.Ticks)
                });
            }

            // 8. ปรับปรุงฐานข้อมูลหลัก (PostgreSQL) แบบ Throttled (ทุก 10 วินาที)
            // เพื่อคงความพร้อมทำงานกับระบบอื่น แต่ลด I/O ของ Database ลงมากกว่า 90%
            var dbThrottleKey = $"telemetry:db_write_throttle:{riderId}";
            var shouldWriteToDb = await db.StringSetAsync(dbThrottleKey, "locked", TimeSpan.FromSeconds(10), When.NotExists);

            if (shouldWriteToDb)
            {
                var riderEntity = await _dbContext.Riders.FindAsync(riderId);
                if (riderEntity is not null)
                {
                    riderEntity.CurrentLocation = new NetTopologySuite.Geometries.Point(snappedLng, snappedLat) { SRID = 4326 };
                    riderEntity.LastGpsUpdate = now;
                    await _dbContext.SaveChangesAsync();
                }
            }
        }

        private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var r = 6371e3; // รัศมีโลกในหน่วยเมตร
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
}
