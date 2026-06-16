using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;
using BackendApi.Data;
using BackendApi.Infrastructure.Redis;
using BackendApi.Services;
using BackendApi.Services.Auth;
using BackendApi.Services.Notifications;
using BackendApi.Services.Orders;

namespace BackendApi.UnitTests.Dispatch;

public class RedisLockServiceFallbackTests
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<ILogger<RedisLockService>> _loggerMock;

    public RedisLockServiceFallbackTests()
    {
        // Set up DB Context with In-Memory Database for Unit Tests
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUserServiceMock.Setup(u => u.UserName).Returns("TestSystem");

        _dbContext = new ApplicationDbContext(options, currentUserServiceMock.Object);

        // Mock Redis Connection to throw exception simulating Redis Down
        _redisMock = new Mock<IConnectionMultiplexer>();
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated connection error"));

        _loggerMock = new Mock<ILogger<RedisLockService>>();
    }

    [Fact]
    public async Task TryAcquireRiderLockAsync_WhenRedisIsDown_AcquiresPostgresFallbackLockSuccessfully()
    {
        // Arrange
        var lockService = new RedisLockService(_redisMock.Object, _loggerMock.Object, _dbContext);
        var riderId = "rider_test_1";
        var offerId = "offer_test_1";
        var timeout = TimeSpan.FromSeconds(5);

        // Act
        var acquired = await lockService.TryAcquireRiderLockAsync(riderId, offerId, timeout);

        // Assert
        Assert.True(acquired);
        
        // Verify holder
        var holder = await lockService.GetLockHolderAsync(riderId);
        Assert.Equal(offerId, holder);

        var isLocked = await lockService.IsLockedAsync(riderId);
        Assert.True(isLocked);
    }

    [Fact]
    public async Task TryAcquireRiderLockAsync_WhenRedisIsDownAndRiderAlreadyLocked_AcquisitionFails()
    {
        // Arrange
        var lockService = new RedisLockService(_redisMock.Object, _loggerMock.Object, _dbContext);
        var riderId = "rider_test_2";
        var firstOffer = "offer_first";
        var secondOffer = "offer_second";
        var timeout = TimeSpan.FromSeconds(5);

        // Act - First acquisition
        var firstAcquired = await lockService.TryAcquireRiderLockAsync(riderId, firstOffer, timeout);
        Assert.True(firstAcquired);

        // Act - Second acquisition
        var secondAcquired = await lockService.TryAcquireRiderLockAsync(riderId, secondOffer, timeout);
        
        // Assert
        Assert.False(secondAcquired);
        var holder = await lockService.GetLockHolderAsync(riderId);
        Assert.Equal(firstOffer, holder); // Holder should still be firstOffer
    }

    [Fact]
    public async Task TryAcquireRiderLockAsync_WhenFallbackLockExpired_AcquisitionSucceeds()
    {
        // Arrange
        var lockService = new RedisLockService(_redisMock.Object, _loggerMock.Object, _dbContext);
        var riderId = "rider_test_3";
        var firstOffer = "offer_first";
        var secondOffer = "offer_second";
        
        // Act - First acquisition with negative timeout (simulates expired lock immediately)
        var firstAcquired = await lockService.TryAcquireRiderLockAsync(riderId, firstOffer, TimeSpan.FromSeconds(-5));
        Assert.True(firstAcquired);

        // Act - Second acquisition should now succeed because first is expired
        var secondAcquired = await lockService.TryAcquireRiderLockAsync(riderId, secondOffer, TimeSpan.FromSeconds(5));
        
        // Assert
        Assert.True(secondAcquired);
        var holder = await lockService.GetLockHolderAsync(riderId);
        Assert.Equal(secondOffer, holder);
    }

    [Fact]
    public async Task ReleaseLockAsync_WhenRedisIsDown_ReleasesPostgresFallbackLockSuccessfully()
    {
        // Arrange
        var lockService = new RedisLockService(_redisMock.Object, _loggerMock.Object, _dbContext);
        var riderId = "rider_test_4";
        var offerId = "offer_test_4";
        var timeout = TimeSpan.FromSeconds(5);

        // Acquire first
        var acquired = await lockService.TryAcquireRiderLockAsync(riderId, offerId, timeout);
        Assert.True(acquired);

        // Act - Release lock
        var released = await lockService.ReleaseLockAsync(riderId, offerId);

        // Assert
        Assert.True(released);
        var holder = await lockService.GetLockHolderAsync(riderId);
        Assert.Null(holder);

        var isLocked = await lockService.IsLockedAsync(riderId);
        Assert.False(isLocked);
    }

    [Fact]
    public async Task ReleaseLockAsync_WithWrongOfferId_DoesNotReleaseLock()
    {
        // Arrange
        var lockService = new RedisLockService(_redisMock.Object, _loggerMock.Object, _dbContext);
        var riderId = "rider_test_5";
        var offerId = "offer_test_5";
        var wrongOfferId = "offer_wrong";
        var timeout = TimeSpan.FromSeconds(5);

        // Acquire first
        var acquired = await lockService.TryAcquireRiderLockAsync(riderId, offerId, timeout);
        Assert.True(acquired);

        // Act - Release lock with wrong ID
        var released = await lockService.ReleaseLockAsync(riderId, wrongOfferId);

        // Assert
        Assert.False(released);
        var holder = await lockService.GetLockHolderAsync(riderId);
        Assert.Equal(offerId, holder); // Holder remains unchanged
    }
}

