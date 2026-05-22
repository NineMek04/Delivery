using System;
using System.Threading.Tasks;
using BackendApi.Core.StateMachines;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Hubs;

public partial class TrackingHub
{
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

        // เพิ่มการนับ GPS ticks สำหรับคำนวณ Telemetry Throughput Rate
        _aggregator.IncrementGpsTick();

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

    /// <summary>
    /// เมธอดสำหรับ Flutter Client ที่ไม่ส่งพารามิเตอร์ accuracy
    /// </summary>
    public async Task UpdateRiderLocation(double lat, double lng)
    {
        await UpdateLocation(lat, lng, 10.0);
    }
}
