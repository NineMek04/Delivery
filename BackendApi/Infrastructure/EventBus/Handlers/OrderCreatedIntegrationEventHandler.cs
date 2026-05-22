using BackendApi.Infrastructure.EventBus.Events;
using Microsoft.Extensions.Logging;

namespace BackendApi.Infrastructure.EventBus.Handlers;

/// <summary>
/// Handles OrderCreatedIntegrationEvent asynchronously.
/// </summary>
public class OrderCreatedIntegrationEventHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    private readonly ILogger<OrderCreatedIntegrationEventHandler> _logger;

    public OrderCreatedIntegrationEventHandler(ILogger<OrderCreatedIntegrationEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderCreatedIntegrationEvent @event)
    {
        _logger.LogInformation(
            "Handling integration event: {EventName} ({EventId}) - Order Ref: {RefNumber}, Distance: {Distance}km, Fee: {Fee} THB",
            nameof(OrderCreatedIntegrationEvent),
            @event.Id,
            @event.RefNumber,
            @event.DistanceKm,
            @event.DeliveryFee
        );

        // Place any cross-boundary business logic here, e.g., triggering notifications or cache priming

        return Task.CompletedTask;
    }
}
