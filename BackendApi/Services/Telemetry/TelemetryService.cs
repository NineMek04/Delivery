using System;
using System.Collections.Generic;
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
using BackendApi.Features.FleetTracking.Telemetry;

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
        private readonly GpsRedisRateLimiter _rateLimiter;
        private readonly GpsRabbitMqPublisher _gpsPublisher;
        private readonly TelemetryAggregator _aggregator;
        private readonly OsrmRoutingService _routingService;
        private readonly IHubContext<TrackingHub> _hubContext;
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<TelemetryService> _logger;

        private const string AdminGroup = "admins";

        public TelemetryService(
            ApplicationDbContext dbContext,
            RiderPresenceService presenceService,
            GpsRedisRateLimiter rateLimiter,
            GpsRabbitMqPublisher gpsPublisher,
            TelemetryAggregator aggregator,
            OsrmRoutingService routingService,
            IHubContext<TrackingHub> hubContext,
            IConnectionMultiplexer redis,
            ILogger<TelemetryService> logger)
        {
            _dbContext = dbContext;
            _presenceService = presenceService;
            _rateLimiter = rateLimiter;
            _gpsPublisher = gpsPublisher;
            _aggregator = aggregator;
            _routingService = routingService;
            _hubContext = hubContext;
            _redis = redis;
            _logger = logger;
        }

        /// <summary>
        /// ประมวลผลและกระจายพิกัด GPS เรียลไทม์
        /// </summary>
        public async Task ProcessLocationUpdateAsync(
            string riderId, 
            double lat, 
            double lng, 
            double accuracy, 
            DateTime? timestamp = null, 
            bool bypassRateLimit = false)
        {
            if (lat < -90 || lat > 90 || lng < -180 || lng > 180) return;

            // 1. กรองความคลาดเคลื่อนเบื้องต้น (Drift Protection)
            if (accuracy > 50) return;

            // 1.5. Level 1 Server-Side Rate Limiting (Safety net for SignalR or unthrottled REST inputs)
            var currentQueueSize = _gpsPublisher.PendingQueueCount;
            if (!bypassRateLimit)
            {
                if (await _rateLimiter.ShouldRateLimitAsync(riderId, currentQueueSize))
                {
                    int recommendedInterval = _rateLimiter.GetRecommendedInterval(currentQueueSize);
                    await _hubContext.Clients.Group($"rider:{riderId}").SendAsync("AdjustPingRate", new
                    {
                        IntervalSeconds = recommendedInterval,
                        Timestamp = DateTime.UtcNow
                    });
                    return;
                }
            }

            var now = timestamp ?? DateTime.UtcNow;

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

            // 4. คำนวณความเร็วจาก GPS point ก่อนหน้า (reuse lastGps จากด้านบน)
            double speedKmh = 0.0;
            if (lastGps is not null)
            {
                var distForSpeed = HaversineDistance(lastGps.Value.Lat, lastGps.Value.Lng, snappedLat, snappedLng);
                var timeDiffForSpeed = (now - lastGps.Value.UpdatedAt).TotalSeconds;
                if (timeDiffForSpeed > 0)
                    speedKmh = (distForSpeed / timeDiffForSpeed) * 3.6; // m/s → km/h
            }

            // 5. บันทึกพิกัดเรียลไทม์ + ความเร็วลงเฉพาะ Redis Presence Cache
            await _presenceService.UpdateGpsAsync(riderId, snappedLat, snappedLng, speedKmh);

            // 6. โยนพิกัดลงคิว RabbitMQ แบบ Durable ป้องกันข้อมูลสูญหายระดับองค์กร
            _gpsPublisher.Publish(new TrackPoint(riderId, snappedLat, snappedLng, now));

            // 7. เพิ่มตัวนับ GPS Tick สำหรับแสดงอัตราผ่านทางหน้าหลังบ้าน
            _aggregator.IncrementGpsTick();

            // 8. จัดการ Dynamic Throttling สำหรับ Broadcast ผ่าน SignalR
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
                // ดึงสถานะไรเดอร์ปัจจุบัน (จาก Redis Cache ก่อน เลี่ยง DB)
                var riderState = "IDLE";
                var statusCacheKey = $"riders:status:{riderId}";
                var cachedState = await db.StringGetAsync(statusCacheKey);
                if (cachedState.HasValue)
                {
                    riderState = cachedState.ToString();
                }
                else
                {
                    var rider = await _dbContext.Riders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == riderId);
                    if (rider is not null)
                    {
                        riderState = rider.State.ToString();
                        await db.StringSetAsync(statusCacheKey, riderState, TimeSpan.FromMinutes(5));
                    }
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

                // B. ค้นหาออเดอร์ที่ค้างอยู่ของไรเดอร์คนนี้เพื่อส่งพิกัดหาแอปลูกค้า (ผ่าน Redis Cache เลี่ยง DB)
                string? customerId = null;
                var activeOrderKey = $"riders:active_order:{riderId}";
                var cachedOrder = await db.HashGetAllAsync(activeOrderKey);

                if (cachedOrder.Length > 0)
                {
                    customerId = cachedOrder.FirstOrDefault(e => e.Name == "customer_id").Value;
                }
                else
                {
                    var activeOrder = await _dbContext.Orders
                        .AsNoTracking()
                        .FirstOrDefaultAsync(o => o.AssignedRiderId == riderId && 
                            (o.State == OrderState.ASSIGNED || o.State == OrderState.PICKING_UP || o.State == OrderState.DELIVERING));

                    if (activeOrder is not null)
                    {
                        customerId = activeOrder.CustomerId;
                        await db.HashSetAsync(activeOrderKey, new[]
                        {
                            new HashEntry("order_id", activeOrder.Id),
                            new HashEntry("customer_id", customerId ?? string.Empty)
                        });
                        await db.KeyExpireAsync(activeOrderKey, TimeSpan.FromHours(24));
                    }
                }

                if (!string.IsNullOrEmpty(customerId))
                {
                    await _hubContext.Clients.Group($"customer:{customerId}").SendAsync("RiderLocationUpdated", new
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

                // Adaptive Client Rate: สั่งแอปไรเดอร์ปรับความถี่ยิง GPS ตาม Backpressure ของ RabbitMQ Queue
                int recommendedIntervalSeconds = _rateLimiter.GetRecommendedInterval(currentQueueSize);

                await _hubContext.Clients.Group($"rider:{riderId}").SendAsync("AdjustPingRate", new
                {
                    IntervalSeconds = recommendedIntervalSeconds,
                    Timestamp = DateTime.UtcNow
                });
            }

            // 9. ปรับปรุงฐานข้อมูลหลัก (PostgreSQL) แบบ Throttled (ทุก 10 วินาที)
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

        /// <summary>
        /// ประมวลผลและกระจายพิกัด GPS เป็นกลุ่ม (Batch) จาก Offline Buffering
        /// </summary>
        public async Task ProcessLocationBatchAsync(string riderId, List<GpsBatchPointRequest> batchPoints)
        {
            if (batchPoints == null || batchPoints.Count == 0) return;

            // 1. กรองจุดที่คลาดเคลื่อนเบื้องต้น (Drift Protection) และขอบเขตพิกัด
            var validPoints = batchPoints
                .Where(p => p.Latitude >= -90 && p.Latitude <= 90 && p.Longitude >= -180 && p.Longitude <= 180 && p.Accuracy <= 50)
                .OrderBy(p => p.Timestamp) // เรียงลำดับตามเวลาแบบเรียงขึ้น (Ascending)
                .ToList();

            if (validPoints.Count == 0) return;

            // 2. แยกจุดล่าสุด (Latest Point) ออกจากพิกัดย้อนหลัง (Historical Points)
            var latestPoint = validPoints.Last();
            var historicalPoints = validPoints.Take(validPoints.Count - 1).ToList();

            // 3. จัดการข้อมูลย้อนหลัง (Historical Points): ส่งตรงเข้า RabbitMQ เป็นข้อมูลดิบ (Raw Lat/Lng) เพื่อประหยัดทรัพยากร
            foreach (var point in historicalPoints)
            {
                _gpsPublisher.Publish(new TrackPoint(riderId, point.Latitude, point.Longitude, point.Timestamp));
            }

            // 4. จัดการจุดล่าสุด (Latest Point): ประมวลผลลอจิกเต็มรูปแบบใน Hot Path (ผ่าน OSRM, Redis Presence, SignalR, DB Throttle)
            // ทำการ bypassRateLimit = true เนื่องจากผ่านการเช็คระดับ Batch-Level มาแล้วจาก Controller
            await ProcessLocationUpdateAsync(
                riderId, 
                latestPoint.Latitude, 
                latestPoint.Longitude, 
                latestPoint.Accuracy, 
                latestPoint.Timestamp, 
                bypassRateLimit: true);
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
