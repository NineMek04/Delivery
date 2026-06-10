using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BackendApi.Hubs;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Security;
using BackendApi.Services.Dispatch;
using BackendApi.Services.Telemetry;
using BackendApi.Services.Tracking;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BackendApi.UnitTests.Hubs
{
    /// <summary>
    /// Security tests for TrackingHub.OnConnectedAsync.
    ///
    /// Bug #1 fix: anonymous loopback bypass removed.
    /// Any connection without valid JWT claims (role + userId) must be aborted,
    /// regardless of the remote IP address.
    /// </summary>
    public class TrackingHubSecurityTests
    {
        // ── Fake HubCallerContext ──────────────────────────────────────────
        // Moq cannot mock GetHttpContext() because it is a non-virtual extension
        // method.  We use a concrete subclass with a real FeatureCollection so
        // that the extension method resolves correctly at runtime.

        private sealed class FakeHubCallerContext : HubCallerContext
        {
            private readonly ClaimsPrincipal _user;
            private readonly FeatureCollection _features = new FeatureCollection();

            public bool AbortCalled { get; private set; }

            public FakeHubCallerContext(ClaimsPrincipal user)
            {
                _user = user;
            }

            public override string ConnectionId     => "fake-conn-id";
            public override string? UserIdentifier  => null;
            public override ClaimsPrincipal User    => _user;
            public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
            public override CancellationToken ConnectionAborted => CancellationToken.None;
            public override IFeatureCollection Features => _features;
            public override void Abort() => AbortCalled = true;
        }

        // ── Factory helpers ───────────────────────────────────────────────

        private static FakeHubCallerContext MakeContext(string? role, string? userId)
        {
            var claims = new List<Claim>();
            if (role   != null) claims.Add(new Claim(ClaimTypes.Role,           role));
            if (userId != null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            return new FakeHubCallerContext(principal);
        }

        private static TrackingHub MakeHub(
            FakeHubCallerContext ctx,
            Mock<IGroupManager> groups,
            Mock<IRiderPresenceManager> presence,
            Mock<ILogger<TrackingHub>> logger,
            Mock<IHubCallerClients>? clients = null)
        {
            var hub = new TrackingHub(
                presence.Object,
                new Mock<DispatchService>(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!).Object,
                new Mock<DispatchOfferHandler>(null!, null!, null!, null!, null!, null!, null!).Object,
                new Mock<IConfiguration>().Object,
                new Mock<IEventBus>().Object,
                new Mock<TelemetryAggregator>().Object,
                new Mock<TelemetryService>(null!, null!, null!, null!, null!, null!, null!, null!, null!).Object,
                logger.Object
            );
            hub.Context = ctx;
            hub.Groups  = groups.Object;
            if (clients != null) hub.Clients = clients.Object;
            return hub;
        }

        // ── No JWT — always abort ──────────────────────────────────────────

        [Fact]
        public async Task OnConnectedAsync_WithNoJwtClaims_ShouldCallAbort()
        {
            // Arrange
            var ctx  = MakeContext(null, null);
            var grps = new Mock<IGroupManager>();
            var hub  = MakeHub(ctx, grps, new Mock<IRiderPresenceManager>(), new Mock<ILogger<TrackingHub>>());

            // Act
            await hub.OnConnectedAsync();

            // Assert
            Assert.True(ctx.AbortCalled, "Abort() must be called for unauthenticated connections.");
            grps.Verify(g => g.AddToGroupAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task OnConnectedAsync_WithRoleButNoUserId_ShouldCallAbort()
        {
            // Connection has role claim but missing userId — still invalid
            var ctx  = MakeContext(AuthConstants.AdminRole, null);
            var grps = new Mock<IGroupManager>();
            var hub  = MakeHub(ctx, grps, new Mock<IRiderPresenceManager>(), new Mock<ILogger<TrackingHub>>());

            await hub.OnConnectedAsync();

            Assert.True(ctx.AbortCalled);
        }

        [Fact]
        public async Task OnConnectedAsync_WithUserIdButNoRole_ShouldCallAbort()
        {
            // Connection has userId but missing role claim — still invalid
            var ctx  = MakeContext(null, Guid.NewGuid().ToString());
            var grps = new Mock<IGroupManager>();
            var hub  = MakeHub(ctx, grps, new Mock<IRiderPresenceManager>(), new Mock<ILogger<TrackingHub>>());

            await hub.OnConnectedAsync();

            Assert.True(ctx.AbortCalled);
        }

        [Fact]
        public async Task OnConnectedAsync_WithNoJwtClaims_MustNotJoinAnyGroup()
        {
            // After the Bug #1 fix: unauthenticated connections must NEVER be added
            // to "admins" or any other group, regardless of source IP.
            var ctx  = MakeContext(null, null);
            var grps = new Mock<IGroupManager>();
            var hub  = MakeHub(ctx, grps, new Mock<IRiderPresenceManager>(), new Mock<ILogger<TrackingHub>>());

            await hub.OnConnectedAsync();

            grps.Verify(g => g.AddToGroupAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task OnConnectedAsync_WithNoJwtClaims_MustNotJoinAdminGroup()
        {
            // Specific assertion that "admins" group is never granted without a valid JWT
            var ctx  = MakeContext(null, null);
            var grps = new Mock<IGroupManager>();
            var hub  = MakeHub(ctx, grps, new Mock<IRiderPresenceManager>(), new Mock<ILogger<TrackingHub>>());

            await hub.OnConnectedAsync();

            grps.Verify(g => g.AddToGroupAsync(
                It.IsAny<string>(),
                It.Is<string>(g => g == "admins"),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        // ── Valid JWT — correct group routing ─────────────────────────────

        [Fact]
        public async Task OnConnectedAsync_WithAdminRole_ShouldJoinAdminGroupAndNotAbort()
        {
            var ctx     = MakeContext(AuthConstants.AdminRole, Guid.NewGuid().ToString());
            var grps    = new Mock<IGroupManager>();
            var clients = new Mock<IHubCallerClients>();
            grps.Setup(g => g.AddToGroupAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(new Mock<IClientProxy>().Object);

            var hub = MakeHub(ctx, grps, new Mock<IRiderPresenceManager>(), new Mock<ILogger<TrackingHub>>(), clients);

            await hub.OnConnectedAsync();

            Assert.False(ctx.AbortCalled);
            grps.Verify(g => g.AddToGroupAsync(
                It.IsAny<string>(), "admins", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_WithDispatcherRole_ShouldJoinAdminGroupAndNotAbort()
        {
            var ctx     = MakeContext(AuthConstants.DispatcherRole, Guid.NewGuid().ToString());
            var grps    = new Mock<IGroupManager>();
            var clients = new Mock<IHubCallerClients>();
            grps.Setup(g => g.AddToGroupAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns(new Mock<IClientProxy>().Object);

            var hub = MakeHub(ctx, grps, new Mock<IRiderPresenceManager>(), new Mock<ILogger<TrackingHub>>(), clients);

            await hub.OnConnectedAsync();

            Assert.False(ctx.AbortCalled);
            grps.Verify(g => g.AddToGroupAsync(
                It.IsAny<string>(), "admins", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_WithUnknownRole_ShouldNotJoinAnyGroupAndNotAbort()
        {
            // Unknown but authenticated role — connection is not aborted but no group is joined
            var ctx  = MakeContext("UnknownRole", Guid.NewGuid().ToString());
            var grps = new Mock<IGroupManager>();
            var hub  = MakeHub(ctx, grps, new Mock<IRiderPresenceManager>(), new Mock<ILogger<TrackingHub>>());

            await hub.OnConnectedAsync();

            Assert.False(ctx.AbortCalled);
            grps.Verify(g => g.AddToGroupAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ── Logging ───────────────────────────────────────────────────────

        [Fact]
        public async Task OnConnectedAsync_WhenNoJwt_ShouldLogWarning()
        {
            var ctx    = MakeContext(null, null);
            var grps   = new Mock<IGroupManager>();
            var logger = new Mock<ILogger<TrackingHub>>();
            var hub    = MakeHub(ctx, grps, new Mock<IRiderPresenceManager>(), logger);

            await hub.OnConnectedAsync();

            logger.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }
    }
}
