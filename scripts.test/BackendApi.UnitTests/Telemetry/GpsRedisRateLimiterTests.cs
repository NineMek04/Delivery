using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;
using BackendApi.Features.FleetTracking.Telemetry;

namespace BackendApi.UnitTests.Telemetry
{
    public class GpsRedisRateLimiterTests
    {
        private readonly Mock<IConnectionMultiplexer> _redisMock;
        private readonly Mock<IDatabase> _dbMock;
        private readonly Mock<ILogger<GpsRedisRateLimiter>> _loggerMock;
        private readonly GpsRedisRateLimiter _rateLimiter;

        public GpsRedisRateLimiterTests()
        {
            _redisMock = new Mock<IConnectionMultiplexer>();
            _dbMock = new Mock<IDatabase>();
            _loggerMock = new Mock<ILogger<GpsRedisRateLimiter>>();

            // Setup IConnectionMultiplexer to return mocked IDatabase
            _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);

            _rateLimiter = new GpsRedisRateLimiter(_redisMock.Object, _loggerMock.Object);
        }

        [Theory]
        [InlineData(12000, 15)]
        [InlineData(10000, 15)]
        [InlineData(8000, 10)]
        [InlineData(5000, 10)]
        [InlineData(3000, 5)]
        [InlineData(1000, 5)]
        [InlineData(500, 3)]
        [InlineData(0, 3)]
        public void GetRecommendedInterval_ShouldReturnCorrectInterval(int pendingCount, int expectedInterval)
        {
            // Act
            int interval = _rateLimiter.GetRecommendedInterval(pendingCount);

            // Assert
            Assert.Equal(expectedInterval, interval);
        }

        [Fact]
        public async Task ShouldRateLimitAsync_WhenKeyDoesNotExist_ShouldAllowRequestAndSetTTL()
        {
            // Arrange
            string riderId = "rider_123";
            int pendingQueueCount = 500; // Expected interval is 3s
            string expectedKey = $"rider_last_gps_limit:{riderId}";

            // StringSetAsync returns true when the set succeeds (key was not present)
            _dbMock.Setup(d => d.StringSetAsync(
                It.Is<RedisKey>(k => k == expectedKey),
                It.Is<RedisValue>(v => v == "1"),
                It.Is<TimeSpan?>(t => t != null && t.Value.TotalSeconds == 3),
                When.NotExists
            )).ReturnsAsync(true);

            // Act
            bool result = await _rateLimiter.ShouldRateLimitAsync(riderId, pendingQueueCount);

            // Assert
            Assert.False(result); // result is rateLimited, so false means allow
            _dbMock.Verify(d => d.StringSetAsync(
                It.Is<RedisKey>(k => k == expectedKey),
                It.Is<RedisValue>(v => v == "1"),
                It.Is<TimeSpan?>(t => t != null && t.Value.TotalSeconds == 3),
                When.NotExists
            ), Times.Once);
        }

        [Fact]
        public async Task ShouldRateLimitAsync_WhenKeyExists_ShouldBlockRequest()
        {
            // Arrange
            string riderId = "rider_456";
            int pendingQueueCount = 6000; // Expected interval is 10s
            string expectedKey = $"rider_last_gps_limit:{riderId}";

            // StringSetAsync returns false when the set fails because key already exists
            _dbMock.Setup(d => d.StringSetAsync(
                It.Is<RedisKey>(k => k == expectedKey),
                It.Is<RedisValue>(v => v == "1"),
                It.Is<TimeSpan?>(t => t != null && t.Value.TotalSeconds == 10),
                When.NotExists
            )).ReturnsAsync(false);

            // Act
            bool result = await _rateLimiter.ShouldRateLimitAsync(riderId, pendingQueueCount);

            // Assert
            Assert.True(result); // result is rateLimited, so true means blocked
            _dbMock.Verify(d => d.StringSetAsync(
                It.Is<RedisKey>(k => k == expectedKey),
                It.Is<RedisValue>(v => v == "1"),
                It.Is<TimeSpan?>(t => t != null && t.Value.TotalSeconds == 10),
                When.NotExists
            ), Times.Once);
        }
    }
}
