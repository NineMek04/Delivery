using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;
using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Services;
using BackendApi.Services.Auth;
using BackendApi.Services.Notifications;
using BackendApi.Services.Orders;
using BackendApi.Features.FleetTracking.Telemetry;
using BackendApi.Services.Telemetry;

namespace BackendApi.UnitTests.Telemetry
{
    public class GpsRabbitMqConsumerWorkerTests
    {
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
        private readonly Mock<IServiceScope> _scopeMock;
        private readonly Mock<IServiceProvider> _scopeServiceProviderMock;
        
        private readonly Mock<IConfiguration> _configMock;
        private readonly Mock<IHostApplicationLifetime> _appLifetimeMock;
        private readonly Mock<ILogger<GpsRabbitMqConsumerWorker>> _loggerMock;
        private readonly Mock<IConnection> _connectionMock;
        private readonly Mock<IModel> _channelMock;
        private readonly Mock<GpsHistoryService> _gpsHistoryServiceMock;
        private readonly Mock<ApplicationDbContext> _dbContextMock;
        private readonly Mock<DatabaseFacade> _databaseFacadeMock;
        private readonly Mock<IDbContextTransaction> _dbTransactionMock;

        public GpsRabbitMqConsumerWorkerTests()
        {
            _serviceProviderMock = new Mock<IServiceProvider>();
            _scopeFactoryMock = new Mock<IServiceScopeFactory>();
            _scopeMock = new Mock<IServiceScope>();
            _scopeServiceProviderMock = new Mock<IServiceProvider>();
            
            _configMock = new Mock<IConfiguration>();
            _appLifetimeMock = new Mock<IHostApplicationLifetime>();
            _loggerMock = new Mock<ILogger<GpsRabbitMqConsumerWorker>>();
            _connectionMock = new Mock<IConnection>();
            _channelMock = new Mock<IModel>();
            
            // Mock GpsHistoryService by passing null dependencies (since we mock SavePointsAsync anyway)
            _gpsHistoryServiceMock = new Mock<GpsHistoryService>(null!, null!);

            // Setup DB Context with In-Memory Database and DatabaseFacade Mock for Transactions
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var currentUserServiceMock = new Mock<ICurrentUserService>();
            currentUserServiceMock.Setup(u => u.UserId).Returns(Guid.NewGuid());
            currentUserServiceMock.Setup(u => u.UserName).Returns("System");

            _dbContextMock = new Mock<ApplicationDbContext>(options, currentUserServiceMock.Object) { CallBase = true };
            _databaseFacadeMock = new Mock<DatabaseFacade>(_dbContextMock.Object);
            _dbTransactionMock = new Mock<IDbContextTransaction>();

            _dbContextMock.Setup(c => c.Database).Returns(_databaseFacadeMock.Object);
            _databaseFacadeMock.Setup(d => d.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                               .ReturnsAsync(_dbTransactionMock.Object);

            // Setup IConfiguration values
            _configMock.Setup(c => c["MessageBroker:Host"]).Returns("localhost");
            _configMock.Setup(c => c["MessageBroker:Port"]).Returns("5672");
            _configMock.Setup(c => c["MessageBroker:Username"]).Returns("test-user");
            _configMock.Setup(c => c["MessageBroker:Password"]).Returns("test-password");

            // Setup DI Scoping
            _serviceProviderMock
                .Setup(s => s.GetService(typeof(IServiceScopeFactory)))
                .Returns(_scopeFactoryMock.Object);

            _scopeFactoryMock
                .Setup(sf => sf.CreateScope())
                .Returns(_scopeMock.Object);

            _scopeMock
                .Setup(s => s.ServiceProvider)
                .Returns(_scopeServiceProviderMock.Object);

            _scopeServiceProviderMock
                .Setup(s => s.GetService(typeof(GpsHistoryService)))
                .Returns(_gpsHistoryServiceMock.Object);

            _scopeServiceProviderMock
                .Setup(s => s.GetService(typeof(ApplicationDbContext)))
                .Returns(_dbContextMock.Object);

            // Setup Connection and Channel
            _connectionMock.Setup(c => c.CreateModel()).Returns(_channelMock.Object);
            _connectionMock.Setup(c => c.IsOpen).Returns(true);
            _channelMock.Setup(c => c.IsOpen).Returns(true);
        }

        [Fact]
        public async Task Worker_Should_ProcessBatchAndAckOnlyOnSuccessfulDatabaseSave()
        {
            // Arrange
            AsyncEventingBasicConsumer? capturedConsumer = null;

            // Capture the consumer when BasicConsume is registered
            _channelMock.Setup(c => c.BasicConsume(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object>?>(),
                It.IsAny<IBasicConsumer>()
            )).Callback((string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive, IDictionary<string, object>? args, IBasicConsumer consumer) =>
            {
                capturedConsumer = consumer as AsyncEventingBasicConsumer;
            }).Returns("consumer_tag");

            var worker = new TestableGpsRabbitMqConsumerWorker(
                _serviceProviderMock.Object,
                _configMock.Object,
                _appLifetimeMock.Object,
                _loggerMock.Object,
                _connectionMock.Object
            );

            // Setup history service mock behavior
            var saveCompletionSource = new TaskCompletionSource<bool>();
            _gpsHistoryServiceMock.Setup(g => g.SavePointsAsync(
                It.IsAny<List<TrackPoint>>(),
                It.IsAny<CancellationToken>()
            )).Callback<List<TrackPoint>, CancellationToken>((points, ct) =>
            {
                saveCompletionSource.SetResult(true);
            }).Returns(Task.CompletedTask);

            // Capture critical logs to diagnose initialization failure
            Exception? loggedException = null;
            _loggerMock.Setup(x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            )).Callback(new Action<LogLevel, EventId, object, Exception?, object>((level, id, state, ex, formatter) =>
            {
                loggedException = ex;
            }));

            // Start background worker
            using var cts = new CancellationTokenSource();
            var runTask = worker.StartAsync(cts.Token);

            // Wait for worker to initialize and capture consumer (avoid asynchronous race condition)
            int retries = 0;
            while (capturedConsumer == null && retries < 20)
            {
                await Task.Delay(50);
                retries++;
            }

            // Assert that consumer registration happened
            if (capturedConsumer == null && loggedException != null)
            {
                throw new InvalidOperationException("Worker failed to initialize.", loggedException);
            }
            Assert.NotNull(capturedConsumer);

            // Act - Simulate sending a GPS point from RabbitMQ
            var point = new TrackPoint("rider_789", 13.0, 100.0, DateTime.UtcNow);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(point));

            // Raise the event on consumer
            ulong deliveryTag = 999;
            await capturedConsumer.HandleBasicDeliver(
                "consumer_tag",
                deliveryTag,
                redelivered: false,
                exchange: "",
                routingKey: "gps_telemetry_queue",
                properties: _channelMock.Object.CreateBasicProperties(),
                body: body
            );

            // Wait for DB Save to be triggered (with timeout)
            var dbSaved = await Task.WhenAny(saveCompletionSource.Task, Task.Delay(5000)) == saveCompletionSource.Task;
            Assert.True(dbSaved, "Database save was not triggered in time.");

            // Cancel worker execution loop
            await cts.CancelAsync();
            await runTask;

            // Assert - Check that DB Save points count is 1 and Ack was called with correct tag
            _gpsHistoryServiceMock.Verify(g => g.SavePointsAsync(
                It.Is<List<TrackPoint>>(list => list.Count == 1 && list[0].RiderId == "rider_789"),
                It.IsAny<CancellationToken>()
            ), Times.Once);

            _channelMock.Verify(c => c.BasicAck(
                deliveryTag,
                true // multiple: true
            ), Times.Once);
        }

        [Fact]
        public async Task Worker_WhenDatabaseSaveFails_ShouldNotAckMessages()
        {
            // Arrange
            AsyncEventingBasicConsumer? capturedConsumer = null;

            _channelMock.Setup(c => c.BasicConsume(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object>?>(),
                It.IsAny<IBasicConsumer>()
            )).Callback((string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive, IDictionary<string, object>? args, IBasicConsumer consumer) =>
            {
                capturedConsumer = consumer as AsyncEventingBasicConsumer;
            }).Returns("consumer_tag");

            var worker = new TestableGpsRabbitMqConsumerWorker(
                _serviceProviderMock.Object,
                _configMock.Object,
                _appLifetimeMock.Object,
                _loggerMock.Object,
                _connectionMock.Object
            );

            // Simulate DB failure
            var dbFailCompletion = new TaskCompletionSource<bool>();
            _gpsHistoryServiceMock.Setup(g => g.SavePointsAsync(
                It.IsAny<List<TrackPoint>>(),
                It.IsAny<CancellationToken>()
            )).Callback<List<TrackPoint>, CancellationToken>((points, ct) =>
            {
                dbFailCompletion.SetResult(true);
            }).ThrowsAsync(new Exception("Database connection failure!"));

            using var cts = new CancellationTokenSource();
            var runTask = worker.StartAsync(cts.Token);

            // Wait for worker to initialize and capture consumer (avoid asynchronous race condition)
            int retries = 0;
            while (capturedConsumer == null && retries < 20)
            {
                await Task.Delay(50);
                retries++;
            }

            Assert.NotNull(capturedConsumer);

            var point = new TrackPoint("rider_fail", 14.0, 101.0, DateTime.UtcNow);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(point));

            ulong deliveryTag = 888;
            await capturedConsumer.HandleBasicDeliver(
                "consumer_tag",
                deliveryTag,
                redelivered: false,
                exchange: "",
                routingKey: "gps_telemetry_queue",
                properties: _channelMock.Object.CreateBasicProperties(),
                body: body
            );

            // Wait for DB Save attempt
            var dbAttempted = await Task.WhenAny(dbFailCompletion.Task, Task.Delay(5000)) == dbFailCompletion.Task;
            Assert.True(dbAttempted);

            await cts.CancelAsync();
            await runTask;

            // Assert - Verify DB Save was attempted but NO BasicAck was called due to the crash
            _gpsHistoryServiceMock.Verify(g => g.SavePointsAsync(It.IsAny<List<TrackPoint>>(), It.IsAny<CancellationToken>()), Times.Once);
            
            _channelMock.Verify(c => c.BasicAck(
                It.IsAny<ulong>(),
                It.IsAny<bool>()
            ), Times.Never);
        }

        // Subclass to inject Mock Connection and bypass actual socket creation
        private class TestableGpsRabbitMqConsumerWorker : GpsRabbitMqConsumerWorker
        {
            private readonly IConnection _mockConnection;

            public TestableGpsRabbitMqConsumerWorker(
                IServiceProvider serviceProvider,
                IConfiguration configuration,
                IHostApplicationLifetime appLifetime,
                ILogger<GpsRabbitMqConsumerWorker> logger,
                IConnection mockConnection) : base(serviceProvider, configuration, appLifetime, logger)
            {
                _mockConnection = mockConnection;
            }

            protected override IConnection CreateConnection(ConnectionFactory factory)
            {
                return _mockConnection;
            }
        }
    }
}


