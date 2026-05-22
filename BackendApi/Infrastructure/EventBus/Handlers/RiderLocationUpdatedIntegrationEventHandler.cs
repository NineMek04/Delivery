using BackendApi.Infrastructure.EventBus.Events;
using Microsoft.Extensions.Logging;

namespace BackendApi.Infrastructure.EventBus.Handlers;

/// <summary>
/// Handles RiderLocationUpdatedIntegrationEvent asynchronously.
/// </summary>
public class RiderLocationUpdatedIntegrationEventHandler : IIntegrationEventHandler<RiderLocationUpdatedIntegrationEvent>
{
    private readonly ILogger<RiderLocationUpdatedIntegrationEventHandler> _logger;

    public RiderLocationUpdatedIntegrationEventHandler(ILogger<RiderLocationUpdatedIntegrationEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(RiderLocationUpdatedIntegrationEvent @event)
    {
        _logger.LogInformation(
            "Handling integration event: {EventName} ({EventId}) - Rider: {RiderId}, Coordinates: ({Latitude}, {Longitude}) at {Timestamp}",
            nameof(RiderLocationUpdatedIntegrationEvent),
            @event.Id,
            @event.RiderId,
            @event.Latitude,
            @event.Longitude,
            @event.Timestamp
        );

        // Place any cross-boundary business logic here, e.g., real-time feed update for AI ranker caching

        return Task.CompletedTask;
    }
}
