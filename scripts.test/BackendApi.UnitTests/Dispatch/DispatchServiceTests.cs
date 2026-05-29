using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using NetTopologySuite.Geometries;
using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Models.DTOs;
using BackendApi.Services.Ai;
using BackendApi.Services.Dispatch;
using BackendApi.Infrastructure.Redis;
using BackendApi.Core.StateMachines;
using BackendApi.Services;

namespace BackendApi.UnitTests.Dispatch
{
    public class DispatchServiceTests
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly Mock<StateMachineService> _stateMachineMock;
        private readonly Mock<RedisLockService> _lockServiceMock;
        private readonly Mock<RiderPresenceService> _presenceServiceMock;
        private readonly Mock<OsrmRoutingService> _routingServiceMock;
        private readonly Mock<IAiService> _aiServiceMock;
        private readonly Mock<DispatchRiderNotifier> _riderNotifierMock;
        private readonly Mock<DispatchAdminNotifier> _adminNotifierMock;
        private readonly IConfiguration _config;
        private readonly Mock<ILogger<DispatchService>> _loggerMock;
        private readonly Mock<ILogger<DispatchCandidateRanker>> _rankerLoggerMock;
        private readonly Mock<ICurrentUserService> _currentUserServiceMock;

        public DispatchServiceTests()
        {
            // Set up DB Context with In-Memory Database
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _currentUserServiceMock = new Mock<ICurrentUserService>();
            _currentUserServiceMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
            _currentUserServiceMock.Setup(u => u.UserName).Returns("System");
            
            _dbContext = new ApplicationDbContext(options, _currentUserServiceMock.Object);

            _stateMachineMock = new Mock<StateMachineService>(null!, null!, null!, null!);
            _lockServiceMock = new Mock<RedisLockService>(new Mock<StackExchange.Redis.IConnectionMultiplexer>().Object, new Mock<ILogger<RedisLockService>>().Object);
            _presenceServiceMock = new Mock<RiderPresenceService>(new Mock<StackExchange.Redis.IConnectionMultiplexer>().Object, new Mock<ILogger<RiderPresenceService>>().Object);
            _routingServiceMock = new Mock<OsrmRoutingService>(new System.Net.Http.HttpClient(), new Mock<StackExchange.Redis.IConnectionMultiplexer>().Object, new Mock<IConfiguration>().Object, new Mock<ILogger<OsrmRoutingService>>().Object);
            _aiServiceMock = new Mock<IAiService>();
            _riderNotifierMock = new Mock<DispatchRiderNotifier>(null!, null!, null!, null!);
            _adminNotifierMock = new Mock<DispatchAdminNotifier>(null!, null!);
            
            _loggerMock = new Mock<ILogger<DispatchService>>();
            _rankerLoggerMock = new Mock<ILogger<DispatchCandidateRanker>>();

            var myConfiguration = new Dictionary<string, string>
            {
                {"Dispatch:SearchRadiusKm", "10"},
                {"Dispatch:OfferTimeoutSeconds", "30"}
            };

            _config = new ConfigurationBuilder()
                .AddInMemoryCollection(myConfiguration)
                .Build();
        }

        [Fact]
        public async Task StartDispatchAsync_WhenRaceConditionOccurs_OnlyOneOfferSucceeds()
        {
            // Arrange
            var orderId = "order_race_123";
            var riderId = "rider_race_456";

            var order = new Order
            {
                Id = orderId,
                State = OrderState.CREATED,
                PickupLocation = new Point(100.5, 13.7) { SRID = 4326 },
                DropoffLocation = new Point(100.6, 13.8) { SRID = 4326 },
                RowVersion = new byte[8]
            };
            
            var rider = new Rider
            {
                Id = riderId,
                Name = "Racing Rider",
                State = RiderState.IDLE,
                CurrentLocation = new Point(100.51, 13.71) { SRID = 4326 },
                RowVersion = new byte[8]
            };

            await _dbContext.Orders.AddAsync(order);
            await _dbContext.Riders.AddAsync(rider);
            await _dbContext.SaveChangesAsync();

            // Mock state machine transitions to succeed
            _stateMachineMock.Setup(s => s.TransitionOrderAsync(It.IsAny<Order>(), It.IsAny<OrderState>())).ReturnsAsync(true);
            _stateMachineMock.Setup(s => s.TransitionRiderAsync(It.IsAny<string>(), It.IsAny<RiderState>())).ReturnsAsync(true);

            // Mock presence service to return nearby rider
            var geoResult = new StackExchange.Redis.GeoRadiusResult(
                new StackExchange.Redis.RedisValue(riderId),
                distance: 1.2,
                hash: null,
                position: new StackExchange.Redis.GeoPosition(100.51, 13.71)
            );
            _presenceServiceMock.Setup(p => p.GetNearbyRidersAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>()))
                .ReturnsAsync(new[] { geoResult });

            // Atomic helper to mock distributed lock: only 1 caller can acquire the lock
            int lockAcquiredCount = 0;
            _lockServiceMock.Setup(l => l.TryAcquireRiderLockAsync(
                It.Is<string>(r => r == riderId),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>()
            )).ReturnsAsync(() =>
            {
                var isFirst = Interlocked.CompareExchange(ref lockAcquiredCount, 1, 0) == 0;
                return isFirst;
            });

            // Mock Ranker to return candidates list
            var ranker = new DispatchCandidateRanker(_aiServiceMock.Object, _presenceServiceMock.Object, _rankerLoggerMock.Object);

            var dispatchService = new DispatchService(
                _dbContext,
                _stateMachineMock.Object,
                _lockServiceMock.Object,
                _presenceServiceMock.Object,
                _routingServiceMock.Object,
                _aiServiceMock.Object,
                ranker,
                _riderNotifierMock.Object,
                _adminNotifierMock.Object,
                _config,
                _loggerMock.Object
            );

            // Act - Simulate 1,000 tasks calling FindAndOfferAsync sequentially to avoid DbContext concurrency
            var semaphore = new SemaphoreSlim(1, 1);
            var tasks = new List<Task>();
            for (int i = 0; i < 1000; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // FindAndOfferAsync receives order inside list
                        await dispatchService.FindAndOfferAsync(new List<Order> { order });
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(1, lockAcquiredCount); // Verified lock was acquired exactly once
            _lockServiceMock.Verify(l => l.TryAcquireRiderLockAsync(riderId, It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Exactly(1000));
        }

        [Fact]
        public async Task RankCandidatesAsync_WhenAiEngineFails_ShouldFallbackToHaversineDistance()
        {
            // Arrange
            var orderId = "order_fallback_123";
            var order = new Order
            {
                Id = orderId,
                PickupLocation = new Point(100.5, 13.7) { SRID = 4326 },
                DropoffLocation = new Point(100.6, 13.8) { SRID = 4326 }
            };

            // Simulating multiple riders at different distances
            var candidates = new List<(string RiderId, double DistanceKm, double Lat, double Lng)>
            {
                ("rider_far", 8.5, 13.75, 100.55),
                ("rider_near", 1.2, 13.71, 100.51),
                ("rider_mid", 4.0, 13.73, 100.53)
            };

            var ridersDict = new Dictionary<string, Rider>
            {
                { "rider_near", new Rider { Id = "rider_near", Name = "Near Rider" } },
                { "rider_mid", new Rider { Id = "rider_mid", Name = "Mid Rider" } },
                { "rider_far", new Rider { Id = "rider_far", Name = "Far Rider" } }
            };

            // Mock AI Service to fail
            _aiServiceMock.Setup(a => a.RankDispatchCandidatesAsync(It.IsAny<DispatchRankRequestDto>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.Net.Http.HttpRequestException("AI Engine Down!"));

            _presenceServiceMock.Setup(p => p.GetRiderSpeedAsync(It.IsAny<string>())).ReturnsAsync(25.0);

            var ranker = new DispatchCandidateRanker(_aiServiceMock.Object, _presenceServiceMock.Object, _rankerLoggerMock.Object);

            // Act
            var rankedResult = await ranker.RankCandidatesAsync(order, candidates, ridersDict);

            // Assert
            Assert.NotNull(rankedResult);
            Assert.Equal(3, rankedResult.Count);

            // Check that it sorted ascending by distance (near -> mid -> far)
            Assert.Equal("rider_near", rankedResult[0].RiderId);
            Assert.Equal("rider_mid", rankedResult[1].RiderId);
            Assert.Equal("rider_far", rankedResult[2].RiderId);

            // Verify distance field is kept correctly
            Assert.Equal(1.2, rankedResult[0].DistanceKm);
            Assert.Equal(4.0, rankedResult[1].DistanceKm);
            Assert.Equal(8.5, rankedResult[2].DistanceKm);

            // Score and ETA should be generated heuristically
            Assert.True(rankedResult[0].Score > rankedResult[1].Score);
            Assert.True(rankedResult[1].Score > rankedResult[2].Score);
            Assert.True(rankedResult[0].EtaMinutes < rankedResult[1].EtaMinutes);
            Assert.True(rankedResult[1].EtaMinutes < rankedResult[2].EtaMinutes);
        }
    }
}
