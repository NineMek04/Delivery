using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BackendApi.Data;
using BackendApi.Core.StateMachines;
using BackendApi.Services.Dispatch;
using BackendApi.Infrastructure.Redis;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Infrastructure.EventBus.Events;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace BackendApi.Services.Tracking
{
    public class RiderPresenceManager : IRiderPresenceManager
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly RiderPresenceService _presenceService;
        private readonly StateMachineService _stateMachine;
        private readonly IEventBus _eventBus;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<RiderPresenceManager> _logger;
        private readonly IServiceProvider _serviceProvider;

        public RiderPresenceManager(
            ApplicationDbContext dbContext,
            RiderPresenceService presenceService,
            StateMachineService stateMachine,
            IEventBus eventBus,
            IHttpContextAccessor httpContextAccessor,
            ILogger<RiderPresenceManager> logger,
            IServiceProvider serviceProvider)
        {
            _dbContext = dbContext;
            _presenceService = presenceService;
            _stateMachine = stateMachine;
            _eventBus = eventBus;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task<RiderConnectionResult?> HandleRiderConnectAsync(string userId)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.RiderId == null) return null;

            await _presenceService.UpdateHeartbeatAsync(user.RiderId);

            var rider = await _dbContext.Riders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == user.RiderId);
            if (rider == null) return null;

            var oldState = rider.State;
            
            // Publish integration event to RabbitMQ for durable out-of-process state transition
            var riderId = rider.Id;
            var correlationId = BackendApi.Security.CorrelationIdProvider.GetOrCreate(_httpContextAccessor);

            await _eventBus.PublishAsync(new RiderStateChangedIntegrationEvent(
                riderId,
                targetState:  null,   // handler resolves IDLE vs BUSY from active orders
                previousState: oldState,
                reason: (rider.State == RiderState.OFFLINE)
                    ? RiderTransitionReason.Connect
                    : RiderTransitionReason.Recover,
                correlationId: correlationId
            ));

            return new RiderConnectionResult(user.RiderId, rider.State, oldState);
        }

        public async Task<RiderConnectionResult?> HandleRiderConnectionDisconnectAsync(string userId)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.RiderId == null) return null;

            var rider = await _dbContext.Riders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == user.RiderId);
            if (rider == null || rider.State == RiderState.OFFLINE) return null;

            var oldState = rider.State;
            var riderId = rider.Id;

            // Publish integration event to RabbitMQ for durable out-of-process state transition
            var correlationId = BackendApi.Security.CorrelationIdProvider.GetOrCreate(_httpContextAccessor);

            await _eventBus.PublishAsync(new RiderStateChangedIntegrationEvent(
                riderId,
                targetState:  RiderState.STALE,
                previousState: oldState,
                reason: RiderTransitionReason.Disconnect,
                correlationId: correlationId
            ));

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

        public async Task<RiderLastLocation?> GetLastKnownLocationForRiderAsync(string riderId)
        {
            var loc = await _presenceService.GetLastKnownLocationAsync(riderId);
            if (loc is null) return null;
            return new RiderLastLocation(loc.Value.Lat, loc.Value.Lng, loc.Value.UpdatedAt);
        }
    }
}
