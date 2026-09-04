using System.Net;
using System.Text.Json;
using BackendApi.Data;
using BackendApi.Features.AiRouting;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Services;
using BackendApi.Services.Auth;
using BackendApi.Services.Notifications;
using BackendApi.Services.Orders;
using BackendApi.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NetTopologySuite.Geometries;
using StackExchange.Redis;
using DeliveryOrder = BackendApi.Models.Entities.Order;

namespace BackendApi.UnitTests.AiRouting;

[Collection("OSRM circuit breaker")]
public sealed class RiderRouteServiceTests
{
    public RiderRouteServiceTests()
    {
        OsrmCircuitBreakerTestHelper.Reset();
    }

    [Fact]
    public async Task ResolveAsync_AssignedPickupRoute_UsesLocalOsrm()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUserService>();
        await using var dbContext = new ApplicationDbContext(
            options,
            currentUser.Object);

        const string userId = "user-1";
        const string riderId = "rider-1";
        const string orderId = "order-1";
        dbContext.Users.Add(new User
        {
            Id = userId,
            Email = "rider@test.local",
            PasswordHash = "hash",
            FullName = "Rider",
            Role = "Rider",
            RiderId = riderId,
            RowVersion = []
        });
        dbContext.Orders.Add(new DeliveryOrder
        {
            Id = orderId,
            AssignedRiderId = riderId,
            PickupLocation = new Point(102.80, 17.42) { SRID = 4326 },
            DropoffLocation = new Point(102.81, 17.43) { SRID = 4326 },
            RowVersion = []
        });
        await dbContext.SaveChangesAsync();

        HttpRequestMessage? capturedRequest = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(
                (request, _) => capturedRequest = request)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    routes = new[]
                    {
                        new
                        {
                            distance = 1200.0,
                            duration = 300.0,
                            geometry = new
                            {
                                coordinates = new[]
                                {
                                    new[] { 102.7872, 17.4138 },
                                    new[] { 102.80, 17.42 }
                                }
                            }
                        }
                    }
                }))
            });

        var redisDb = new Mock<IDatabase>();
        redisDb
            .Setup(item => item.StringGetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        redisDb
            .Setup(item => item.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        var redis = new Mock<IConnectionMultiplexer>();
        redis
            .Setup(item => item.GetDatabase(
                It.IsAny<int>(),
                It.IsAny<object>()))
            .Returns(redisDb.Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Routing:LocalOsrmUrl"] = "http://osrm:5000"
            })
            .Build();
        var osrm = new OsrmRoutingService(
            new HttpClient(handler.Object),
            redis.Object,
            configuration,
            Mock.Of<ILogger<OsrmRoutingService>>());
        var service = new RiderRouteService(
            dbContext,
            osrm,
            Mock.Of<ILogger<RiderRouteService>>());

        var result = await service.ResolveAsync(
            userId,
            new RiderRouteRequest
            {
                OrderId = orderId,
                RoutePhase = "PICKUP",
                CurrentLat = 17.4138,
                CurrentLng = 102.7872
            },
            "correlation-1",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("LOCAL_OSRM", result.Source);
        Assert.NotEmpty(result.EncodedPolyline);
        Assert.NotNull(capturedRequest);
        Assert.Contains(
            "/route/v1/driving/102.7872,17.4138;102.8,17.42",
            capturedRequest.RequestUri!.ToString());
    }
}



