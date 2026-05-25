namespace BackendApi.Infrastructure.EventBus;

/// <summary>
/// Defines a handler for an integration event.
/// </summary>
/// <typeparam name="TIntegrationEvent">The type of integration event to handle.</typeparam>
public interface IIntegrationEventHandler<in TIntegrationEvent>
    where TIntegrationEvent : IntegrationEvent
{
    Task Handle(TIntegrationEvent @event);
}
