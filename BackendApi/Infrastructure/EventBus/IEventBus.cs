namespace BackendApi.Infrastructure.EventBus;

/// <summary>
/// Defines the messaging contract for publishing and subscribing to integration events.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an integration event to the event bus.
    /// </summary>
    Task PublishAsync<T>(T @event) where T : IntegrationEvent;

    /// <summary>
    /// Subscribes a handler to a specific integration event.
    /// </summary>
    void Subscribe<T, TH>()
        where T : IntegrationEvent
        where TH : IIntegrationEventHandler<T>;
}
