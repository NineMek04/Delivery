using System.Net;
using BackendApi.Core.StateMachines;
using BackendApi.Hubs;
using BackendApi.Hubs.Chat;
using BackendApi.Hubs.Tracking;
using BackendApi.Infrastructure.EventBus.Events;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Models.DTOs;
using BackendApi.Services;
using BackendApi.Services.Auth;
using BackendApi.Services.Notifications;
using BackendApi.Services.Orders;
using BackendApi.Services.Ai;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BackendApi.Data;
using BackendApi.Services.BackgroundWorkers;
using BackendApi.Services.BackgroundWorkers.Queues;
using BackendApi.Services.BackgroundWorkers.Maintenance;
using BackendApi.Services.BackgroundWorkers.Jobs;
using BackendApi.Services.Telemetry;
using BackendApi.Services.Dispatch;
using BackendApi.Services.Tracking;
using StackExchange.Redis;
using BackendApi.Infrastructure.Redis;
using Microsoft.Extensions.Logging;
using Order = BackendApi.Models.Entities.Order;
using Mapster;

namespace BackendApi.UnitTests;

public class QABugRegressionTests
{
    [Fact]
    public void OrderStateRules_DeliveringCanBeCancelled()
    {
        Assert.True(OrderStateRules.IsValidTransition(
            OrderState.DELIVERING,
            OrderState.CANCELLED));
        Assert.False(OrderStateRules.IsValidTransition(
            OrderState.COMPLETED,
            OrderState.CANCELLED));
    }

    [Fact]
    public void OrderDto_DefaultStatusIsCreated()
    {
        Assert.Equal("CREATED", new OrderDto().Status);
    }

    [Fact]
    public void RiderDto_DefaultStatusIsOffline()
    {
        Assert.Equal("OFFLINE", new RiderDto().Status);
    }

    [Fact]
    public void RiderDto_MappingUsesCanonicalRiderState()
    {
        BackendApi.Core.Mappings.MappingConfig.Configure();
        var dto = new Rider
        {
            Id = "rider-1",
            Name = "Rider One",
            State = RiderState.RESERVED
        }.Adapt<RiderDto>();

        Assert.Equal("RESERVED", dto.Status);
    }

    [Fact]
    public void StoreRejectionTransition_IsLimitedToCreatedOrder()
    {
        Assert.True(OrderStateRules.IsValidTransition(
            OrderState.CREATED,
            OrderState.CANCELLED));
        Assert.False(OrderStateRules.IsValidTransition(
            OrderState.COMPLETED,
            OrderState.CANCELLED));
    }

    [Fact]
    public void OrderStatusChangedIntegrationEvent_PreservesCorrelationAndShopFields()
    {
        var integrationEvent = new OrderStatusChangedIntegrationEvent(
            "order-1",
            42,
            OrderState.OFFERING,
            OrderState.ASSIGNED,
            "rider-1",
            "customer-1",
            "correlation-1",
            "shop-1");

        Assert.Equal("correlation-1", integrationEvent.CorrelationId);
        Assert.Equal("shop-1", integrationEvent.ShopId);
    }

    [Fact]
    public async Task PredictEtaAsync_WhenAiFails_IncludesPickupAndDropoffOverhead()
    {
        var client = new HttpClient(new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
        {
            BaseAddress = new Uri("http://ai.test")
        };
        var service = new AiService(client, NullLogger<AiService>.Instance);

        var result = await service.PredictEtaAsync(new PredictEtaRequestDto
        {
            RouteDistanceMeters = 5_000,
            RouteDurationSeconds = 600,
            CurrentTime = "2026-06-11T12:00:00+07:00",
            WeatherCondition = "clear",
            TrafficLevel = "normal"
        });

        Assert.NotNull(result);
        Assert.Equal(23, result!.EtaMinutes);
        Assert.Equal(10d, result.Factors["dispatch_pickup_mins"]);
    }

    [Fact]
    public async Task NotifyOrderStatusChangedAsync_SendsObjectPayloadWithContractFields()
    {
        var proxy = new Mock<IClientProxy>();
        object?[]? capturedArguments = null;
        proxy
            .Setup(client => client.SendCoreAsync(
                "OrderStatusChanged",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>(
                (_, arguments, _) => capturedArguments ??= arguments)
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients
            .Setup(client => client.Group(It.IsAny<string>()))
            .Returns(proxy.Object);

        var hubContext = new Mock<IHubContext<TrackingHub>>();
        hubContext.SetupGet(context => context.Clients).Returns(clients.Object);

        var service = new OrderNotificationService(
            hubContext.Object,
            NullLogger<OrderNotificationService>.Instance);
        var order = new Order
        {
            Id = "order-1",
            RefNumber = 42,
            State = OrderState.DELIVERING,
            AssignedRiderId = "rider-1",
            ShopId = "shop-1",
            CustomerId = "customer-1"
        };

        await service.NotifyOrderStatusChangedAsync(
            order,
            OrderState.PICKING_UP);

        Assert.NotNull(capturedArguments);
        var payload = Assert.Single(capturedArguments!);
        Assert.NotNull(payload);

        var values = payload!.GetType()
            .GetProperties()
            .ToDictionary(property => property.Name, property => property.GetValue(payload));

        Assert.Equal("order-1", values["orderId"]);
        Assert.Equal("ORD-000042", values["orderRefNumber"]);
        Assert.Equal("PICKING_UP", values["previousStatus"]);
        Assert.Equal("DELIVERING", values["newStatus"]);
        Assert.Equal("rider-1", values["riderId"]);
        Assert.IsType<DateTime>(values["timestamp"]);

        proxy.Verify(client => client.SendCoreAsync(
            "OrderStatusChanged",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task CsrfMiddleware_RejectsOriginPrefixSpoofing()
    {
        var nextCalled = false;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
                ["AllowedHosts"] = "*"
            })
            .Build();
        var middleware = new BackendApi.Setup.Middlewares.CsrfValidationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<BackendApi.Setup.Middlewares.CsrfValidationMiddleware>.Instance,
            configuration);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/orders";
        context.Request.Headers.Origin = "http://localhost:4200.evil.example";
        context.Request.Headers.Cookie = "access_token=fake; XSRF-TOKEN=csrf-token";
        context.Request.Headers["X-XSRF-TOKEN"] = "csrf-token";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task TelemetryBroadcastWorker_RefreshDatabaseSnapshots_NoDoubleCountingStaleRiders()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserServiceMock.Setup(u => u.UserName).Returns("System");

        using var dbContext = new ApplicationDbContext(options, currentUserServiceMock.Object);
        // Add offline rider
        dbContext.Riders.Add(new Rider { Id = "r-offline", State = RiderState.OFFLINE, RowVersion = new byte[8] });
        // Add stale rider
        dbContext.Riders.Add(new Rider { Id = "r-stale", State = RiderState.STALE, RowVersion = new byte[8] });
        // Add busy rider
        dbContext.Riders.Add(new Rider { Id = "r-busy", State = RiderState.BUSY, RowVersion = new byte[8] });
        // Add idle rider
        dbContext.Riders.Add(new Rider { Id = "r-idle", State = RiderState.IDLE, RowVersion = new byte[8] });
        await dbContext.SaveChangesAsync();

        var serviceProviderMock = new Mock<IServiceProvider>();
        var serviceScopeMock = new Mock<IServiceScope>();
        var serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();

        serviceScopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        serviceProviderMock.Setup(s => s.GetService(typeof(ApplicationDbContext))).Returns(dbContext);
        serviceScopeFactoryMock.Setup(f => f.CreateScope()).Returns(serviceScopeMock.Object);
        serviceProviderMock.Setup(s => s.GetService(typeof(IServiceScopeFactory))).Returns(serviceScopeFactoryMock.Object);

        // Mock Redis (since hotspots refresh queries IConnectionMultiplexer)
        var redisMock = new Mock<IConnectionMultiplexer>();
        var redisDbMock = new Mock<IDatabase>();
        redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbMock.Object);
        serviceProviderMock.Setup(s => s.GetService(typeof(IConnectionMultiplexer))).Returns(redisMock.Object);

        var aggregator = new TelemetryAggregator();
        
        var worker = new TelemetryBroadcastWorker(
            serviceProviderMock.Object,
            aggregator,
            new Mock<IHubContext<TrackingHub>>().Object,
            new Mock<ILogger<TelemetryBroadcastWorker>>().Object
        );

        // Act - Invoke private method RefreshDatabaseSnapshotsAsync via reflection
        var method = typeof(TelemetryBroadcastWorker).GetMethod("RefreshDatabaseSnapshotsAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        Assert.NotNull(method);
        await (Task)method!.Invoke(worker, new object[] { CancellationToken.None, false })!;

        // Assert
        var telemetry = aggregator.GetTelemetry(2.0);
        var utilization = aggregator.GetUtilization();

        // Active riders must be busy + idle (which is 2: r-busy and r-idle). STALE and OFFLINE must be excluded.
        Assert.Equal(2, telemetry.ActiveRidersCount);
        // Offline riders must be offline + stale (which is 2: r-offline and r-stale).
        Assert.Equal(2, utilization.RidersOfflineCount);
    }

    [Fact]
    public async Task HeartbeatMonitor_CheckRiderHeartbeats_SendsSignalRStatusUpdated()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserServiceMock.Setup(u => u.UserName).Returns("System");

        using var dbContext = new ApplicationDbContext(options, currentUserServiceMock.Object);
        // Add a rider who will go stale (idle state, no heartbeat)
        dbContext.Riders.Add(new Rider { Id = "r-test-heartbeat", State = RiderState.IDLE, RowVersion = new byte[8] });
        await dbContext.SaveChangesAsync();

        var serviceProviderMock = new Mock<IServiceProvider>();
        var serviceScopeMock = new Mock<IServiceScope>();
        var serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();

        serviceScopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        serviceProviderMock.Setup(s => s.GetService(typeof(ApplicationDbContext))).Returns(dbContext);
        serviceScopeFactoryMock.Setup(f => f.CreateScope()).Returns(serviceScopeMock.Object);
        serviceProviderMock.Setup(s => s.GetService(typeof(IServiceScopeFactory))).Returns(serviceScopeFactoryMock.Object);

        // Mock Presence Service: last heartbeat was 60 seconds ago (so it's > 20s timeout)
        var presenceServiceMock = new Mock<RiderPresenceService>(new Mock<IConnectionMultiplexer>().Object, new Mock<ILogger<RiderPresenceService>>().Object);
        presenceServiceMock.Setup(p => p.GetLastHeartbeatAsync("r-test-heartbeat")).ReturnsAsync(DateTime.UtcNow.AddSeconds(-60));
        serviceProviderMock.Setup(s => s.GetService(typeof(RiderPresenceService))).Returns(presenceServiceMock.Object);

        // State machine service: transition the rider
        var stateMachineMock = new Mock<StateMachineService>(dbContext, new Mock<Infrastructure.EventBus.IEventBus>().Object, new Mock<IConnectionMultiplexer>().Object, new Mock<IHttpContextAccessor>().Object, new Mock<ILogger<StateMachineService>>().Object);
        stateMachineMock.Setup(s => s.TransitionRiderAsync(It.IsAny<Rider>(), It.IsAny<RiderState>())).ReturnsAsync(true);
        serviceProviderMock.Setup(s => s.GetService(typeof(StateMachineService))).Returns(stateMachineMock.Object);

        // DispatchOfferHandler mock
        var offerHandlerMock = new Mock<DispatchOfferHandler>(dbContext, stateMachineMock.Object, null!, null!, new Mock<IConnectionMultiplexer>().Object, serviceProviderMock.Object, new Mock<ILogger<DispatchOfferHandler>>().Object);
        serviceProviderMock.Setup(s => s.GetService(typeof(DispatchOfferHandler))).Returns(offerHandlerMock.Object);

        // Mock SignalR
        var proxyMock = new Mock<IClientProxy>();
        string? sentMethod = null;
        object? sentPayload = null;
        proxyMock.Setup(p => p.SendCoreAsync("RiderStatusUpdated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) => {
                sentMethod = method;
                sentPayload = args[0];
            })
            .Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(c => c.Group("admins")).Returns(proxyMock.Object);

        var hubContextMock = new Mock<IHubContext<TrackingHub>>();
        hubContextMock.SetupGet(h => h.Clients).Returns(clientsMock.Object);
        serviceProviderMock.Setup(s => s.GetService(typeof(IHubContext<TrackingHub>))).Returns(hubContextMock.Object);

        var myConfiguration = new Dictionary<string, string>
        {
            {"Dispatch:HeartbeatTimeoutSeconds", "20"},
            {"Dispatch:StaleToOfflineSeconds", "120"}
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration)
            .Build();

        var monitor = new HeartbeatMonitor(
            serviceProviderMock.Object,
            config,
            new Mock<ILogger<HeartbeatMonitor>>().Object
        );

        // Act - Invoke private method CheckRiderHeartbeatsAsync
        var method = typeof(HeartbeatMonitor).GetMethod("CheckRiderHeartbeatsAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        Assert.NotNull(method);
        await (Task)method!.Invoke(monitor, new object[] { CancellationToken.None })!;

        // Assert
        Assert.Equal("RiderStatusUpdated", sentMethod);
        Assert.NotNull(sentPayload);
        var payloadProperties = sentPayload.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(sentPayload));
        Assert.Equal("r-test-heartbeat", payloadProperties["RiderId"]);
        Assert.Equal("STALE", payloadProperties["NewStatus"]);
        Assert.Equal("IDLE", payloadProperties["PreviousStatus"]);
        Assert.Equal("heartbeat_timeout", payloadProperties["Reason"]);
    }

    [Fact]
    public async Task DispatchOfferHandler_RejectOrTimeout_PublishesSingleStatusIntegrationEvent()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserServiceMock.Setup(u => u.UserName).Returns("System");

        using var dbContext = new ApplicationDbContext(options, currentUserServiceMock.Object);
        // Add an order currently in OFFERING state
        dbContext.Orders.Add(new Order 
        { 
            Id = "o-test-reject", 
            CurrentOfferId = "offer-test", 
            AssignedRiderId = "rider-test",
            State = OrderState.OFFERING,
            RowVersion = new byte[8]
        });
        // Add rider
        dbContext.Riders.Add(new Rider { Id = "rider-test", State = RiderState.RESERVED, RowVersion = new byte[8] });
        await dbContext.SaveChangesAsync();

        var serviceProviderMock = new Mock<IServiceProvider>();
        var serviceScopeMock = new Mock<IServiceScope>();
        var serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();

        serviceScopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        serviceProviderMock.Setup(s => s.GetService(typeof(ApplicationDbContext))).Returns(dbContext);
        serviceScopeFactoryMock.Setup(f => f.CreateScope()).Returns(serviceScopeMock.Object);
        serviceProviderMock.Setup(s => s.GetService(typeof(IServiceScopeFactory))).Returns(serviceScopeFactoryMock.Object);

        var redisMock = new Mock<IConnectionMultiplexer>();
        var redisDbMock = new Mock<IDatabase>();
        redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(redisDbMock.Object);
        redisDbMock.Setup(r => r.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
        redisDbMock.Setup(r => r.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>())).ReturnsAsync(true);

        // Mock StateMachineService
        var eventBusMock = new Mock<Infrastructure.EventBus.IEventBus>();
        eventBusMock
            .Setup(bus => bus.PublishAsync(
                It.IsAny<Infrastructure.EventBus.Events.OrderStatusChangedIntegrationEvent>()))
            .Returns(Task.CompletedTask);
        var stateMachine = new StateMachineService(dbContext, eventBusMock.Object, redisMock.Object, new Mock<IHttpContextAccessor>().Object, new Mock<ILogger<StateMachineService>>().Object);
        serviceProviderMock.Setup(s => s.GetService(typeof(StateMachineService))).Returns(stateMachine);

        // Mock RedisLockService
        var redisLockMock = new Mock<RedisLockService>(redisMock.Object, new Mock<ILogger<RedisLockService>>().Object);
        redisLockMock.Setup(r => r.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        // Mock DispatchService (re-dispatch recipient)
        var mockDispatch = new Mock<DispatchService>(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        mockDispatch.Setup(d => d.FindAndOfferAsync(It.IsAny<List<Order>>())).Returns(Task.CompletedTask);
        serviceProviderMock.Setup(s => s.GetService(typeof(DispatchService))).Returns(mockDispatch.Object);

        var loggedMessages = new List<string>();
        Exception? loggedException = null;
        var loggerMock = new Mock<ILogger<DispatchOfferHandler>>();
        loggerMock.Setup(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(i => {
                var level = (LogLevel)i.Arguments[0];
                var state = i.Arguments[2];
                var ex = i.Arguments[3] as Exception;
                loggedMessages.Add($"[{level}] {state}");
                if (ex != null) loggedException = ex;
            }));

        var handler = new DispatchOfferHandler(
            dbContext,
            stateMachine,
            redisLockMock.Object,
            new Mock<DispatchAdminNotifier>(new Mock<IHubContext<TrackingHub>>().Object, new Mock<ILogger<DispatchAdminNotifier>>().Object).Object,
            redisMock.Object,
            serviceProviderMock.Object,
            loggerMock.Object
        );

        // Act
        await handler.RejectOrTimeoutAsync("offer-test", "rider-test");

        // Assert
        var updatedOrder = await dbContext.Orders.FindAsync("o-test-reject");
        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderState.MATCHING, updatedOrder.State);
        eventBusMock.Verify(bus => bus.PublishAsync(
            It.Is<Infrastructure.EventBus.Events.OrderStatusChangedIntegrationEvent>(
                integrationEvent =>
                    integrationEvent.OrderId == "o-test-reject" &&
                    integrationEvent.OldState == OrderState.OFFERING &&
                    integrationEvent.NewState == OrderState.MATCHING)),
            Times.Once);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }
}


