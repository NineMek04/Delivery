using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using Moq;
using StackExchange.Redis;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using BackendApi.Data;
using BackendApi.Hubs;
using BackendApi.Infrastructure.Redis;
using BackendApi.Services.Telemetry;
using BackendApi.Features.FleetTracking.Telemetry;
using BackendApi.Services.Ai;
using BackendApi.Services;

namespace BackendApi.UnitTests.Telemetry
{
    public class RiderPresenceServiceTests
    {
        private readonly Mock<IConnectionMultiplexer> _redisMock;
        private readonly Mock<IDatabase> _dbMock;
        private readonly Mock<IBatch> _batchMock;
        private readonly Mock<ILogger<RiderPresenceService>> _presenceLoggerMock;
        private readonly Mock<ILogger<TelemetryService>> _telemetryLoggerMock;
        private readonly Mock<ApplicationDbContext> _dbContextMock;
        private readonly Mock<GpsRedisRateLimiter> _rateLimiterMock;
        private readonly Mock<GpsRabbitMqPublisher> _gpsPublisherMock;
        private readonly Mock<TelemetryAggregator> _aggregatorMock;
        private readonly Mock<OsrmRoutingService> _routingServiceMock;
        private readonly Mock<IHubContext<TrackingHub>> _hubContextMock;

        public RiderPresenceServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
            currentUserServiceMock.Setup(u => u.UserName).Returns("System");

            _redisMock = new Mock<IConnectionMultiplexer>();
            _dbMock = new Mock<IDatabase>();
            _batchMock = new Mock<IBatch>();
            _presenceLoggerMock = new Mock<ILogger<RiderPresenceService>>();
            _telemetryLoggerMock = new Mock<ILogger<TelemetryService>>();
            _dbContextMock = new Mock<ApplicationDbContext>(options, currentUserServiceMock.Object);
            _rateLimiterMock = new Mock<GpsRedisRateLimiter>(null!, null!);
            _gpsPublisherMock = new Mock<GpsRabbitMqPublisher>(
                new Mock<IConfiguration>().Object, 
                new Mock<ILogger<GpsRabbitMqPublisher>>().Object,
                new Mock<IHostApplicationLifetime>().Object
            );
            _aggregatorMock = new Mock<TelemetryAggregator>();
            _routingServiceMock = new Mock<OsrmRoutingService>(new System.Net.Http.HttpClient(), new Mock<StackExchange.Redis.IConnectionMultiplexer>().Object, new Mock<IConfiguration>().Object, new Mock<ILogger<OsrmRoutingService>>().Object);
            _hubContextMock = new Mock<IHubContext<TrackingHub>>();

            _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
            _dbMock.Setup(d => d.CreateBatch(It.IsAny<object>())).Returns(_batchMock.Object);
        }

        [Fact]
        public async Task UpdateGpsAsync_ShouldMaintainMovingAverageSpeedBufferAndTrimToFive()
        {
            // Arrange
            var riderId = "rider_speed_123";
            var presenceService = new RiderPresenceService(_redisMock.Object, _presenceLoggerMock.Object);

            var speedListKey = $"riders:speed_buffer:{riderId}";

            // Act - perform update
            await presenceService.UpdateGpsAsync(riderId, 13.7, 100.5, 35.5);

            // Assert
            _batchMock.Verify(b => b.GeoAddAsync("riders:locations", 100.5, 13.7, riderId, CommandFlags.None), Times.Once);
            _batchMock.Verify(b => b.HashSetAsync(
                $"riders:gps:{riderId}",
                It.Is<HashEntry[]>(arr => 
                    arr.Any(e => e.Name == "lat" && (double)e.Value == 13.7) &&
                    arr.Any(e => e.Name == "lng" && (double)e.Value == 100.5) &&
                    arr.Any(e => e.Name == "speed_kmh" && (double)e.Value == 35.5)
                ),
                CommandFlags.None
            ), Times.Once);

            // Verify moving average buffer list operations are queued in batch
            _batchMock.Verify(b => b.ListRightPushAsync(speedListKey, 35.5, When.Always, CommandFlags.None), Times.Once);
            _batchMock.Verify(b => b.ListTrimAsync(speedListKey, -5, -1, CommandFlags.None), Times.Once);
            _batchMock.Verify(b => b.KeyExpireAsync(speedListKey, It.Is<TimeSpan?>(t => t != null && t.Value.TotalMinutes == 5), ExpireWhen.Always, CommandFlags.None), Times.Once);
            
            // Verify batch is executed
            _batchMock.Verify(b => b.Execute(), Times.Once);
        }

        [Fact]
        public async Task ProcessLocationUpdateAsync_WithInvalidCoordinates_ShouldRejectImmediately()
        {
            // Arrange
            var riderId = "rider_invalid_coord";
            var presenceService = new RiderPresenceService(_redisMock.Object, _presenceLoggerMock.Object);

            // Mock TelemetryService dependencies
            var telemetryService = new TelemetryService(
                _dbContextMock.Object,
                presenceService,
                _rateLimiterMock.Object,
                _gpsPublisherMock.Object,
                _aggregatorMock.Object,
                _routingServiceMock.Object,
                _hubContextMock.Object,
                _redisMock.Object,
                _telemetryLoggerMock.Object
            );

            // Act
            await telemetryService.ProcessLocationUpdateAsync(riderId, 95.0, 100.5, 10.0, bypassRateLimit: true); // Lat 95 is invalid

            // Assert - check that IDatabase batch or Redis calls were never made
            _dbMock.Verify(d => d.CreateBatch(It.IsAny<object>()), Times.Never);
        }

        [Fact]
        public async Task ProcessLocationUpdateAsync_WhenTeleportAnomalyDetected_ShouldIgnoreUpdate()
        {
            // Arrange
            var riderId = "rider_warp_99";
            var presenceServiceMock = new Mock<RiderPresenceService>(_redisMock.Object, _presenceLoggerMock.Object);

            // Set up last known location 2 seconds ago at (13.7000, 100.5000)
            var lastKnown = (Lat: 13.7000, Lng: 100.5000, UpdatedAt: DateTime.UtcNow.AddSeconds(-2));
            presenceServiceMock.Setup(p => p.GetLastKnownLocationAsync(riderId)).ReturnsAsync(lastKnown);

            var telemetryService = new TelemetryService(
                _dbContextMock.Object,
                presenceServiceMock.Object,
                _rateLimiterMock.Object,
                _gpsPublisherMock.Object,
                _aggregatorMock.Object,
                _routingServiceMock.Object,
                _hubContextMock.Object,
                _redisMock.Object,
                _telemetryLoggerMock.Object
            );

            // Act - Send coordinates 20 kilometers away (Teleport anomaly!)
            await telemetryService.ProcessLocationUpdateAsync(riderId, 13.9000, 100.7000, 10.0, bypassRateLimit: true);

            // Assert
            // UpdateGpsAsync should never be called for this warp coordinate
            presenceServiceMock.Verify(p => p.UpdateGpsAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>()), Times.Never);
        }
    }
}
