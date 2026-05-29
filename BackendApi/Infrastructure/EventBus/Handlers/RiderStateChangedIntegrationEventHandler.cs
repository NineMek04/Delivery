using System;
using System.Linq;
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
    /// Handler to durably process Rider state transitions out of process using RabbitMQ.
    /// Eliminates thread-unsafe in-RAM Task.Run calls and guarantees state safety.
    /// </summary>
    public class RiderStateChangedIntegrationEventHandler : IIntegrationEventHandler<RiderStateChangedIntegrationEvent>
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
                "Handling Rider state change integration event: Rider {RiderId} from {PreviousState} to target state {TargetState} (Reason: {Reason})",
                @event.RiderId,
                @event.PreviousState ?? "Unknown",
                @event.TargetState,
                @event.Reason
            );

            try
            {
                var rider = await _dbContext.Riders.FindAsync(@event.RiderId);
                if (rider == null)
                {
                    _logger.LogWarning("Rider {RiderId} not found in database. State change skipped.", @event.RiderId);
                    return;
                }

                if (@event.TargetState == "IDLE")
                {
                    if (rider.State == RiderState.OFFLINE)
                    {
                        await _stateMachine.TransitionRiderAsync(rider, RiderState.IDLE);
                    }
                    else
                    {
                        _logger.LogDebug("Rider {RiderId} connect ignored since state is {State} (expected OFFLINE)", rider.Id, rider.State);
                    }
                }
                else if (@event.TargetState == "RECOVER")
                {
                    if (rider.State == RiderState.STALE)
                    {
                        var hasActiveJob = await _dbContext.Orders.AnyAsync(o => 
                            o.AssignedRiderId == @event.RiderId && 
                            (o.State == OrderState.ASSIGNED || o.State == OrderState.PICKING_UP || o.State == OrderState.DELIVERING));
                            
                        var newState = hasActiveJob ? RiderState.BUSY : RiderState.IDLE;
                        await _stateMachine.TransitionRiderAsync(rider, newState);
                    }
                    else
                    {
                        _logger.LogDebug("Rider {RiderId} recovery connect ignored since state is {State} (expected STALE)", rider.Id, rider.State);
                    }
                }
                else if (@event.TargetState == "STALE")
                {
                    if (rider.State != RiderState.OFFLINE)
                    {
                        await _stateMachine.TransitionRiderAsync(rider, RiderState.STALE);
                    }
                    else
                    {
                        _logger.LogDebug("Rider {RiderId} disconnect ignored since state is already OFFLINE", rider.Id);
                    }
                }
                else
                {
                    _logger.LogWarning("Unsupported target state received: {TargetState}", @event.TargetState);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute durable state machine transition for Rider {RiderId} to target {TargetState}", @event.RiderId, @event.TargetState);
                throw; // Throw to trigger RabbitMQ retry/DLQ mechanism
            }
        }
    }
}
