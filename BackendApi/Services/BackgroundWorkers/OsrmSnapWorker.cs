using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BackendApi.Features.FleetTracking.Telemetry;
using BackendApi.Hubs;
using BackendApi.Services.Ai;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;

namespace BackendApi.Services.BackgroundWorkers
{
    /// <summary>
    /// Background worker that consumes GPS points from 'gps_snap_queue',
    /// calls OSRM to snap the points to the nearest road asynchronously,
    /// caches the snapped coordinates in Redis, and broadcasts the snapped
    /// points to the Admin dashboard via SignalR.
    /// </summary>
    public class OsrmSnapWorker : BackgroundService
    {
        private const string QueueName = "gps_snap_queue";
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OsrmSnapWorker> _logger;
        private readonly IConnectionMultiplexer _redis;
        private readonly IHubContext<TrackingHub> _hubContext;

        private IConnection? _connection;
        private IModel? _channel;
        private AsyncEventingBasicConsumer? _consumer;
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        public OsrmSnapWorker(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<OsrmSnapWorker> logger,
            IConnectionMultiplexer redis,
            IHubContext<TrackingHub> hubContext)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
            _redis = redis;
            _hubContext = hubContext;
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
                DispatchConsumersAsync = true,
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
                    _logger.LogInformation("OsrmSnapWorker connecting to RabbitMQ Host: {Host}:{Port} (Attempt {Attempt}/{MaxRetries})", host, port, retryCount, maxRetries);
                    _connection = factory.CreateConnection();
                    connected = true;
                }
                catch (Exception ex)
                {
                    if (retryCount >= maxRetries)
                    {
                        _logger.LogCritical(ex, "OsrmSnapWorker failed to connect to RabbitMQ broker after {MaxRetries} attempts.", maxRetries);
                        throw;
                    }

                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                    _logger.LogWarning("OsrmSnapWorker: RabbitMQ unreachable. Retrying in {Delay}s... Error: {Message}", delay.TotalSeconds, ex.Message);
                    Thread.Sleep(delay);
                }
            }

            _channel = _connection!.CreateModel();

            var dlxName = "gps_telemetry_dlx";
            var dlqName = $"{QueueName}_dlq";

            // Declare Dead Letter Exchange + Queue
            _channel.ExchangeDeclare(dlxName, "direct", durable: true);
            _channel.QueueDeclare(dlqName, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(dlqName, dlxName, QueueName);

            var queueArguments = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", dlxName },
                { "x-dead-letter-routing-key", QueueName }
            };

            // Declare queue to ensure it exists
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

            // Level 2 QoS: Limit prefetch to avoid buffer build-up
            _channel.BasicQos(prefetchSize: 0, prefetchCount: 100, global: false);

            _consumer = new AsyncEventingBasicConsumer(_channel);
            _consumer.Received += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var point = JsonSerializer.Deserialize<TrackPoint>(message, _jsonOptions);

                    if (point != null)
                    {
                        await ProcessSnapPointAsync(point);
                        _channel.BasicAck(ea.DeliveryTag, multiple: false);
                    }
                    else
                    {
                        _logger.LogWarning("Discarding malformed snap GPS message. Routing to DLQ.");
                        _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing snap GPS message. Routing to DLQ.");
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            _channel.BasicConsume(
                queue: QueueName,
                autoAck: false,
                consumer: _consumer
            );

            _logger.LogInformation("OsrmSnapWorker successfully subscribed to '{QueueName}' (with DLQ)", QueueName);
        }

        private async Task ProcessSnapPointAsync(TrackPoint point)
        {
            var db = _redis.GetDatabase();
            var lastSnapKey = $"telemetry:last_snap:{point.RiderId}";

            // 1. Throttling: Skip snapping if snapped less than 1.0 second ago
            var lastSnapTimeRaw = await db.StringGetAsync(lastSnapKey);
            if (lastSnapTimeRaw.HasValue && double.TryParse(lastSnapTimeRaw, out var lastSnapUnix))
            {
                var currentUnix = (point.Timestamp - DateTime.UnixEpoch).TotalSeconds;
                if (currentUnix - lastSnapUnix < 1.0)
                {
                    return; // Skip OSRM snap call
                }
            }

            // 2. Call OSRM Snap-to-Road
            double snappedLat = point.Lat;
            double snappedLng = point.Lng;

            using (var scope = _serviceProvider.CreateScope())
            {
                var routingService = scope.ServiceProvider.GetRequiredService<OsrmRoutingService>();
                var snappedResult = await routingService.SnapToRoadAsync(point.Lat, point.Lng);
                snappedLat = snappedResult.Lat;
                snappedLng = snappedResult.Lng;
            }

            var now = DateTime.UtcNow;

            // 3. Write beautiful snapped coordinates to Redis Hash
            var snappedGpsKey = $"riders:snapped_gps:{point.RiderId}";
            await db.HashSetAsync(snappedGpsKey, new[]
            {
                new HashEntry("lat", snappedLat),
                new HashEntry("lng", snappedLng),
                new HashEntry("updated_at", now.ToString("o"))
            });
            await db.KeyExpireAsync(snappedGpsKey, TimeSpan.FromHours(24));

            // Record snap timestamp
            var currentPointUnix = (point.Timestamp - DateTime.UnixEpoch).TotalSeconds;
            await db.StringSetAsync(lastSnapKey, currentPointUnix, TimeSpan.FromSeconds(30));

            // 4. Broadcast snap telemetry via SignalR
            await _hubContext.Clients.Group("admins").SendAsync("RiderLocationSnapped", new
            {
                RiderId = point.RiderId,
                Lat = snappedLat,
                Lng = snappedLng,
                Timestamp = point.Timestamp,
                isSnapped = true
            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                InitializeRabbitMq();
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to initialize RabbitMQ for OsrmSnapWorker.");
                return;
            }

            _logger.LogInformation("OsrmSnapWorker background processor loop started.");

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            finally
            {
                _channel?.Dispose();
                _connection?.Dispose();
                _logger.LogInformation("OsrmSnapWorker background processor stopped.");
            }
        }
    }
}
