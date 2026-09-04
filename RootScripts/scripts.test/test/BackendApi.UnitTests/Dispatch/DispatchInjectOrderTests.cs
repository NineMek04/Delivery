using System;
using System.Collections.Generic;
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
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Services.Ai;
using BackendApi.Services.Dispatch;
using BackendApi.Infrastructure.Redis;
using BackendApi.Core.StateMachines;
using BackendApi.Services;
using BackendApi.Services.Auth;
using BackendApi.Services.Notifications;
using BackendApi.Services.Orders;

namespace BackendApi.UnitTests.Dispatch
{
    /// <summary>
    /// Unit tests for TryInjectOrderAsync covering:
    ///   - Issue #1: Concurrent injection lock prevents double-inject into same BUSY rider
    ///   - Issue #2: CREATED → MATCHING state transition is enforced before OFFERING
    /// </summary>
    public class DispatchInjectOrderTests
    {
        // ── Shared test infrastructure ────────────────────────────────────

        private static (DispatchService svc, ApplicationDbContext db,
            Mock<StateMachineService> sm, Mock<RedisLockService> lk)
            BuildSut(
                Dictionary<string, string?>? extraConfig = null,
                Action<Mock<StateMachineService>>? smSetup = null,
                Action<Mock<RedisLockService>>? lkSetup = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var userSvc = new Mock<ICurrentUserService>();
            userSvc.Setup(u => u.UserId).Returns(Guid.NewGuid());
            userSvc.Setup(u => u.UserName).Returns("TestUser");

            var db = new ApplicationDbContext(options, userSvc.Object);

            var sm = new Mock<StateMachineService>(null!, null!, null!, null!, null!);
            var lk = new Mock<RedisLockService>(
                new Mock<StackExchange.Redis.IConnectionMultiplexer>().Object,
                new Mock<ILogger<RedisLockService>>().Object);
            var presence = new Mock<RiderPresenceService>(
                new Mock<StackExchange.Redis.IConnectionMultiplexer>().Object,
                new Mock<ILogger<RiderPresenceService>>().Object);
            var routing = new Mock<OsrmRoutingService>(
                new System.Net.Http.HttpClient(),
                new Mock<StackExchange.Redis.IConnectionMultiplexer>().Object,
                new Mock<IConfiguration>().Object,
                new Mock<ILogger<OsrmRoutingService>>().Object);
            var ai       = new Mock<IAiService>();
            var ranker   = new DispatchCandidateRanker(ai.Object, presence.Object,
                new Mock<ILogger<DispatchCandidateRanker>>().Object);
            var rNotify  = new Mock<DispatchRiderNotifier>(null!, null!, null!, null!, null!, null!);
            var aNotify  = new Mock<DispatchAdminNotifier>(null!, null!);

            // Apply caller-supplied mock setups
            smSetup?.Invoke(sm);
            lkSetup?.Invoke(lk);

            var cfg = new Dictionary<string, string?>
            {
                { "Dispatch:MaxActiveOrdersPerRider", "3" },
                { "Dispatch:OfferTimeoutSeconds",     "30" },
                { "Dispatch:SearchRadiusKm",          "10" }
            };
            if (extraConfig != null)
                foreach (var kv in extraConfig) cfg[kv.Key] = kv.Value;

            var config = new ConfigurationBuilder().AddInMemoryCollection(cfg).Build();
            var logger = new Mock<ILogger<DispatchService>>().Object;

            var svc = new DispatchService(db, sm.Object, lk.Object, presence.Object,
                routing.Object, ai.Object, ranker, rNotify.Object, aNotify.Object, config, logger);

            return (svc, db, sm, lk);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static Order MakeOrder(string id, string shopId,
            OrderState state = OrderState.CREATED) => new()
        {
            Id             = id,
            ShopId         = shopId,
            State          = state,
            PickupLocation = new Point(100.5, 13.7) { SRID = 4326 },
            DropoffLocation = new Point(100.6, 13.8) { SRID = 4326 },
            RowVersion     = new byte[8]
        };

        private static Rider MakeRider(string id,
            RiderState state = RiderState.BUSY) => new()
        {
            Id             = id,
            Name           = $"Rider_{id}",
            State          = state,
            CurrentLocation = new Point(100.51, 13.71) { SRID = 4326 },
            RowVersion     = new byte[8]
        };

        // ═════════════════════════════════════════════════════════════════
        // Issue #2 — State Machine: CREATED → MATCHING must happen before
        //            OFFERING (the old code skipped this transition causing
        //            a Ghost Batch when TransitionOrderAsync(OFFERING) failed)
        // ═════════════════════════════════════════════════════════════════

        [Fact]
        public async Task TryInjectOrderAsync_MustTransitionToMATCHING_BeforeOffering()
        {
            // Arrange
            var (svc, db, sm, lk) = BuildSut();

            var rider = MakeRider("rider_sm");
            var activeOrder = MakeOrder("active_01", "shop_A", OrderState.ASSIGNED);
            activeOrder.AssignedRiderId = rider.Id;

            var newOrder = MakeOrder("new_01", "shop_A", OrderState.CREATED);

            await db.Riders.AddAsync(rider);
            await db.Orders.AddRangeAsync(activeOrder, newOrder);
            await db.SaveChangesAsync();

            // Capture every state transition call in order
            var transitions = new List<(string orderId, OrderState to)>();
            sm.Setup(s => s.TransitionOrderAsync(It.IsAny<Order>(), It.IsAny<OrderState>()))
              .Callback<Order, OrderState>((o, t) => transitions.Add((o.Id, t)))
              .ReturnsAsync(true);

            sm.Setup(s => s.TransitionRiderAsync(It.IsAny<Rider>(), It.IsAny<RiderState>()))
              .ReturnsAsync(true);

            // Injection lock succeeds
            lk.Setup(l => l.TryAcquireRiderLockAsync(
                    It.Is<string>(k => k.Contains("inject_lock")),
                    It.IsAny<string>(), It.IsAny<TimeSpan>()))
              .ReturnsAsync(true);
            lk.Setup(l => l.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(true);

            // Act
            await svc.TryInjectOrderAsync(newOrder.Id);

            // Assert — MATCHING must appear before OFFERING for the injected order
            var matchingIdx = transitions.FindIndex(t => t.orderId == newOrder.Id && t.to == OrderState.MATCHING);
            var offeringIdx = transitions.FindIndex(t => t.orderId == newOrder.Id && t.to == OrderState.OFFERING);

            Assert.True(matchingIdx >= 0,  "MATCHING transition was never called for the injected order.");
            Assert.True(offeringIdx >= 0,  "OFFERING transition was never called for the injected order.");
            Assert.True(matchingIdx < offeringIdx,
                $"MATCHING (idx {matchingIdx}) must precede OFFERING (idx {offeringIdx}).");
        }

        [Fact]
        public async Task TryInjectOrderAsync_WhenMATCHINGTransitionFails_ShouldReturnFalseAndCleanBatchGroupId()
        {
            // Arrange — StateMachine rejects CREATED → MATCHING
            var (svc, db, sm, lk) = BuildSut(
                smSetup: s =>
                {
                    // Reject the MATCHING transition
                    s.Setup(x => x.TransitionOrderAsync(
                           It.IsAny<Order>(),
                           It.Is<OrderState>(st => st == OrderState.MATCHING)))
                     .ReturnsAsync(false);
                });

            var rider = MakeRider("rider_sm_fail");
            var activeOrder = MakeOrder("active_02", "shop_B", OrderState.ASSIGNED);
            activeOrder.AssignedRiderId = rider.Id;
            var newOrder = MakeOrder("new_02", "shop_B", OrderState.CREATED);

            await db.Riders.AddAsync(rider);
            await db.Orders.AddRangeAsync(activeOrder, newOrder);
            await db.SaveChangesAsync();

            lk.Setup(l => l.TryAcquireRiderLockAsync(
                    It.Is<string>(k => k.Contains("inject_lock")),
                    It.IsAny<string>(), It.IsAny<TimeSpan>()))
              .ReturnsAsync(true);
            lk.Setup(l => l.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(true);

            // Act
            var result = await svc.TryInjectOrderAsync(newOrder.Id);

            // Assert
            Assert.False(result, "TryInjectOrderAsync should return false when MATCHING transition fails.");

            // BatchGroupId must be cleaned up — Ghost Batch prevention
            var reloaded = await db.Orders.FindAsync(newOrder.Id);
            Assert.Null(reloaded!.BatchGroupId);
            Assert.Equal(0, reloaded.BatchSequence);
        }

        [Fact]
        public async Task TryInjectOrderAsync_WhenOrderAlreadyMATCHING_ShouldSkipMATCHINGTransition()
        {
            // Arrange — order is already MATCHING (e.g. re-dispatch scenario)
            var (svc, db, sm, lk) = BuildSut(
                smSetup: s =>
                {
                    s.Setup(x => x.TransitionOrderAsync(It.IsAny<Order>(), It.IsAny<OrderState>()))
                     .ReturnsAsync(true);
                    s.Setup(x => x.TransitionRiderAsync(It.IsAny<Rider>(), It.IsAny<RiderState>()))
                     .ReturnsAsync(true);
                });

            var rider = MakeRider("rider_sm_already");
            var activeOrder = MakeOrder("active_03", "shop_C", OrderState.ASSIGNED);
            activeOrder.AssignedRiderId = rider.Id;
            var newOrder = MakeOrder("new_03", "shop_C", OrderState.MATCHING); // already MATCHING

            await db.Riders.AddAsync(rider);
            await db.Orders.AddRangeAsync(activeOrder, newOrder);
            await db.SaveChangesAsync();

            lk.Setup(l => l.TryAcquireRiderLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
              .ReturnsAsync(true);
            lk.Setup(l => l.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(true);

            // Act
            await svc.TryInjectOrderAsync(newOrder.Id);

            // Assert — MATCHING must NOT be called again for an already-MATCHING order
            sm.Verify(s => s.TransitionOrderAsync(
                It.Is<Order>(o => o.Id == newOrder.Id),
                It.Is<OrderState>(st => st == OrderState.MATCHING)),
                Times.Never,
                "MATCHING transition must not be called when order is already in MATCHING state.");
        }

        // ═════════════════════════════════════════════════════════════════
        // Issue #1 — Injection lock: concurrent injects to the same BUSY
        //            rider must be serialised via Redis SETNX (inject_lock)
        // ═════════════════════════════════════════════════════════════════

        [Fact]
        public async Task TryInjectOrderAsync_InjectionLockKey_MustContainRiderId()
        {
            // Arrange — capture the lock key that is actually used
            var (svc, db, sm, lk) = BuildSut(
                smSetup: s =>
                {
                    s.Setup(x => x.TransitionOrderAsync(It.IsAny<Order>(), It.IsAny<OrderState>()))
                     .ReturnsAsync(true);
                    s.Setup(x => x.TransitionRiderAsync(It.IsAny<Rider>(), It.IsAny<RiderState>()))
                     .ReturnsAsync(true);
                });

            var riderId = "rider_lock_check";
            var rider   = MakeRider(riderId);
            var ao      = MakeOrder("a_04", "shop_D", OrderState.ASSIGNED);
            ao.AssignedRiderId = riderId;
            var newOrder = MakeOrder("n_04", "shop_D");

            await db.Riders.AddAsync(rider);
            await db.Orders.AddRangeAsync(ao, newOrder);
            await db.SaveChangesAsync();

            string? capturedKey = null;
            lk.Setup(l => l.TryAcquireRiderLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
              .Callback<string, string, TimeSpan>((key, _, _) => capturedKey = key)
              .ReturnsAsync(true);
            lk.Setup(l => l.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(true);

            // Act
            await svc.TryInjectOrderAsync(newOrder.Id);

            // Assert — lock key must identify both the rider and be scope-specific
            Assert.NotNull(capturedKey);
            Assert.Contains(riderId, capturedKey);
            Assert.Contains("inject_lock", capturedKey);
        }

        [Fact]
        public async Task TryInjectOrderAsync_InjectionLock_TTLMustBeThreeSeconds()
        {
            // Arrange — PO requirement: TTL ≤ 3s to minimise deadlock risk
            var (svc, db, sm, lk) = BuildSut(
                smSetup: s =>
                {
                    s.Setup(x => x.TransitionOrderAsync(It.IsAny<Order>(), It.IsAny<OrderState>()))
                     .ReturnsAsync(true);
                    s.Setup(x => x.TransitionRiderAsync(It.IsAny<Rider>(), It.IsAny<RiderState>()))
                     .ReturnsAsync(true);
                });

            var rider    = MakeRider("rider_ttl");
            var ao       = MakeOrder("a_05", "shop_E", OrderState.ASSIGNED);
            ao.AssignedRiderId = rider.Id;
            var newOrder = MakeOrder("n_05", "shop_E");

            await db.Riders.AddAsync(rider);
            await db.Orders.AddRangeAsync(ao, newOrder);
            await db.SaveChangesAsync();

            TimeSpan? capturedTtl = null;
            lk.Setup(l => l.TryAcquireRiderLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
              .Callback<string, string, TimeSpan>((_, _, ttl) => capturedTtl = ttl)
              .ReturnsAsync(true);
            lk.Setup(l => l.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(true);

            // Act
            await svc.TryInjectOrderAsync(newOrder.Id);

            // Assert — TTL must be ≤ 3 seconds as required by PO
            Assert.NotNull(capturedTtl);
            Assert.True(capturedTtl!.Value.TotalSeconds <= 3,
                $"Injection lock TTL must be ≤ 3s but was {capturedTtl.Value.TotalSeconds}s.");
        }

        [Fact]
        public async Task TryInjectOrderAsync_WhenLockAlreadyHeld_ShouldSkipRiderAndContinue()
        {
            // Arrange — first rider is already locked (concurrent inject in progress)
            var (svc, db, sm, lk) = BuildSut();

            var lockedRider  = MakeRider("rider_locked");
            var freeRider    = MakeRider("rider_free");

            var aoLocked = MakeOrder("ao_locked", "shop_F", OrderState.ASSIGNED);
            aoLocked.AssignedRiderId = lockedRider.Id;

            var aoFree = MakeOrder("ao_free", "shop_F", OrderState.ASSIGNED);
            aoFree.AssignedRiderId = freeRider.Id;

            var newOrder = MakeOrder("n_06", "shop_F");

            await db.Riders.AddRangeAsync(lockedRider, freeRider);
            await db.Orders.AddRangeAsync(aoLocked, aoFree, newOrder);
            await db.SaveChangesAsync();

            sm.Setup(s => s.TransitionOrderAsync(It.IsAny<Order>(), It.IsAny<OrderState>()))
              .ReturnsAsync(true);
            sm.Setup(s => s.TransitionRiderAsync(It.IsAny<Rider>(), It.IsAny<RiderState>()))
              .ReturnsAsync(true);

            // Only the free rider's lock can be acquired
            lk.Setup(l => l.TryAcquireRiderLockAsync(
                    It.Is<string>(k => k.Contains(lockedRider.Id)),
                    It.IsAny<string>(), It.IsAny<TimeSpan>()))
              .ReturnsAsync(false);  // locked
            lk.Setup(l => l.TryAcquireRiderLockAsync(
                    It.Is<string>(k => k.Contains(freeRider.Id)),
                    It.IsAny<string>(), It.IsAny<TimeSpan>()))
              .ReturnsAsync(true);
            lk.Setup(l => l.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(true);

            // Act
            var result = await svc.TryInjectOrderAsync(newOrder.Id);

            // Assert — must succeed via the free rider, not fail because first rider was locked
            Assert.True(result, "TryInjectOrderAsync must succeed by falling through to a non-locked rider.");
        }

        [Fact]
        public async Task TryInjectOrderAsync_LockMustBeReleasedInFinally_EvenOnFailure()
        {
            // Arrange — TryOfferToRiderAsync will fail (OFFERING transition fails)
            var (svc, db, sm, lk) = BuildSut(
                smSetup: s =>
                {
                    // MATCHING succeeds but OFFERING fails
                    s.Setup(x => x.TransitionOrderAsync(It.IsAny<Order>(),
                           It.Is<OrderState>(st => st == OrderState.MATCHING)))
                     .ReturnsAsync(true);
                    s.Setup(x => x.TransitionOrderAsync(It.IsAny<Order>(),
                           It.Is<OrderState>(st => st == OrderState.OFFERING)))
                     .ReturnsAsync(false);
                    s.Setup(x => x.TransitionRiderAsync(It.IsAny<Rider>(), It.IsAny<RiderState>()))
                     .ReturnsAsync(true);
                });

            var rider    = MakeRider("rider_release");
            var ao       = MakeOrder("ao_rel", "shop_G", OrderState.ASSIGNED);
            ao.AssignedRiderId = rider.Id;
            var newOrder = MakeOrder("n_07", "shop_G");

            await db.Riders.AddAsync(rider);
            await db.Orders.AddRangeAsync(ao, newOrder);
            await db.SaveChangesAsync();

            lk.Setup(l => l.TryAcquireRiderLockAsync(
                    It.Is<string>(k => k.Contains("inject_lock")),
                    It.IsAny<string>(), It.IsAny<TimeSpan>()))
              .ReturnsAsync(true);
            lk.Setup(l => l.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
              .ReturnsAsync(true);

            // Act
            await svc.TryInjectOrderAsync(newOrder.Id);

            // Assert — ReleaseLockAsync must be called exactly once regardless of inner failure
            lk.Verify(l => l.ReleaseLockAsync(
                It.Is<string>(k => k.Contains("inject_lock")),
                It.IsAny<string>()), Times.Once,
                "Injection lock must be released in finally block even when the offer fails.");
        }

        // ═════════════════════════════════════════════════════════════════
        // Issue #2 — OrderStateRules: verify the rule table itself is correct
        //            (prevents future regressions if someone edits the table)
        // ═════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(OrderState.CREATED,  OrderState.MATCHING,  true)]
        [InlineData(OrderState.MATCHING, OrderState.OFFERING,  true)]
        [InlineData(OrderState.CREATED,  OrderState.OFFERING,  false)] // the forbidden transition
        [InlineData(OrderState.OFFERING, OrderState.ASSIGNED,  true)]
        [InlineData(OrderState.OFFERING, OrderState.MATCHING,  true)]  // re-dispatch
        [InlineData(OrderState.ASSIGNED, OrderState.PICKING_UP, true)]
        [InlineData(OrderState.PICKING_UP, OrderState.DELIVERING, true)]
        [InlineData(OrderState.DELIVERING, OrderState.COMPLETED, true)]
        public void OrderStateRules_ValidTransitions_ShouldMatchSpec(
            OrderState from, OrderState to, bool expected)
        {
            // Act
            var result = OrderStateRules.IsValidTransition(from, to);

            // Assert
            Assert.Equal(expected, result);
        }

        // ═════════════════════════════════════════════════════════════════
        // Issue #1 — Injection guard: order must not be injected when it's
        //            not CREATED or MATCHING
        // ═════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(OrderState.OFFERING)]
        [InlineData(OrderState.ASSIGNED)]
        [InlineData(OrderState.DELIVERING)]
        [InlineData(OrderState.COMPLETED)]
        [InlineData(OrderState.CANCELLED)]
        public async Task TryInjectOrderAsync_WhenOrderNotCreatedOrMatching_ShouldReturnFalse(
            OrderState invalidState)
        {
            var (svc, db, _, _) = BuildSut();

            var order = MakeOrder($"n_{invalidState}", "shop_H", invalidState);
            await db.Orders.AddAsync(order);
            await db.SaveChangesAsync();

            // Act
            var result = await svc.TryInjectOrderAsync(order.Id);

            // Assert
            Assert.False(result,
                $"TryInjectOrderAsync must return false for order in state {invalidState}.");
        }
    }
}


