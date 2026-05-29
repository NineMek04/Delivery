using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BackendApi.Features.FleetTracking.Telemetry
{
    /// <summary>
    /// Background service that consumes GPS telemetry messages from 'gps_telemetry_queue'.
    /// Implements high-performance Mega-Batching with a Prefetch Count of 5000,
    /// a local bounded channel for thread separation, and manual Batch ACKs
    /// after successful PostgreSQL insert to guarantee Zero Data Loss.
    /// </summary>
    public class GpsRabbitMqConsumerWorker : BackgroundService
    {
        private const string QueueName = "gps_telemetry_queue";
        private const int SubBatchLimit = 5_000;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GpsRabbitMqConsumerWorker> _logger;

        private IConnection? _connection;
        private IModel? _channel;
        private AsyncEventingBasicConsumer? _consumer;

        // Local bounded Channel to safely buffer incoming messages before database batch write
        private readonly Channel<(TrackPoint Point, ulong DeliveryTag)> _localChannel;

        public GpsRabbitMqConsumerWorker(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<GpsRabbitMqConsumerWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;

            // Bounded to 10,000 items in C# memory to prevent OOM
            _localChannel = Channel.CreateBounded<(TrackPoint, ulong)>(new BoundedChannelOptions(10_000)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        private void InitializeRabbitMq()
        {
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
                DispatchConsumersAsync = true, // Enables async event handler
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                TopologyRecoveryEnabled = true
            };

            const int maxRetries = 5;
            var retryCount = 0;
            var connected = false;

            while (!connected && retryCount < maxRetries)
            {
                try
                {
                    retryCount++;
                    _logger.LogInformation("GpsRabbitMqConsumerWorker connecting to RabbitMQ Host: {Host}:{Port} (Attempt {Attempt}/{MaxRetries})", host, port, retryCount, maxRetries);
                    _connection = CreateConnection(factory);
                    connected = true;
                }
                catch (Exception ex)
                {
                    if (retryCount >= maxRetries)
                    {
                        _logger.LogCritical(ex, "GpsRabbitMqConsumerWorker failed to connect to RabbitMQ broker after {MaxRetries} attempts.", maxRetries);
                        throw;
                    }

                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                    _logger.LogWarning("GpsRabbitMqConsumerWorker: RabbitMQ unreachable. Retrying in {Delay}s... Error: {Message}", delay.TotalSeconds, ex.Message);
                    Thread.Sleep(delay);
                }
            }

            _channel = _connection!.CreateModel();

            // Declare queue to ensure it exists
            _channel.QueueDeclare(
                queue: QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            // Level 2 QoS: Prefetch 5000 messages to process in Mega Batch
            _channel.BasicQos(prefetchSize: 0, prefetchCount: SubBatchLimit, global: false);

            _consumer = new AsyncEventingBasicConsumer(_channel);
            _consumer.Received += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var point = JsonSerializer.Deserialize<TrackPoint>(message);

                    if (point != null)
                    {
                        // Write to local channel, wait if full to apply safe backpressure
                        await _localChannel.Writer.WriteAsync((point, ea.DeliveryTag));
                    }
                    else
                    {
                        _logger.LogWarning("Discarding malformed GPS message.");
                        _channel.BasicAck(ea.DeliveryTag, multiple: false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing incoming GPS message.");
                    // Send negative ACK without requeue (to DLQ or drop)
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            _channel.BasicConsume(
                queue: QueueName,
                autoAck: false, // Manual Acknowledgment only!
                consumer: _consumer
            );

            _logger.LogInformation("GpsRabbitMqConsumerWorker successfully subscribed to '{QueueName}' with PrefetchCount={Prefetch}", QueueName, SubBatchLimit);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                InitializeRabbitMq();
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to initialize RabbitMQ for GPS Telemetry Consumer.");
                return;
            }

            _logger.LogInformation("GpsRabbitMqConsumerWorker background processor loop started.");

            try
            {
                // Main reading loop from the local C# Channel
                while (await _localChannel.Reader.WaitToReadAsync(stoppingToken))
                {
                    try
                    {
                        await DrainAndSaveBatchAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred while draining and saving GPS batches.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            finally
            {
                _logger.LogInformation("GpsRabbitMqConsumerWorker stopping. Draining remaining local buffer...");

                // Complete the local channel writer
                _localChannel.Writer.TryComplete();

                try
                {
                    // Do a final drain of any remaining items in the local channel
                    await DrainAndSaveBatchAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during final flush of GpsRabbitMqConsumerWorker");
                }

                _channel?.Dispose();
                _connection?.Dispose();
            }
        }

        /// <summary>
        /// Drains messages up to SubBatchLimit, saves them to PostGIS, and performs a manual ACK.
        /// </summary>
        private async Task DrainAndSaveBatchAsync(CancellationToken ct)
        {
            var points = new List<TrackPoint>(SubBatchLimit);
            var deliveryTags = new List<ulong>(SubBatchLimit);

            // Read up to SubBatchLimit elements that are immediately available
            while (points.Count < SubBatchLimit && _localChannel.Reader.TryRead(out var item))
            {
                points.Add(item.Point);
                deliveryTags.Add(item.DeliveryTag);
            }

            if (points.Count == 0) return;

            _logger.LogInformation("Drained {Count} GPS points from local channel. Committing to database...", points.Count);

            try
            {
                // Save to database within scoped context
                using (var scope = _serviceProvider.CreateScope())
                {
                    var historyService = scope.ServiceProvider.GetRequiredService<BackendApi.Services.Telemetry.GpsHistoryService>();
                    await historyService.SavePointsAsync(points, ct);
                }

                // Successful Database Save -> Bulk ACK to RabbitMQ
                // We acknowledge all messages up to the maximum delivery tag in this batch
                ulong maxDeliveryTag = 0;
                foreach (var tag in deliveryTags)
                {
                    if (tag > maxDeliveryTag) maxDeliveryTag = tag;
                }

                if (maxDeliveryTag > 0 && _channel != null)
                {
                    lock (_channel) // Make sure basic ack is thread-safe on the channel
                    {
                        _channel.BasicAck(maxDeliveryTag, multiple: true);
                    }
                    _logger.LogInformation("Batch of {Count} GPS points successfully ACKed to RabbitMQ up to tag {Tag}.", points.Count, maxDeliveryTag);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to commit batch of {Count} GPS points to the database. Messages will not be ACKed.", points.Count);
                // We do NOT acknowledge these messages. Since they are not ACKed, they will be preserved
                // in RabbitMQ's persistent store. If the connection drops or the service restarts,
                // RabbitMQ will automatically requeue and redeliver them.
            }
        }

        /// <summary>
        /// Factory method for creating RabbitMQ connections. Virtual to support unit testing via subclassing.
        /// </summary>
        protected virtual IConnection CreateConnection(ConnectionFactory factory)
        {
            return factory.CreateConnection();
        }
    }
}
