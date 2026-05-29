using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Core.Models;
using BackendApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using BackendApi.Security;

namespace BackendApi.Controllers.Business
{
    /// <summary>
    /// API สำหรับดึงพิกัดล่าสุดของไรเดอร์ทุกคนจาก Redis (Redis-first) เพื่อแก้ปัญหา F5 Refresh แล้วพิกัดกระโดด
    /// </summary>
    [Authorize(Policy = AuthConstants.OperationsPolicy)]
    [Route("api/v1/rider-locations")]
    public class RiderLocationController : DeliveryControllerBase
    {
        private readonly IConnectionMultiplexer _redis;

        public RiderLocationController(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        /// <summary>
        /// ดึงพิกัดล่าสุดและสถานะจริงของไรเดอร์ทุกคนจาก Redis Operational Cache ด้วย Batching/Pipelining
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<RiderLocationDto>>>> GetRiderLocations()
        {
            var db = _redis.GetDatabase();
            var endpoints = _redis.GetEndPoints();
            var server = _redis.GetServer(endpoints.First());

            // 1. SCAN หา keys ทั้งหมดที่ match riders:gps:*
            var keys = new List<RedisKey>();
            foreach (var key in server.Keys(pattern: "riders:gps:*"))
            {
                keys.Add(key);
            }

            if (keys.Count == 0)
            {
                return Ok(ApiResponse<List<RiderLocationDto>>.Ok(new List<RiderLocationDto>()));
            }

            // 2. ดึงข้อมูลพิกัดจาก Redis Hash ด้วย Pipeline/Batch เพื่อหลีกเลี่ยง N+1 roundtrips
            var batch = db.CreateBatch();
            var tasks = keys.Select(k => new
            {
                Key = k,
                Task = batch.HashGetAllAsync(k)
            }).ToList();

            batch.Execute();
            
            // รอให้ทุก Task ใน batch รันเสร็จ
            var results = await Task.WhenAll(tasks.Select(t => t.Task));

            var locations = new List<RiderLocationDto>();

            // 3. ดึงสถานะ Rider จาก riders:status:* เพิ่มเติมเพื่อให้หน้าบ้านได้สถานะล่าสุด
            var statusBatch = db.CreateBatch();
            var statusTasks = keys.Select(k =>
            {
                var riderId = k.ToString().Substring("riders:gps:".Length);
                var statusKey = $"riders:status:{riderId}";
                return new
                {
                    RiderId = riderId,
                    Task = statusBatch.StringGetAsync(statusKey)
                };
            }).ToList();

            statusBatch.Execute();
            await Task.WhenAll(statusTasks.Select(t => t.Task));

            // ดึงข้อมูล Rider จากฐานข้อมูลเป็น fallback เผื่อสถานะไม่อยู่ใน Redis หรือดึงชื่อไรเดอร์
            var riderEntities = await DbContext.Riders
                .AsNoTracking()
                .Select(r => new { r.Id, r.Name, r.State })
                .ToDictionaryAsync(r => r.Id);

            for (int i = 0; i < keys.Count; i++)
            {
                var keyStr = keys[i].ToString();
                var riderId = keyStr.Substring("riders:gps:".Length);
                var hashEntries = results[i];

                if (hashEntries.Length == 0) continue;

                var latVal = hashEntries.FirstOrDefault(e => e.Name == "lat").Value;
                var lngVal = hashEntries.FirstOrDefault(e => e.Name == "lng").Value;
                var ticksVal = hashEntries.FirstOrDefault(e => e.Name == "updated_at").Value;
                var speedVal = hashEntries.FirstOrDefault(e => e.Name == "speed_kmh").Value;

                double lat = latVal.HasValue ? (double)latVal : 0.0;
                double lng = lngVal.HasValue ? (double)lngVal : 0.0;
                long ticks = ticksVal.HasValue ? (long)ticksVal : 0;
                double speed = speedVal.HasValue ? (double)speedVal : 0.0;

                // 4. ดึงพิกัด snap เผื่อมี
                var snappedKey = $"riders:snapped_gps:{riderId}";
                var snappedEntries = await db.HashGetAllAsync(snappedKey);
                double snappedLat = lat;
                double snappedLng = lng;
                bool isSnapped = false;

                if (snappedEntries.Length > 0)
                {
                    var sLatVal = snappedEntries.FirstOrDefault(e => e.Name == "lat").Value;
                    var sLngVal = snappedEntries.FirstOrDefault(e => e.Name == "lng").Value;
                    if (sLatVal.HasValue && sLngVal.HasValue)
                    {
                        snappedLat = (double)sLatVal;
                        snappedLng = (double)sLngVal;
                        isSnapped = true;
                    }
                }

                var statusRedis = statusTasks[i].Task.Result;
                string status = "OFFLINE";
                string name = "Unknown Rider";

                if (riderEntities.TryGetValue(riderId, out var riderInfo))
                {
                    name = riderInfo.Name;
                    status = riderInfo.State.ToString();
                }

                if (statusRedis.HasValue)
                {
                    status = statusRedis.ToString();
                }

                locations.Add(new RiderLocationDto
                {
                    RiderId = riderId,
                    Name = name,
                    Lat = lat,
                    Lng = lng,
                    SnappedLat = snappedLat,
                    SnappedLng = snappedLng,
                    IsSnapped = isSnapped,
                    SpeedKmh = speed,
                    Status = status,
                    UpdatedAt = ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.UtcNow
                });
            }

            return Ok(ApiResponse<List<RiderLocationDto>>.Ok(locations));
        }
    }

    /// <summary>
    /// DTO สำหรับส่งข้อมูลพิกัดและสถานะไรเดอร์จริงจาก Redis
    /// </summary>
    public class RiderLocationDto
    {
        public string RiderId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Lat { get; set; }
        public double Lng { get; set; }
        public double SnappedLat { get; set; }
        public double SnappedLng { get; set; }
        public bool IsSnapped { get; set; }
        public double SpeedKmh { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }
}
