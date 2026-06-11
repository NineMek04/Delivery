using System.Net;
using BackendApi.Core.StateMachines;
using BackendApi.Hubs;
using BackendApi.Infrastructure.EventBus.Events;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Services;
using BackendApi.Services.Ai;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

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
        var middleware = new BackendApi.Setup.CsrfValidationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<BackendApi.Setup.CsrfValidationMiddleware>.Instance,
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
