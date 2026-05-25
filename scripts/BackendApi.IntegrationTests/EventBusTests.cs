using System.Text.Json.Serialization;
using BackendApi.Infrastructure.EventBus;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackendApi.IntegrationTests;

public record TestIntegrationEvent : IntegrationEvent
{
    public string Data { get; init; } = null!;

    public TestIntegrationEvent() { }

    [JsonConstructor]
    public TestIntegrationEvent(string data)
    {
        Data = data;
    }
}

public class TestIntegrationEventHandler : IIntegrationEventHandler<TestIntegrationEvent>
{
    public static bool WasCalled { get; private set; }
    public static string? ReceivedData { get; private set; }
    public static TaskCompletionSource<bool> Tcs = new();

    public Task Handle(TestIntegrationEvent @event)
    {
        WasCalled = true;
        ReceivedData = @event.Data;
        Tcs.TrySetResult(true);
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        WasCalled = false;
        ReceivedData = null;
        Tcs = new TaskCompletionSource<bool>();
    }
}

[Collection("SharedTestDatabase")]
public class EventBusTests : IAsyncLifetime
{
    private readonly DeliveryWebApplicationFactory _factory;

    public EventBusTests(DeliveryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EventBus_PublishAndSubscribe_FlowSucceeds()
    {
        // Arrange
        var testFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddTransient<TestIntegrationEventHandler>();
            });
        });

        // Resolve Event Bus
        var eventBus = testFactory.Services.GetRequiredService<IEventBus>();
        
        // Reset state
        TestIntegrationEventHandler.Reset();

        // Subscribe to custom test event
        eventBus.Subscribe<TestIntegrationEvent, TestIntegrationEventHandler>();

        // Act
        var expectedData = $"Integration-Test-Data-{Guid.NewGuid():N}";
        await eventBus.PublishAsync(new TestIntegrationEvent(expectedData));

        // Assert - Wait up to 5 seconds for asynchronous RabbitMQ consumer to process
        var completed = await Task.WhenAny(
            TestIntegrationEventHandler.Tcs.Task, 
            Task.Delay(TimeSpan.FromSeconds(5))
        );

        Assert.Same(TestIntegrationEventHandler.Tcs.Task, completed);
        Assert.True(TestIntegrationEventHandler.WasCalled);
        Assert.Equal(expectedData, TestIntegrationEventHandler.ReceivedData);
    }
}
