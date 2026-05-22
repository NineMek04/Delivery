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

        var cleaned = status?.Trim() ?? "";
        RiderState targetState;
        
        // Defensive String Parsing: รองรับ AVAILABLE/IDLE, OFFLINE, DELIVERING/BUSY
        if (string.Equals(cleaned, "AVAILABLE", StringComparison.OrdinalIgnoreCase))
        {
            targetState = RiderState.IDLE;
        }
        else if (string.Equals(cleaned, "DELIVERING", StringComparison.OrdinalIgnoreCase))
        {
            targetState = RiderState.BUSY;
        }
        else if (Enum.TryParse<RiderState>(cleaned, true, out var parsed))
        {
            targetState = parsed;
        }
        else
        {
            _logger.LogWarning("Unknown status requested for Rider {RiderId}: {Status}", riderId, status);
            await Clients.Caller.SendAsync("RiderStatusUpdatedResult", new { Success = false, Message = $"Unknown status: {status}" });
            return false;
        }

        var rider = await _dbContext.Riders.FindAsync(riderId);
        if (rider is null)
        {
            _logger.LogWarning("Rider {RiderId} not found in database.", riderId);
            return false;
        }

        var oldState = rider.State;
        var success = await _stateMachine.TransitionRiderAsync(rider, targetState);

        if (success)
        {
            // ซิงค์ Redis Presence Cache
            if (targetState == RiderState.OFFLINE)
            {
                await _presenceService.RemoveRiderAsync(riderId);
            }
            else
            {
                await _presenceService.UpdateHeartbeatAsync(riderId);
            }

            // Broadcast ไปยังกลุ่ม Admin (Telemetry Sync)
            await Clients.Group(AdminGroup).SendAsync("RiderStatusUpdated", new
            {
                RiderId = riderId,
                NewStatus = targetState.ToString(),
                PreviousStatus = oldState.ToString(),
                Reason = "explicit_update",
                Timestamp = DateTime.UtcNow
            });

            // ส่งผลลัพธ์กลับไปยัง Caller
            await Clients.Caller.SendAsync("RiderStatusUpdatedResult", new { Success = true, Status = targetState.ToString() });
            return true;
        }
        
        _logger.LogWarning("Illegal state transition requested for Rider {RiderId} from {From} to {To}", riderId, oldState, targetState);
        await Clients.Caller.SendAsync("RiderStatusUpdatedResult", new { Success = false, Message = $"Illegal status transition from {oldState} to {targetState}" });
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
