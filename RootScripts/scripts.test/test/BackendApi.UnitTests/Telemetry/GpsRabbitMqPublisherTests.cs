using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Moq;
using RabbitMQ.Client;
using Xunit;
using BackendApi.Features.FleetTracking.Telemetry;

namespace BackendApi.UnitTests.Telemetry
{
    public class GpsRabbitMqPublisherTests
    {
        private readonly Mock<IConfiguration> _configMock;
        private readonly Mock<ILogger<GpsRabbitMqPublisher>> _loggerMock;
        private readonly Mock<IConnection> _connectionMock;
        private readonly Mock<IModel> _channelMock;
        private readonly Mock<IHostApplicationLifetime> _appLifetimeMock;
        private readonly TestableGpsRabbitMqPublisher _publisher;

        public GpsRabbitMqPublisherTests()
        {
            _configMock = new Mock<IConfiguration>();
            _loggerMock = new Mock<ILogger<GpsRabbitMqPublisher>>();
            _connectionMock = new Mock<IConnection>();
            _channelMock = new Mock<IModel>();
            _appLifetimeMock = new Mock<IHostApplicationLifetime>();

            // Setup default config values
            _configMock.Setup(c => c["MessageBroker:Host"]).Returns("localhost");
            _configMock.Setup(c => c["MessageBroker:Port"]).Returns("5672");
            _configMock.Setup(c => c["MessageBroker:Username"]).Returns("test-user");
            _configMock.Setup(c => c["MessageBroker:Password"]).Returns("test-password");

            // Setup application stopping token mock to prevent null refs
            var cancellationTokenSource = new CancellationTokenSource();
            _appLifetimeMock.Setup(a => a.ApplicationStopping).Returns(cancellationTokenSource.Token);

            // Setup mock connection to return mock channel
            _connectionMock.Setup(c => c.CreateModel()).Returns(_channelMock.Object);
            _connectionMock.Setup(c => c.IsOpen).Returns(true);
            _channelMock.Setup(c => c.IsOpen).Returns(true);

            _publisher = new TestableGpsRabbitMqPublisher(
                _configMock.Object,
                _loggerMock.Object,
                _appLifetimeMock.Object,
                _connectionMock.Object
            );
        }

        [Fact]
        public async Task Publish_Should_DeclareQueueAndBasicPublishPersistentMessage()
        {
            // Arrange
            var point = new TrackPoint("rider_123", 13.7563, 100.5018, DateTime.UtcNow);

            var basicPropertiesMock = new Mock<IBasicProperties>();
            _channelMock.Setup(c => c.CreateBasicProperties()).Returns(basicPropertiesMock.Object);

            var batchMock = new Mock<IBasicPublishBatch>();
            _channelMock.Setup(c => c.CreateBasicPublishBatch()).Returns(batchMock.Object);

            // Act
            _publisher.Publish(point);

            // Wait briefly for the background channel worker to process the message
            await Task.Delay(1000);

            // Assert
            // 1. Ensure queue declared with correct parameters
            _channelMock.Verify(c => c.QueueDeclare(
                "gps_telemetry_queue",
                true, // durable
                false, // exclusive
                false, // autoDelete
                It.IsAny<IDictionary<string, object>>() // arguments
            ), Times.AtLeastOnce);

            // 2. Ensure persistent property is set
            basicPropertiesMock.VerifySet(p => p.Persistent = true, Times.AtLeastOnce);
            basicPropertiesMock.VerifySet(p => p.Type = nameof(TrackPoint), Times.AtLeastOnce);

            // 3. Ensure batch publishing was used with correct parameters
#pragma warning disable CS0618 // RabbitMQ 6.x exposes the mockable interface overload as byte[].
            batchMock.Verify(b => b.Add(
                "", // exchange
                "gps_telemetry_queue", // routingKey
                true, // mandatory
                basicPropertiesMock.Object,
                It.IsAny<byte[]>()
            ), Times.AtLeastOnce);
#pragma warning restore CS0618

            batchMock.Verify(b => b.Publish(), Times.AtLeastOnce);
        }

        [Fact]
        public async Task PendingQueueCount_Should_ReturnMessageCountFromChannel()
        {
            // Arrange
            uint expectedCount = 145;
            _channelMock.Setup(c => c.MessageCount("gps_telemetry_queue")).Returns(expectedCount);

            // Act: First call triggers background fetch
            int initialCount = _publisher.PendingQueueCount;

            // Wait briefly for background Task.Run to query the channel and cache the value
            await Task.Delay(1000);

            // Second call retrieves cached value
            int pendingCount = _publisher.PendingQueueCount;

            // Assert
            Assert.Equal((int)expectedCount, pendingCount);
            _channelMock.Verify(c => c.MessageCount("gps_telemetry_queue"), Times.AtLeastOnce);
        }

        [Fact]
        public async Task PendingQueueCount_WhenExceptionOccurs_ShouldReturnZeroGracefully()
        {
            // Arrange
            _channelMock.Setup(c => c.MessageCount("gps_telemetry_queue")).Throws(new Exception("RabbitMQ dead"));

            // Act
            int initialCount = _publisher.PendingQueueCount;
            await Task.Delay(1000);
            int pendingCount = _publisher.PendingQueueCount;

            // Assert
            Assert.Equal(0, pendingCount);
        }

        private static bool VerifySerializedPoint(ReadOnlyMemory<byte> body, TrackPoint originalPoint)
        {
            var json = Encoding.UTF8.GetString(body.Span);
            var deserialized = JsonSerializer.Deserialize<TrackPoint>(json);
            return deserialized != null &&
                   deserialized.RiderId == originalPoint.RiderId &&
                   deserialized.Lat == originalPoint.Lat &&
                   deserialized.Lng == originalPoint.Lng;
        }

        // Subclass to inject Mock Connection and bypass actual socket creation
        private class TestableGpsRabbitMqPublisher : GpsRabbitMqPublisher
        {
            private readonly IConnection _mockConnection;

            public TestableGpsRabbitMqPublisher(
                IConfiguration configuration,
                ILogger<GpsRabbitMqPublisher> logger,
                IHostApplicationLifetime appLifetime,
                IConnection mockConnection) : base(configuration, logger, appLifetime)
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
