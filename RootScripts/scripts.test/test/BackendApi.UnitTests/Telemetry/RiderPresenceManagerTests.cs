using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Infrastructure.Redis;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Services;
using BackendApi.Services.Auth;
using BackendApi.Services.Notifications;
using BackendApi.Services.Orders;
using BackendApi.Services.Dispatch;
using BackendApi.Services.Tracking;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BackendApi.UnitTests.Telemetry;

public class RiderPresenceManagerTests
{
    [Fact]
    public async Task HandleRiderStatusUpdateAsync_ShouldRejectOfflineWithActiveOrder()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(service => service.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(service => service.UserName).Returns("System");

        await using var dbContext =
            new ApplicationDbContext(options, currentUser.Object);
        var rider = new Rider
        {
            Id = "rider-active",
            Name = "Active Rider",
            State = RiderState.BUSY,
            RowVersion = new byte[8]
        };
        var order = new Order
        {
            Id = "order-active",
            AssignedRiderId = rider.Id,
            State = OrderState.DELIVERING,
            RowVersion = new byte[8]
        };
        dbContext.AddRange(rider, order);
        await dbContext.SaveChangesAsync();

        var manager = new RiderPresenceManager(
            dbContext,
            new Mock<RiderPresenceService>(
                new Mock<StackExchange.Redis.IConnectionMultiplexer>().Object,
                new Mock<ILogger<RiderPresenceService>>().Object).Object,
            new Mock<StateMachineService>(
                null!,
                null!,
                null!,
                null!,
                null!).Object,
            new Mock<IEventBus>().Object,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<ILogger<RiderPresenceManager>>().Object,
            new Mock<IServiceProvider>().Object);

        var result = await manager.HandleRiderStatusUpdateAsync(
            rider.Id,
            RiderState.OFFLINE.ToString());

        Assert.False(result.Success);
        Assert.Equal(RiderState.BUSY, rider.State);
    }
}


