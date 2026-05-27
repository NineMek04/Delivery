using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using BackendApi.Infrastructure.Redis;
using Xunit;

namespace BackendApi.IntegrationTests;

public class RedisResilienceTests
{
    [Fact]
    public async Task RiderPresenceService_RedisThrowsException_ReturnsSafeFallbacks()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        var mockLogger = new Mock<ILogger<RiderPresenceService>>();

        // Setup methods to throw RedisConnectionException
        mockDb.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated Redis fail"));

        mockDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated Redis fail"));

        mockDb.Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated Redis fail"));

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        var service = new RiderPresenceService(mockRedis.Object, mockLogger.Object);

        // Act & Assert
        // 1. UpdateHeartbeatAsync should catch exception and not throw
        var hbException = await Record.ExceptionAsync(() => service.UpdateHeartbeatAsync("rider-123"));
        Assert.Null(hbException);

        // 2. GetLastKnownLocationAsync should return null
        var location = await service.GetLastKnownLocationAsync("rider-123");
        Assert.Null(location);

        // 3. GetRiderSpeedAsync should return 0.0
        var speed = await service.GetRiderSpeedAsync("rider-123");
        Assert.Equal(0.0, speed);

        // 4. GetLastHeartbeatAsync should return null
        var lastHb = await service.GetLastHeartbeatAsync("rider-123");
        Assert.Null(lastHb);
    }

    [Fact]
    public async Task RedisLockService_RedisThrowsException_ReturnsFalseAndNull()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        var mockLogger = new Mock<ILogger<RedisLockService>>();

        mockDb.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated Redis fail"));

        mockDb.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated Redis fail"));

        mockDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated Redis fail"));

        mockDb.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated Redis fail"));

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        var service = new RedisLockService(mockRedis.Object, mockLogger.Object);

        // Act & Assert
        // TryAcquireRiderLockAsync should return false
        var lockAcquired = await service.TryAcquireRiderLockAsync("rider-123", "offer-123", TimeSpan.FromSeconds(10));
        Assert.False(lockAcquired);

        // ReleaseLockAsync should return false
        var lockReleased = await service.ReleaseLockAsync("rider-123", "offer-123");
        Assert.False(lockReleased);

        // GetLockHolderAsync should return null
        var holder = await service.GetLockHolderAsync("rider-123");
        Assert.Null(holder);

        // IsLockedAsync should return false
        var isLocked = await service.IsLockedAsync("rider-123");
        Assert.False(isLocked);
    }
}
