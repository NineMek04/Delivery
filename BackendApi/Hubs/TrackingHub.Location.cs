using System;
using System.Threading.Tasks;
using BackendApi.Core.StateMachines;
using BackendApi.Services.Telemetry;
using Microsoft.AspNetCore.Mvc;
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

    public async Task UpdateLocation(double lat, double lng, double accuracy, [FromServices] TelemetryService telemetryService)
    {
        var riderId = await GetRiderIdAsync();
        if (riderId is null) return;

        await telemetryService.ProcessLocationUpdateAsync(riderId, lat, lng, accuracy);
    }

    /// <summary>
    /// เมธอดสำหรับ Flutter Client ที่ไม่ส่งพารามิเตอร์ accuracy
    /// </summary>
    public async Task UpdateRiderLocation(double lat, double lng, [FromServices] TelemetryService telemetryService)
    {
        await UpdateLocation(lat, lng, 10.0, telemetryService);
    }
}
