using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BackendApi.Data;
using BackendApi.Core.StateMachines;
using BackendApi.Services.Dispatch;
using BackendApi.Infrastructure.Redis;

namespace BackendApi.Services.Tracking
{
    public class RiderPresenceManager : IRiderPresenceManager
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly RiderPresenceService _presenceService;
        private readonly StateMachineService _stateMachine;
        private readonly ILogger<RiderPresenceManager> _logger;

        public RiderPresenceManager(
            ApplicationDbContext dbContext,
            RiderPresenceService presenceService,
            StateMachineService stateMachine,
            ILogger<RiderPresenceManager> logger)
        {
            _dbContext = dbContext;
            _presenceService = presenceService;
            _stateMachine = stateMachine;
            _logger = logger;
        }

        public async Task<RiderConnectionResult?> HandleRiderConnectAsync(string userId)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.RiderId == null) return null;

            await _presenceService.UpdateHeartbeatAsync(user.RiderId);

            var rider = await _dbContext.Riders.FindAsync(user.RiderId);
            if (rider == null) return null;

            var oldState = rider.State;
            if (rider.State == RiderState.OFFLINE)
            {
                await _stateMachine.TransitionRiderAsync(rider, RiderState.IDLE);
            }
            else if (rider.State == RiderState.STALE)
            {
                var newState = await HasActiveJobAsync(rider.Id) ? RiderState.BUSY : RiderState.IDLE;
                await _stateMachine.TransitionRiderAsync(rider, newState);
            }

            return new RiderConnectionResult(user.RiderId, rider.State, oldState);
        }

        public async Task<RiderConnectionResult?> HandleRiderConnectionDisconnectAsync(string userId)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.RiderId == null) return null;

            var rider = await _dbContext.Riders.FindAsync(user.RiderId);
            if (rider == null || rider.State == RiderState.OFFLINE) return null;

            var oldState = rider.State;
            await _stateMachine.TransitionRiderAsync(rider, RiderState.STALE);

            return new RiderConnectionResult(user.RiderId, rider.State, oldState);
        }

        public async Task<RiderState?> HandleRiderHeartbeatAsync(string riderId)
        {
            await _presenceService.UpdateHeartbeatAsync(riderId);

            var rider = await _dbContext.Riders.FindAsync(riderId);
            if (rider != null && rider.State == RiderState.STALE)
            {
                var newState = await HasActiveJobAsync(riderId) ? RiderState.BUSY : RiderState.IDLE;
                await _stateMachine.TransitionRiderAsync(rider, newState);
            }

            return rider?.State;
        }

        public async Task<RiderStatusUpdateResult> HandleRiderStatusUpdateAsync(string riderId, string status)
        {
            var cleaned = status?.Trim() ?? "";
            RiderState targetState;
            
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
                return new RiderStatusUpdateResult(false, $"Unknown status: {status}");
            }

            var rider = await _dbContext.Riders.FindAsync(riderId);
            if (rider == null)
            {
                _logger.LogWarning("Rider {RiderId} not found in database.", riderId);
                return new RiderStatusUpdateResult(false, "Rider not found");
            }

            if (rider.State == targetState)
            {
                _logger.LogDebug("Rider {RiderId} already in state {State} — no-op", riderId, targetState);
                return new RiderStatusUpdateResult(true, State: targetState);
            }

            var oldState = rider.State;
            var success = await _stateMachine.TransitionRiderAsync(rider, targetState);

            if (success)
            {
                if (targetState == RiderState.OFFLINE)
                {
                    await _presenceService.RemoveRiderAsync(riderId);
                }
                else
                {
                    await _presenceService.UpdateHeartbeatAsync(riderId);
                }

                return new RiderStatusUpdateResult(true, State: targetState, PreviousState: oldState);
            }

            return new RiderStatusUpdateResult(false, $"Illegal status transition from {oldState} to {targetState}");
        }

        public async Task<string?> GetRiderIdByUserIdAsync(string userId)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            return user?.RiderId;
        }

        public async Task<bool> HasActiveJobAsync(string riderId)
        {
            return await _dbContext.Orders.AnyAsync(o => 
                o.AssignedRiderId == riderId && 
                (o.State == OrderState.ASSIGNED || o.State == OrderState.PICKING_UP || o.State == OrderState.DELIVERING));
        }
    }
}
