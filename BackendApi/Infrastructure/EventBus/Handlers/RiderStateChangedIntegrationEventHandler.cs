using System;
using System.Threading.Tasks;
using BackendApi.Data;
using BackendApi.Core.StateMachines;
using BackendApi.Infrastructure.EventBus.Events;
using BackendApi.Services.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
        private readonly ILogger<RiderStateChangedIntegrationEventHandler> _logger;

        public RiderStateChangedIntegrationEventHandler(
            ApplicationDbContext dbContext,
            StateMachineService stateMachine,
            ILogger<RiderStateChangedIntegrationEventHandler> logger)
        {
            _dbContext = dbContext;
            _stateMachine = stateMachine;
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
                            var newState = await HasActiveJobAsync(@event.RiderId)
                                ? RiderState.BUSY
                                : RiderState.IDLE;
                            await _stateMachine.TransitionRiderAsync(rider, newState);
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
                            await _stateMachine.TransitionRiderAsync(rider, newState);
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
                            await _stateMachine.TransitionRiderAsync(rider, RiderState.STALE);
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

        private async Task<bool> HasActiveJobAsync(string riderId) =>
            await _dbContext.Orders.AnyAsync(o =>
                o.AssignedRiderId == riderId &&
                (o.State == OrderState.ASSIGNED ||
                 o.State == OrderState.PICKING_UP ||
                 o.State == OrderState.DELIVERING));
    }
}
