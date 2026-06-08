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

        await _presenceManager.HandleRiderHeartbeatAsync(riderId);
    }

    public async Task UpdateLocation(double lat, double lng, double accuracy)
    {
        var riderId = await GetRiderIdAsync();
        if (riderId is null) return;

        await _telemetryService.ProcessLocationUpdateAsync(riderId, lat, lng, accuracy);
    }

    /// <summary>
    /// เมธอดสำหรับ Flutter Client ที่ไม่ส่งพารามิเตอร์ accuracy
    /// </summary>
    public async Task UpdateRiderLocation(double lat, double lng)
    {
        await UpdateLocation(lat, lng, 10.0);
    }
}
