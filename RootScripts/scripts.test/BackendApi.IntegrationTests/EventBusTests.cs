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
    public static int CallCount { get; private set; }
    public static string? ReceivedData { get; private set; }
    public static TaskCompletionSource<bool> Tcs = new();

    public Task Handle(TestIntegrationEvent @event)
    {
        CallCount++;
        ReceivedData = @event.Data;
        Tcs.TrySetResult(true);
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        CallCount = 0;
        ReceivedData = null;
        Tcs = new TaskCompletionSource<bool>();
    }
}

public record ThrowingIntegrationEvent : IntegrationEvent
{
    public string Data { get; init; } = null!;

    public ThrowingIntegrationEvent() { }

    [JsonConstructor]
    public ThrowingIntegrationEvent(string data)
    {
        Data = data;
    }
}

public class ThrowingIntegrationEventHandler : IIntegrationEventHandler<ThrowingIntegrationEvent>
{
    public static bool WasCalled { get; private set; }
    public static TaskCompletionSource<bool> Tcs = new();

    public Task Handle(ThrowingIntegrationEvent @event)
    {
        WasCalled = true;
        Tcs.TrySetResult(true);
        throw new InvalidOperationException("Simulated event handler failure for DLQ verification");
    }

    public static void Reset()
    {
        WasCalled = false;
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
        Assert.Equal(1, TestIntegrationEventHandler.CallCount);
        Assert.Equal(expectedData, TestIntegrationEventHandler.ReceivedData);
    }

    [Fact]
    public async Task EventBus_Idempotency_SameEventProcessedOnce()
    {
        // Arrange
        var testFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddTransient<TestIntegrationEventHandler>();
            });
        });

        var eventBus = testFactory.Services.GetRequiredService<IEventBus>();
        TestIntegrationEventHandler.Reset();
        eventBus.Subscribe<TestIntegrationEvent, TestIntegrationEventHandler>();

        var eventId = Guid.NewGuid();
        var data = $"Idempotent-Test-Data-{Guid.NewGuid():N}";
        var integrationEvent = new TestIntegrationEvent(data) { Id = eventId };

        // Act - Publish same event twice
        await eventBus.PublishAsync(integrationEvent);
        await eventBus.PublishAsync(integrationEvent);

        // Wait for potential dual processing
        await Task.WhenAny(
            TestIntegrationEventHandler.Tcs.Task,
            Task.Delay(TimeSpan.FromSeconds(3))
        );

        // Allow another brief moment for second execution attempt to finish if any
        await Task.Delay(500);

        // Assert - CallCount should be strictly 1
        Assert.Equal(1, TestIntegrationEventHandler.CallCount);
    }

    [Fact]
    public async Task EventBus_HandlerThrows_EventHandledGracefully()
    {
        // Arrange
        var testFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddTransient<ThrowingIntegrationEventHandler>();
                services.AddTransient<TestIntegrationEventHandler>();
            });
        });

        var eventBus = testFactory.Services.GetRequiredService<IEventBus>();
        
        ThrowingIntegrationEventHandler.Reset();
        TestIntegrationEventHandler.Reset();

        eventBus.Subscribe<ThrowingIntegrationEvent, ThrowingIntegrationEventHandler>();
        eventBus.Subscribe<TestIntegrationEvent, TestIntegrationEventHandler>();

        // Act - Publish throwing event and then a normal one to verify event bus survival
        var throwEvent = new ThrowingIntegrationEvent("Boom");
        var normalEvent = new TestIntegrationEvent("Survive");

        await eventBus.PublishAsync(throwEvent);

        // Wait for the throwing event handler to execute
        var throwCompleted = await Task.WhenAny(
            ThrowingIntegrationEventHandler.Tcs.Task,
            Task.Delay(TimeSpan.FromSeconds(5))
        );
        Assert.Same(ThrowingIntegrationEventHandler.Tcs.Task, throwCompleted);
        Assert.True(ThrowingIntegrationEventHandler.WasCalled);

        // Now publish the normal event to ensure the bus is healthy and auto-recovered
        await eventBus.PublishAsync(normalEvent);

        var normalCompleted = await Task.WhenAny(
            TestIntegrationEventHandler.Tcs.Task,
            Task.Delay(TimeSpan.FromSeconds(5))
        );
        Assert.Same(TestIntegrationEventHandler.Tcs.Task, normalCompleted);
        Assert.Equal(1, TestIntegrationEventHandler.CallCount);
    }
}

