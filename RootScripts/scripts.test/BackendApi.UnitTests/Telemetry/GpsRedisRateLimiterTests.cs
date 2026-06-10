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

        // ─────────────────────────────────────────────────────────────────
        // GetRecommendedInterval — dynamic interval thresholds
        // ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(12000, 15)]  // above critical threshold
        [InlineData(10000, 15)]  // exactly at critical threshold
        [InlineData(8000,  10)]  // between high and critical
        [InlineData(5000,  10)]  // exactly at high threshold
        [InlineData(3000,   5)]  // between moderate and high
        [InlineData(1000,   5)]  // exactly at moderate threshold
        [InlineData(500,    3)]  // below moderate — normal load
        [InlineData(0,      3)]  // empty queue — normal load
        public void GetRecommendedInterval_ShouldReturnCorrectInterval(int pendingCount, int expectedInterval)
        {
            // Act
            int interval = _rateLimiter.GetRecommendedInterval(pendingCount);

            // Assert
            Assert.Equal(expectedInterval, interval);
        }

        // ─────────────────────────────────────────────────────────────────
        // ShouldRateLimitAsync — normal flow
        // ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ShouldRateLimitAsync_WhenKeyDoesNotExist_ShouldAllowRequestAndSetTTL()
        {
            // Arrange
            string riderId = "rider_123";
            int pendingQueueCount = 500; // expected interval = 3s
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
            Assert.False(result); // false = allowed (not rate-limited)
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
            int pendingQueueCount = 6000; // expected interval = 10s
            string expectedKey = $"rider_last_gps_limit:{riderId}";

            // StringSetAsync returns false when key already exists (NX condition not met)
            _dbMock.Setup(d => d.StringSetAsync(
                It.Is<RedisKey>(k => k == expectedKey),
                It.Is<RedisValue>(v => v == "1"),
                It.Is<TimeSpan?>(t => t != null && t.Value.TotalSeconds == 10),
                When.NotExists
            )).ReturnsAsync(false);

            // Act
            bool result = await _rateLimiter.ShouldRateLimitAsync(riderId, pendingQueueCount);

            // Assert
            Assert.True(result); // true = blocked (rate-limited)
            _dbMock.Verify(d => d.StringSetAsync(
                It.Is<RedisKey>(k => k == expectedKey),
                It.Is<RedisValue>(v => v == "1"),
                It.Is<TimeSpan?>(t => t != null && t.Value.TotalSeconds == 10),
                When.NotExists
            ), Times.Once);
        }

        // ─────────────────────────────────────────────────────────────────
        // [NEW] Redis connection failure — fail-open resilience (Bug #3)
        // ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ShouldRateLimitAsync_WhenRedisThrowsConnectionException_ShouldBypassAndReturnFalse()
        {
            // Arrange — Redis is down; StringSetAsync throws RedisConnectionException
            string riderId = "rider_resilience";
            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>()
            )).ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis is down"));

            // Act
            bool result = await _rateLimiter.ShouldRateLimitAsync(riderId, pendingQueueCount: 0);

            // Assert — fail-open: GPS update must NOT be blocked when Redis is unavailable
            Assert.False(result);
        }

        [Fact]
        public async Task ShouldRateLimitAsync_WhenRedisThrowsTimeoutException_ShouldBypassAndReturnFalse()
        {
            // Arrange — Redis call times out
            string riderId = "rider_timeout";
            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>()
            )).ThrowsAsync(new RedisTimeoutException("Command timed out", CommandStatus.WaitingToBeSent));

            // Act
            bool result = await _rateLimiter.ShouldRateLimitAsync(riderId, pendingQueueCount: 500);

            // Assert — a Redis timeout must not block all rider location updates
            Assert.False(result);
        }

        [Fact]
        public async Task ShouldRateLimitAsync_WhenRedisUnavailable_ShouldLogWarning()
        {
            // Arrange
            string riderId = "rider_log_check";
            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>()
            )).ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis offline"));

            // Act
            await _rateLimiter.ShouldRateLimitAsync(riderId, pendingQueueCount: 0);

            // Assert — a Warning-level log must be emitted (not swallowed silently)
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(riderId)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        // ─────────────────────────────────────────────────────────────────
        // GetRecommendedInterval — boundary edge cases
        // ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(9999,  10)]  // just below critical
        [InlineData(10001, 15)]  // just above critical
        [InlineData(4999,   5)]  // just below high
        [InlineData(999,    3)]  // just below moderate
        public void GetRecommendedInterval_BoundaryEdgeCases_ShouldReturnCorrectInterval(int pendingCount, int expectedInterval)
        {
            Assert.Equal(expectedInterval, _rateLimiter.GetRecommendedInterval(pendingCount));
        }

        // ─────────────────────────────────────────────────────────────────
        // TTL correctness — each load tier gets the right TTL
        // ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0,     3)]
        [InlineData(2000,  5)]
        [InlineData(7000, 10)]
        [InlineData(15000,15)]
        public async Task ShouldRateLimitAsync_ShouldPassCorrectTtlToRedis(int pendingQueueCount, int expectedTtl)
        {
            // Arrange
            string riderId = $"rider_ttl_{pendingQueueCount}";
            TimeSpan? capturedTtl = null;

            _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                When.NotExists
            )).Callback<RedisKey, RedisValue, TimeSpan?, When>((_, _, ttl, _) =>
            {
                capturedTtl = ttl;
            }).ReturnsAsync(true);

            // Act
            await _rateLimiter.ShouldRateLimitAsync(riderId, pendingQueueCount);

            // Assert
            Assert.NotNull(capturedTtl);
            Assert.Equal(expectedTtl, (int)capturedTtl!.Value.TotalSeconds);
        }
    }
}
