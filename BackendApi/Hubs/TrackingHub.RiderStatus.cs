using System;
using System.Threading.Tasks;
using BackendApi.Core.StateMachines;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Hubs;

public partial class TrackingHub
{
    /// <summary>
    /// เมธอดสำหรับอัปเดตสถานะของ Rider โดยรับชื่อสถานะเป็น string
    /// </summary>
    public async Task<bool> UpdateStatus(string status)
    {
        var riderId = await GetRiderIdAsync();
        if (riderId is null)
        {
            _logger.LogWarning("Unauthorized user tried to update rider status.");
            return false;
        }

        var result = await _presenceManager.HandleRiderStatusUpdateAsync(riderId, status);
        if (result.Success && result.State is not null)
        {
            if (result.PreviousState is not null)
            {
                // Broadcast ไปยังกลุ่ม Admin (Telemetry Sync)
                await Clients.Group(AdminGroup).SendAsync("RiderStatusUpdated", new
                {
                    RiderId = riderId,
                    NewStatus = result.State.ToString(),
                    PreviousStatus = result.PreviousState.ToString(),
                    Reason = "explicit_update",
                    Timestamp = DateTime.UtcNow
                });
            }

            // ส่งผลลัพธ์กลับไปยัง Caller
            await Clients.Caller.SendAsync("RiderStatusUpdatedResult", new { Success = true, Status = result.State.ToString() });
            return true;
        }

        await Clients.Caller.SendAsync("RiderStatusUpdatedResult", new { Success = false, Message = result.Message ?? "Illegal status transition" });
        return false;
    }

    /// <summary>
    /// เมธอดสำรองเพื่อรองรับการเรียกใน Flutter Rider App
    /// </summary>
    public async Task<bool> UpdateRiderStatus(string status)
    {
        return await UpdateStatus(status);
    }
}
