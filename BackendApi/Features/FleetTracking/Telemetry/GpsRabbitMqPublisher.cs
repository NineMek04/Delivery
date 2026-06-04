using System;
using System.Collections.Generic;
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
        private const string SnapQueueName = "gps_snap_queue";
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
            _channelQueue = System.Threading.Channels.Channel.CreateBounded<TrackPoint>(
                new System.Threading.Channels.BoundedChannelOptions(10000)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
                });

            _snapChannelQueue = System.Threading.Channels.Channel.CreateBounded<TrackPoint>(
                new System.Threading.Channels.BoundedChannelOptions(10000)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
                });

            // Start the background publisher workers
            _ = System.Threading.Tasks.Task.Run(ProcessQueueAsync);
            _ = System.Threading.Tasks.Task.Run(ProcessSnapQueueAsync);
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

                var dlxName = "gps_telemetry_dlx";
                var dlqName = $"{QueueName}_dlq";
                var snapDlqName = $"{SnapQueueName}_dlq";

                // Declare Dead Letter Exchange
                _channel.ExchangeDeclare(dlxName, "direct", durable: true);

                // Declare Dead Letter Queues
                _channel.QueueDeclare(dlqName, durable: true, exclusive: false, autoDelete: false);
                _channel.QueueDeclare(snapDlqName, durable: true, exclusive: false, autoDelete: false);

                // Bind Dead Letter Queues to DLX
                _channel.QueueBind(dlqName, dlxName, QueueName);
                _channel.QueueBind(snapDlqName, dlxName, SnapQueueName);

                var queueArguments = new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", dlxName },
                    { "x-dead-letter-routing-key", QueueName }
                };

                var snapQueueArguments = new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", dlxName },
                    { "x-dead-letter-routing-key", SnapQueueName }
                };

                // Declare Durable Queue with DLX configuration
                try
                {
                    _channel.QueueDeclare(
                        queue: QueueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: queueArguments
                    );
                }
                catch (RabbitMQ.Client.Exceptions.OperationInterruptedException ex) when (ex.ShutdownReason.ReplyCode == 406)
                {
                    _logger.LogWarning("Queue {QueueName} has mismatched arguments. Deleting and recreating...", QueueName);
                    _channel.Dispose();
                    _channel = _connection.CreateModel();
                    _channel.QueueDelete(QueueName);
                    _channel.QueueDeclare(
                        queue: QueueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: queueArguments
                    );
                }

                try
                {
                    _channel.QueueDeclare(
                        queue: SnapQueueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: snapQueueArguments
                    );
                }
                catch (RabbitMQ.Client.Exceptions.OperationInterruptedException ex) when (ex.ShutdownReason.ReplyCode == 406)
                {
                    _logger.LogWarning("Queue {SnapQueueName} has mismatched arguments. Deleting and recreating...", SnapQueueName);
                    _channel.Dispose();
                    _channel = _connection.CreateModel();
                    _channel.QueueDelete(SnapQueueName);
                    _channel.QueueDeclare(
                        queue: SnapQueueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: snapQueueArguments
                    );
                }

                _logger.LogInformation("GpsRabbitMqPublisher successfully connected to RabbitMQ and declared queues '{QueueName}' (with DLQ) and '{SnapQueueName}' (with DLQ)", QueueName, SnapQueueName);
            }
        }

        /// <summary>
        /// Factory method for creating RabbitMQ connections. Virtual to support unit testing via subclassing.
        /// </summary>
        protected virtual IConnection CreateConnection(ConnectionFactory factory)
        {
            return factory.CreateConnection();
        }

        private int _cachedQueueCount = 0;
        private DateTime _lastQueueCountTime = DateTime.MinValue;

        /// <summary>
        /// Gets the current number of pending messages in the GPS RabbitMQ queue.
        /// Cached for 1 second to prevent blocking the SignalR thread with synchronous TCP calls.
        /// </summary>
        public int PendingQueueCount
        {
            get
            {
                var now = DateTime.UtcNow;
                if ((now - _lastQueueCountTime).TotalSeconds > 1)
                {
                    _lastQueueCountTime = now;
                    _ = System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            EnsureConnection();
                            lock (_connectionLock)
                            {
                                if (_channel != null)
                                {
                                    _cachedQueueCount = (int)_channel.MessageCount(QueueName);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to retrieve message count for RabbitMQ queue '{QueueName}'.", QueueName);
                        }
                    });
                }
                return _cachedQueueCount;
            }
        }

        /// <summary>
        /// Publishes a GPS TrackPoint to RabbitMQ as a persistent message.
        /// </summary>
        private readonly System.Threading.Channels.Channel<TrackPoint> _channelQueue;
        private readonly System.Threading.Channels.Channel<TrackPoint> _snapChannelQueue;
        private readonly System.Threading.CancellationTokenSource _cts = new();

        private async System.Threading.Tasks.Task ProcessQueueAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    EnsureConnection();
                    
                    await foreach (var point in _channelQueue.Reader.ReadAllAsync(_cts.Token))
                    {
                        var message = JsonSerializer.Serialize(point);
                        var body = Encoding.UTF8.GetBytes(message);

                        lock (_connectionLock)
                        {
                            if (_channel == null) break; // Re-ensure connection on next loop

                            var properties = _channel.CreateBasicProperties();
                            properties.Persistent = true;
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
                }
                catch (System.OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in GpsRabbitMqPublisher background worker.");
                    await System.Threading.Tasks.Task.Delay(1000, _cts.Token);
                }
            }
        }

        private async System.Threading.Tasks.Task ProcessSnapQueueAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    EnsureConnection();
                    
                    await foreach (var point in _snapChannelQueue.Reader.ReadAllAsync(_cts.Token))
                    {
                        var message = JsonSerializer.Serialize(point);
                        var body = Encoding.UTF8.GetBytes(message);

                        lock (_connectionLock)
                        {
                            if (_channel == null) break; // Re-ensure connection on next loop

                            var properties = _channel.CreateBasicProperties();
                            properties.Persistent = true;
                            properties.Type = nameof(TrackPoint);

                            _channel.BasicPublish(
                                exchange: "",
                                routingKey: SnapQueueName,
                                mandatory: true,
                                basicProperties: properties,
                                body: body
                            );
                        }
                    }
                }
                catch (System.OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in GpsRabbitMqPublisher background snap worker.");
                    await System.Threading.Tasks.Task.Delay(1000, _cts.Token);
                }
            }
        }

        public void Publish(TrackPoint point)
        {
            // Non-blocking fire-and-forget publish to memory channel
            _channelQueue.Writer.TryWrite(point);
        }

        public void PublishBatch(IEnumerable<TrackPoint> points)
        {
            if (points == null) return;
            foreach (var point in points)
            {
                _channelQueue.Writer.TryWrite(point);
            }
        }

        public void PublishForSnap(TrackPoint point)
        {
            // Non-blocking fire-and-forget publish to snap memory channel
            _snapChannelQueue.Writer.TryWrite(point);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _cts.Cancel();
            lock (_connectionLock)
            {
                _channel?.Dispose();
                _connection?.Dispose();
            }
            _disposed = true;
        }
    }
}
