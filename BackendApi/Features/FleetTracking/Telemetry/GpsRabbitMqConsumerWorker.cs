using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
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
    public partial class GpsRabbitMqConsumerWorker : BackgroundService
    {
        private const string QueueName = "gps_telemetry_queue";
        private const int SubBatchLimit = 5_000;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly ILogger<GpsRabbitMqConsumerWorker> _logger;

        private IConnection? _connection;
        private IModel? _channel;
        private AsyncEventingBasicConsumer? _consumer;

        // Local bounded Channel to safely buffer incoming messages before database batch write
        private readonly Channel<(TrackPoint Point, ulong DeliveryTag)> _localChannel;

        public GpsRabbitMqConsumerWorker(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            IHostApplicationLifetime appLifetime,
            ILogger<GpsRabbitMqConsumerWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _appLifetime = appLifetime;
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
            var username = _configuration["MessageBroker:Username"] ??
                _configuration["MessageBroker__Username"] ??
                throw new InvalidOperationException("MessageBroker:Username is required.");
            var password = _configuration["MessageBroker:Password"] ??
                _configuration["MessageBroker__Password"] ??
                throw new InvalidOperationException("MessageBroker:Password is required.");

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
                        _logger.LogWarning("Discarding malformed GPS message. Routing to DLQ.");
                        // [THREAD-SAFETY FIX] IModel is not thread-safe — wrap all BasicNack/Ack calls in lock
                        if (_channel != null)
                            lock (_channel)
                                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing incoming GPS message. Routing to DLQ.");
                    // [THREAD-SAFETY FIX] IModel is not thread-safe — wrap all BasicNack/Ack calls in lock
                    if (_channel != null)
                        lock (_channel)
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


        /// <summary>
        /// Factory method for creating RabbitMQ connections. Virtual to support unit testing via subclassing.
        /// </summary>
        protected virtual IConnection CreateConnection(ConnectionFactory factory)
        {
            return factory.CreateConnection();
        }
    }
}
