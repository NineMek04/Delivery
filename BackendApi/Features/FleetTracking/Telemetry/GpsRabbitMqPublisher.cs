using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace BackendApi.Features.FleetTracking.Telemetry
{
    /// <summary>
    /// Raw RabbitMQ Publisher for GPS Telemetry.
    /// Manages a dedicated, thread-safe connection and channel to publish
    /// persistent telemetry messages to the durable queue 'gps_telemetry_queue'.
    /// </summary>
    public class GpsRabbitMqPublisher : IDisposable
    {
        private const string QueueName = "gps_telemetry_queue";
        private readonly IConfiguration _configuration;
        private readonly ILogger<GpsRabbitMqPublisher> _logger;
        private IConnection? _connection;
        private IModel? _channel;
        private readonly object _connectionLock = new();
        private bool _disposed;

        public GpsRabbitMqPublisher(IConfiguration configuration, ILogger<GpsRabbitMqPublisher> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        private void EnsureConnection()
        {
            if (_connection is { IsOpen: true } && _channel is { IsOpen: true }) return;

            lock (_connectionLock)
            {
                if (_connection is { IsOpen: true } && _channel is { IsOpen: true }) return;

                // Close/dispose existing if partially open
                _channel?.Dispose();
                _connection?.Dispose();

                var host = _configuration["MessageBroker:Host"] ?? _configuration["MessageBroker__Host"] ?? "localhost";
                var portStr = _configuration["MessageBroker:Port"] ?? _configuration["MessageBroker__Port"] ?? "5672";
                var username = _configuration["MessageBroker:Username"] ?? _configuration["MessageBroker__Username"] ?? "guest";
                var password = _configuration["MessageBroker:Password"] ?? _configuration["MessageBroker__Password"] ?? "guest";

                int.TryParse(portStr, out var port);

                var factory = new ConnectionFactory
                {
                    HostName = host,
                    Port = port == 0 ? 5672 : port,
                    UserName = username,
                    Password = password,
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                    TopologyRecoveryEnabled = true
                };

                const int maxRetries = 3;
                var retryCount = 0;
                var connected = false;

                while (!connected && retryCount < maxRetries)
                {
                    try
                    {
                        retryCount++;
                        _logger.LogInformation("GpsRabbitMqPublisher connecting to RabbitMQ Host: {Host}:{Port} (Attempt {Attempt}/{MaxRetries})", host, port, retryCount, maxRetries);
                        _connection = CreateConnection(factory);
                        connected = true;
                    }
                    catch (Exception ex)
                    {
                        if (retryCount >= maxRetries)
                        {
                            _logger.LogCritical(ex, "GpsRabbitMqPublisher failed to connect to RabbitMQ broker after {MaxRetries} attempts.", maxRetries);
                            throw;
                        }

                        var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                        _logger.LogWarning("GpsRabbitMqPublisher: RabbitMQ unreachable. Retrying in {Delay}s... Error: {Message}", delay.TotalSeconds, ex.Message);
                        Thread.Sleep(delay);
                    }
                }

                _channel = _connection!.CreateModel();

                // Declare Durable Queue
                _channel.QueueDeclare(
                    queue: QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null
                );

                _logger.LogInformation("GpsRabbitMqPublisher successfully connected to RabbitMQ and declared queue '{QueueName}'", QueueName);
            }
        }

        /// <summary>
        /// Factory method for creating RabbitMQ connections. Virtual to support unit testing via subclassing.
        /// </summary>
        protected virtual IConnection CreateConnection(ConnectionFactory factory)
        {
            return factory.CreateConnection();
        }

        /// <summary>
        /// Gets the current number of pending messages in the GPS RabbitMQ queue.
        /// Useful for calculating backpressure and dynamic rate limits.
        /// </summary>
        public int PendingQueueCount
        {
            get
            {
                try
                {
                    EnsureConnection();
                    lock (_connectionLock)
                    {
                        if (_channel != null)
                        {
                            return (int)_channel.MessageCount(QueueName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to retrieve message count for RabbitMQ queue '{QueueName}'.", QueueName);
                }
                return 0;
            }
        }

        /// <summary>
        /// Publishes a GPS TrackPoint to RabbitMQ as a persistent message.
        /// </summary>
        public void Publish(TrackPoint point)
        {
            EnsureConnection();

            var message = JsonSerializer.Serialize(point);
            var body = Encoding.UTF8.GetBytes(message);

            lock (_connectionLock)
            {
                if (_channel == null)
                {
                    throw new InvalidOperationException("RabbitMQ channel is not initialized.");
                }

                var properties = _channel.CreateBasicProperties();
                properties.Persistent = true; // Durable (persist to disk)
                properties.Type = nameof(TrackPoint);

                _channel.BasicPublish(
                    exchange: "",
                    routingKey: QueueName,
                    mandatory: true,
                    basicProperties: properties,
                    body: body
                );
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_connectionLock)
            {
                _channel?.Dispose();
                _connection?.Dispose();
            }
            _disposed = true;
        }
    }
}
