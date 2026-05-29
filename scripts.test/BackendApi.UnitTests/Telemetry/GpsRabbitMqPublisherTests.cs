using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
        private readonly TestableGpsRabbitMqPublisher _publisher;

        public GpsRabbitMqPublisherTests()
        {
            _configMock = new Mock<IConfiguration>();
            _loggerMock = new Mock<ILogger<GpsRabbitMqPublisher>>();
            _connectionMock = new Mock<IConnection>();
            _channelMock = new Mock<IModel>();

            // Setup default config values
            _configMock.Setup(c => c["MessageBroker:Host"]).Returns("localhost");
            _configMock.Setup(c => c["MessageBroker:Port"]).Returns("5672");

            // Setup mock connection to return mock channel
            _connectionMock.Setup(c => c.CreateModel()).Returns(_channelMock.Object);
            _connectionMock.Setup(c => c.IsOpen).Returns(true);
            _channelMock.Setup(c => c.IsOpen).Returns(true);

            _publisher = new TestableGpsRabbitMqPublisher(
                _configMock.Object,
                _loggerMock.Object,
                _connectionMock.Object
            );
        }

        [Fact]
        public void Publish_Should_DeclareQueueAndBasicPublishPersistentMessage()
        {
            // Arrange
            var point = new TrackPoint("rider_123", 13.7563, 100.5018, DateTime.UtcNow);

            var basicPropertiesMock = new Mock<IBasicProperties>();
            _channelMock.Setup(c => c.CreateBasicProperties()).Returns(basicPropertiesMock.Object);

            // Act
            _publisher.Publish(point);

            // Assert
            // 1. Ensure queue declared with correct parameters
            _channelMock.Verify(c => c.QueueDeclare(
                "gps_telemetry_queue",
                true, // durable
                false, // exclusive
                false, // autoDelete
                null // arguments
            ), Times.Once);

            // 2. Ensure persistent property is set
            basicPropertiesMock.VerifySet(p => p.Persistent = true, Times.Once);
            basicPropertiesMock.VerifySet(p => p.Type = nameof(TrackPoint), Times.Once);

            // 3. Ensure publish called with correct exchange, queue, and payload
            _channelMock.Verify(c => c.BasicPublish(
                "", // exchange
                "gps_telemetry_queue", // routingKey
                true, // mandatory
                basicPropertiesMock.Object,
                It.Is<ReadOnlyMemory<byte>>(body => VerifySerializedPoint(body, point))
            ), Times.Once);
        }

        [Fact]
        public void PendingQueueCount_Should_ReturnMessageCountFromChannel()
        {
            // Arrange
            uint expectedCount = 145;
            _channelMock.Setup(c => c.MessageCount("gps_telemetry_queue")).Returns(expectedCount);

            // Act
            int pendingCount = _publisher.PendingQueueCount;

            // Assert
            Assert.Equal((int)expectedCount, pendingCount);
            _channelMock.Verify(c => c.MessageCount("gps_telemetry_queue"), Times.Once);
        }

        [Fact]
        public void PendingQueueCount_WhenExceptionOccurs_ShouldReturnZeroGracefully()
        {
            // Arrange
            _channelMock.Setup(c => c.MessageCount("gps_telemetry_queue")).Throws(new Exception("RabbitMQ dead"));

            // Act
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
                IConnection mockConnection) : base(configuration, logger)
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
