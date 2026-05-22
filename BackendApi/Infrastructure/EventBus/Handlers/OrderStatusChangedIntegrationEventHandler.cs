using BackendApi.Infrastructure.EventBus.Events;
using Microsoft.Extensions.Logging;

namespace BackendApi.Infrastructure.EventBus.Handlers;

/// <summary>
/// Handles OrderStatusChangedIntegrationEvent asynchronously.
/// </summary>
public class OrderStatusChangedIntegrationEventHandler : IIntegrationEventHandler<OrderStatusChangedIntegrationEvent>
{
    private readonly ILogger<OrderStatusChangedIntegrationEventHandler> _logger;

    public OrderStatusChangedIntegrationEventHandler(ILogger<OrderStatusChangedIntegrationEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderStatusChangedIntegrationEvent @event)
    {
        _logger.LogInformation(
            "Handling integration event: {EventName} ({EventId}) - Order {OrderId} (Ref: {RefNumber}) changed from {OldState} to {NewState}",
            nameof(OrderStatusChangedIntegrationEvent),
            @event.Id,
            @event.OrderId,
            @event.RefNumber,
            @event.OldState,
            @event.NewState
        );

        // Place any cross-boundary business logic here, e.g., real-time analytics aggregation, rider workload updates

        return Task.CompletedTask;
    }
}
