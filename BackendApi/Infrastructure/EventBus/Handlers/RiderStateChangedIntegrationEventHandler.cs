using System;
using System.Threading.Tasks;
using BackendApi.Data;
using BackendApi.Core.StateMachines;
using BackendApi.Infrastructure.EventBus.Events;
using BackendApi.Services.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using BackendApi.Infrastructure.Redis;
using BackendApi.Models;

namespace BackendApi.Infrastructure.EventBus.Handlers
{
    /// <summary>
    /// Handles durable, out-of-process Rider state transitions via RabbitMQ.
    ///
    /// [MAGIC-STRING FIX] Previously used string literals ("IDLE", "RECOVER", "STALE")
    /// to route logic. A single typo in any publisher would silently ACK the message
    /// without performing any state change. Now uses strongly-typed RiderTransitionReason
    /// and RiderState? enums — compiler-verified, refactor-safe, log-safe.
    /// </summary>
    public class RiderStateChangedIntegrationEventHandler
        : IIntegrationEventHandler<RiderStateChangedIntegrationEvent>
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly StateMachineService _stateMachine;
        private readonly RiderPresenceService _presenceService;
        private readonly IHubContext<BackendApi.Hubs.TrackingHub> _hubContext;
        private readonly ILogger<RiderStateChangedIntegrationEventHandler> _logger;

        public RiderStateChangedIntegrationEventHandler(
            ApplicationDbContext dbContext,
            StateMachineService stateMachine,
            RiderPresenceService presenceService,
            IHubContext<BackendApi.Hubs.TrackingHub> hubContext,
            ILogger<RiderStateChangedIntegrationEventHandler> logger)
        {
            _dbContext = dbContext;
            _stateMachine = stateMachine;
            _presenceService = presenceService;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task Handle(RiderStateChangedIntegrationEvent @event)
        {
            _logger.LogInformation(
                "Handling Rider state change: Rider {RiderId} | {PreviousState} → Reason={Reason} TargetState={TargetState}",
                @event.RiderId,
                @event.PreviousState?.ToString() ?? "Unknown",
                @event.Reason,
                @event.TargetState?.ToString() ?? "auto");

            try
            {
                var rider = await _dbContext.Riders.FindAsync(@event.RiderId);
                if (rider == null)
                {
                    _logger.LogWarning("Rider {RiderId} not found. State change skipped.", @event.RiderId);
                    return;
                }

                switch (@event.Reason)
                {
                    case RiderTransitionReason.Connect:
                        // Rider app came online from OFFLINE
                        if (rider.State == RiderState.OFFLINE)
                        {
                            var oldState = rider.State;
                            var hasActiveJob = await HasActiveJobAsync(@event.RiderId);
                            var restored = await _stateMachine.TransitionRiderAsync(
                                rider,
                                RiderState.IDLE);

                            if (restored && hasActiveJob)
                            {
                                restored =
                                    await _stateMachine.TransitionRiderAsync(
                                        rider,
                                        RiderState.RESERVED) &&
                                    await _stateMachine.TransitionRiderAsync(
                                        rider,
                                        RiderState.BUSY);
                            }

                            if (restored)
                            {
                                await BroadcastRiderStateChangeAsync(rider, oldState, "connect");
                            }
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Connect ignored: Rider {RiderId} is {State} (expected OFFLINE).",
                                rider.Id, rider.State);
                        }
                        break;

                    case RiderTransitionReason.Recover:
                        // Rider reconnected after a STALE window
                        if (rider.State == RiderState.STALE)
                        {
                            var newState = await HasActiveJobAsync(@event.RiderId)
                                ? RiderState.BUSY
                                : RiderState.IDLE;
                            var oldState = rider.State;
                            if (await _stateMachine.TransitionRiderAsync(rider, newState))
                            {
                                await BroadcastRiderStateChangeAsync(rider, oldState, "recover");
                            }
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Recover ignored: Rider {RiderId} is {State} (expected STALE).",
                                rider.Id, rider.State);
                        }
                        break;

                    case RiderTransitionReason.Disconnect:
                        // SignalR connection dropped — move to STALE unless already OFFLINE
                        if (rider.State != RiderState.OFFLINE)
                        {
                            var oldState = rider.State;
                            if (await _stateMachine.TransitionRiderAsync(rider, RiderState.STALE))
                            {
                                await BroadcastRiderStateChangeAsync(rider, oldState, "disconnect");
                            }
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Disconnect ignored: Rider {RiderId} is already OFFLINE.",
                                rider.Id);
                        }
                        break;

                    case RiderTransitionReason.HeartbeatTimeout:
                        // Heartbeat monitor escalated STALE → OFFLINE
                        if (rider.State == RiderState.STALE)
                        {
                            await _stateMachine.TransitionRiderAsync(rider, RiderState.OFFLINE);
                        }
                        else
                        {
                            _logger.LogDebug(
                                "HeartbeatTimeout ignored: Rider {RiderId} is {State} (expected STALE).",
                                rider.Id, rider.State);
                        }
                        break;

                    default:
                        // This branch is unreachable with a complete enum — kept as a compile-time
                        // safety net in case a new enum member is added without updating the handler.
                        _logger.LogWarning(
                            "Unhandled RiderTransitionReason={Reason} for Rider {RiderId}.",
                            @event.Reason, @event.RiderId);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to execute state transition for Rider {RiderId} (Reason={Reason})",
                    @event.RiderId, @event.Reason);
                throw; // Re-throw to trigger RabbitMQ retry → DLQ
            }
        }

        private async Task BroadcastRiderStateChangeAsync(Rider rider, RiderState oldState, string reason)
        {
            try
            {
                // 1. Broadcast new status to admin dashboard
                await _hubContext.Clients.Group("admins").SendAsync("RiderStatusUpdated", new
                {
                    RiderId = rider.Id,
                    NewStatus = rider.State.ToString(),
                    PreviousStatus = oldState.ToString(),
                    Reason = reason,
                    Timestamp = DateTime.UtcNow
                });

                // 2. Query and broadcast last known coordinates if present
                var loc = await _presenceService.GetLastKnownLocationAsync(rider.Id);
                if (loc != null)
                {
                    await _hubContext.Clients.Group("admins").SendAsync("RiderLocationUpdated", new
                    {
                        RiderId = rider.Id,
                        Lat = loc.Value.Lat,
                        Lng = loc.Value.Lng,
                        Status = rider.State.ToString(),
                        Timestamp = loc.Value.UpdatedAt,
                        isSnapped = false
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast RiderStatusUpdated/LocationUpdated SignalR event for Rider {RiderId}", rider.Id);
            }
        }

        private async Task<bool> HasActiveJobAsync(string riderId) =>
            await _dbContext.Orders.AnyAsync(o =>
                o.AssignedRiderId == riderId &&
                (o.State == OrderState.ASSIGNED ||
                 o.State == OrderState.PICKING_UP ||
                 o.State == OrderState.DELIVERING));
    }
}
