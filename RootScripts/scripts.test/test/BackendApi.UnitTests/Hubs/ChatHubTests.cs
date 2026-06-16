using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BackendApi.Hubs;
using BackendApi.Hubs.Chat;
using BackendApi.Hubs.Tracking;
using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Core.DataHandlers;
using BackendApi.Core.Constants;
using BackendApi.Core.StateMachines;
using BackendApi.Services;
using BackendApi.Services.Auth;
using BackendApi.Services.Notifications;
using BackendApi.Services.Orders;
using BackendApi.Security;
using BackendApi.Security.Models;
using BackendApi.Security.Services;

namespace BackendApi.UnitTests.Hubs
{
    public class ChatHubTests
    {
        private sealed class FakeHubCallerContext : HubCallerContext
        {
            private readonly ClaimsPrincipal _user;
            private readonly FeatureCollection _features = new FeatureCollection();

            public bool AbortCalled { get; private set; }

            public FakeHubCallerContext(ClaimsPrincipal user)
            {
                _user = user;
            }

            public override string ConnectionId => "fake-chat-conn-id";
            public override string? UserIdentifier => null;
            public override ClaimsPrincipal User => _user;
            public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
            public override CancellationToken ConnectionAborted => CancellationToken.None;
            public override IFeatureCollection Features => _features;
            public override void Abort() => AbortCalled = true;
        }

        private static FakeHubCallerContext MakeContext(string? role, string? userId, string? shopId = null)
        {
            var claims = new List<Claim>();
            if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));
            if (userId != null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            if (shopId != null) claims.Add(new Claim("shop_id", shopId));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            return new FakeHubCallerContext(principal);
        }

        private static (ChatHub hub, ApplicationDbContext dbContext) BuildSut(FakeHubCallerContext context)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var userSvc = new Mock<ICurrentUserService>();
            userSvc.Setup(u => u.UserId).Returns((Guid?)Guid.NewGuid());
            userSvc.Setup(u => u.UserName).Returns((string?)"TestUser");

            var dbContext = new ApplicationDbContext(options, userSvc.Object);

            var httpContextAccessor = new Mock<IHttpContextAccessor>();
            var dbHandler = new DBHandlerCore(dbContext, new ConditionContext(), httpContextAccessor.Object);

            var logger = new Mock<ILogger<ChatHub>>();
            var hub = new ChatHub(dbHandler, logger.Object);
            hub.Context = context;

            return (hub, dbContext);
        }

        [Fact]
        public async Task OnConnectedAsync_WhenUserIsRider_ShouldCacheRiderId()
        {
            // Arrange
            var riderId = "rider_cached_123";
            var userId = "user_rider_456";
            var context = MakeContext(AuthConstants.RiderRole, userId);
            var (hub, dbContext) = BuildSut(context);

            var user = new User
            {
                Id = userId,
                Email = "rider@test.com",
                FullName = "Test Rider",
                Role = "Rider",
                RiderId = riderId,
                RowVersion = new byte[8]
            };
            await dbContext.Users.AddAsync(user);
            await dbContext.SaveChangesAsync();

            // Act
            await hub.OnConnectedAsync();

            // Assert
            Assert.True(context.Items.ContainsKey("RiderId"));
            Assert.Equal(riderId, context.Items["RiderId"]);
        }

        [Fact]
        public async Task JoinOrderChat_WhenOrderOlderThan7Days_ShouldThrowHubException()
        {
            // Arrange
            var orderId = "order_old_7";
            var context = MakeContext(AuthConstants.AdminRole, "admin_user");
            var (hub, dbContext) = BuildSut(context);

            var order = new Order
            {
                Id = orderId,
                State = OrderState.CREATED,
                RowVersion = new byte[8]
            };
            await dbContext.Orders.AddAsync(order);
            await dbContext.SaveChangesAsync();

            // Update CreatedAt in modified state to bypass Added audit overwrite
            order.CreatedAt = DateTime.UtcNow.AddDays(-8);
            await dbContext.SaveChangesAsync();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinOrderChat(orderId));
            Assert.Contains("Cannot access chat history for orders older than 7 days.", ex.Message);
        }

        [Fact]
        public async Task JoinOrderChat_WhenSuccessful_ShouldReturnAtMost50MessagesChronologically()
        {
            // Arrange
            var orderId = "order_active_50";
            var context = MakeContext(AuthConstants.AdminRole, "admin_user");
            var (hub, dbContext) = BuildSut(context);

            var order = new Order
            {
                Id = orderId,
                State = OrderState.CREATED,
                RowVersion = new byte[8]
            };
            await dbContext.Orders.AddAsync(order);

            // Add 60 chat messages
            for (int i = 1; i <= 60; i++)
            {
                var msg = new ChatMessage
                {
                    Id = $"msg_{i}",
                    OrderId = orderId,
                    SenderId = "sender_1",
                    SenderRole = "Customer",
                    Message = $"Message number {i}",
                    RowVersion = new byte[8]
                };
                await dbContext.ChatMessages.AddAsync(msg);
            }
            await dbContext.SaveChangesAsync();

            // Update CreatedAt timestamps to distinct values after initial save to avoid audit overwrite
            var baseTime = DateTime.UtcNow.AddHours(-10);
            var savedMessages = await dbContext.ChatMessages.ToListAsync();
            for (int i = 0; i < savedMessages.Count; i++)
            {
                savedMessages[i].CreatedAt = baseTime.AddMinutes(i + 1);
            }
            await dbContext.SaveChangesAsync();

            var clientsMock = new Mock<IHubCallerClients>();
            var callerMock = new Mock<ISingleClientProxy>();
            clientsMock.Setup(c => c.Caller).Returns(callerMock.Object);
            hub.Clients = clientsMock.Object;

            var groupsMock = new Mock<IGroupManager>();
            hub.Groups = groupsMock.Object;

            // Capture returned messages
            object? capturedHistoryObj = null;
            callerMock
                .Setup(c => c.SendCoreAsync("ChatHistoryReceived", It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                .Callback<string, object[], CancellationToken>((method, args, ct) => {
                    capturedHistoryObj = args[1];
                })
                .Returns(Task.CompletedTask);

            // Act
            await hub.JoinOrderChat(orderId);

            // Assert
            Assert.NotNull(capturedHistoryObj);
            var historyList = Assert.IsAssignableFrom<System.Collections.IEnumerable>(capturedHistoryObj);
            
            int count = 0;
            var list = new List<dynamic>();
            foreach (var item in historyList)
            {
                list.Add(item);
                count++;
            }

            Assert.Equal(50, count); // Limited to 50

            // The history should be chronological (from oldest to newest of the last 50).
            // Total was 60. The last 50 are 11 to 60.
            // Oldest of last 50 is Message 11, newest is Message 60.
            var firstMsg = list[0];
            var lastMsg = list[49];

            // Extract the messages
            var firstMessageProp = firstMsg.GetType().GetProperty("Message").GetValue(firstMsg);
            var lastMessageProp = lastMsg.GetType().GetProperty("Message").GetValue(lastMsg);

            Assert.Equal("Message number 11", firstMessageProp);
            Assert.Equal("Message number 60", lastMessageProp);
        }

        [Fact]
        public async Task VerifyUserAccess_RiderRoleUsingCache_ShouldBypassDatabase()
        {
            // Arrange
            var riderId = "rider_cached_verify";
            var userId = "user_rider_verify";
            var orderId = "order_rider_verify";
            var context = MakeContext(AuthConstants.RiderRole, userId);
            
            // Explicitly set RiderId in Items cache
            context.Items["RiderId"] = riderId;

            var (hub, dbContext) = BuildSut(context);

            var order = new Order
            {
                Id = orderId,
                AssignedRiderId = riderId,
                CreatedAt = DateTime.UtcNow,
                RowVersion = new byte[8]
            };
            await dbContext.Orders.AddAsync(order);
            await dbContext.SaveChangesAsync();

            var clientsMock = new Mock<IHubCallerClients>();
            clientsMock.Setup(c => c.Caller).Returns(new Mock<ISingleClientProxy>().Object);
            hub.Clients = clientsMock.Object;

            var groupsMock = new Mock<IGroupManager>();
            hub.Groups = groupsMock.Object;

            // Act & Assert
            // This join should succeed because VerifyUserAccess will read RiderId from Items cache and match the order.
            // If it tried to query the database for User, it would return null (since we didn't add User to DB).
            await hub.JoinOrderChat(orderId);
        }

        [Fact]
        public async Task VerifyUserAccess_StorePartnerRoleUsingClaims_ShouldBypassDatabase()
        {
            // Arrange
            var shopId = "shop_claims_verify";
            var orderId = "order_shop_verify";
            var context = MakeContext(AuthConstants.StorePartnerRole, "user_shop", shopId);

            var (hub, dbContext) = BuildSut(context);

            var order = new Order
            {
                Id = orderId,
                ShopId = shopId,
                CreatedAt = DateTime.UtcNow,
                RowVersion = new byte[8]
            };
            await dbContext.Orders.AddAsync(order);
            await dbContext.SaveChangesAsync();

            var clientsMock = new Mock<IHubCallerClients>();
            clientsMock.Setup(c => c.Caller).Returns(new Mock<ISingleClientProxy>().Object);
            hub.Clients = clientsMock.Object;

            var groupsMock = new Mock<IGroupManager>();
            hub.Groups = groupsMock.Object;

            // Act & Assert
            // This join should succeed because VerifyUserAccess reads shopId directly from claims and matches.
            // If it queried the database, it would fail since no user is created in DB.
            await hub.JoinOrderChat(orderId);
        }

        [Fact]
        public async Task CheckRateLimit_WhenExceeding5CallsIn5Seconds_ShouldThrowHubException()
        {
            // Arrange
            var orderId = "order_active_limit";
            var context = MakeContext(AuthConstants.AdminRole, "admin_user");
            var (hub, dbContext) = BuildSut(context);

            var order = new Order
            {
                Id = orderId,
                State = OrderState.CREATED,
                CreatedAt = DateTime.UtcNow,
                RowVersion = new byte[8]
            };
            await dbContext.Orders.AddAsync(order);
            await dbContext.SaveChangesAsync();

            var clientsMock = new Mock<IHubCallerClients>();
            var callerMock = new Mock<ISingleClientProxy>();
            clientsMock.Setup(c => c.Caller).Returns(callerMock.Object);
            hub.Clients = clientsMock.Object;

            var groupsMock = new Mock<IGroupManager>();
            hub.Groups = groupsMock.Object;

            // Act & Assert
            // Call JoinOrderChat 5 times successfully
            for (int i = 0; i < 5; i++)
            {
                await hub.JoinOrderChat(orderId);
            }

            // The 6th call should throw HubException due to Rate Limiting
            var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinOrderChat(orderId));
            Assert.Contains("Rate limit exceeded.", ex.Message);
        }
    }
}


