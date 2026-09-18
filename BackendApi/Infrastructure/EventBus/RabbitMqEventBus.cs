using System.Text;
using System.Text.Json;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BackendApi.Infrastructure.EventBus;

/// <summary>
/// A robust production-grade RabbitMQ Event Bus implementation supporting asynchronous processing.
/// </summary>
public partial class RabbitMqEventBus : IEventBus, IDisposable
{
    private const string ExchangeName = "delivery_event_bus";
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private IConnection? _connection;
    private IModel? _channel;
    private IModel? _publishChannel;
    private bool _disposed;
    private readonly Dictionary<string, List<Type>> _handlers = new();
    private readonly Dictionary<string, Type> _eventTypes = new();
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private readonly SemaphoreSlim _publishSemaphore = new(1, 1);
    private volatile string? _returnedRoutingKey;

    public RabbitMqEventBus(
        IConfiguration configuration, 
        IServiceProvider serviceProvider, 
        ILogger<RabbitMqEventBus> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    private void EnsureConnection()
    {
        EnsureConnectionAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureConnectionAsync()
    {
        if (_connection is { IsOpen: true } &&
            _channel is { IsOpen: true } &&
            _publishChannel is { IsOpen: true }) return;

        await _connectionSemaphore.WaitAsync();
        try
        {
            if (_connection is { IsOpen: true } &&
                _channel is { IsOpen: true } &&
                _publishChannel is { IsOpen: true }) return;

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
                DispatchConsumersAsync = true, // Enable asynchronous event handlers
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
                    _logger.LogInformation("Connecting to RabbitMQ Host: {Host}:{Port} (Attempt {Attempt}/{MaxRetries})", host, port, retryCount, maxRetries);
                    _connection = factory.CreateConnection();
                    _connection.ConnectionShutdown += (sender, e) =>
                    {
                        BackendApi.Security.Services.SecurityMetrics.RabbitMqConnectionStatus.Set(0);
                    };
                    if (retryCount > 1)
                    {
                        BackendApi.Security.Services.SecurityMetrics.RabbitMqReconnectsTotal.Inc();
                    }
                    connected = true;
                    BackendApi.Security.Services.SecurityMetrics.RabbitMqConnectionStatus.Set(1);
                }
                catch (Exception ex)
                {
                    if (retryCount >= maxRetries)
                    {
                        BackendApi.Security.Services.SecurityMetrics.RabbitMqConnectionStatus.Set(0);
                        _logger.LogCritical(ex, "Failed to connect to RabbitMQ broker after {MaxRetries} attempts.", maxRetries);
                        throw;
                    }

                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // Exponential backoff: 2s, 4s, 8s, 16s
                    _logger.LogWarning("RabbitMQ broker unreachable. Retrying in {Delay}s... Error: {Message}", delay.TotalSeconds, ex.Message);
                    await Task.Delay(delay);
                }
            }

            _channel = _connection!.CreateModel();
            _publishChannel = _connection.CreateModel();
            _publishChannel.ConfirmSelect();
            _publishChannel.BasicReturn += (_, args) =>
            {
                _returnedRoutingKey = args.RoutingKey;
                _logger.LogError(
                    "RabbitMQ returned unroutable event {RoutingKey}: {ReplyCode} {ReplyText}",
                    args.RoutingKey,
                    args.ReplyCode,
                    args.ReplyText);
            };

            // Declare dynamic/direct exchange for the routing system
            _channel.ExchangeDeclare(
                exchange: ExchangeName,
                type: "direct",
                durable: true,
                autoDelete: false
            );

            _logger.LogInformation("Successfully connected to RabbitMQ and declared exchange {Exchange}", ExchangeName);
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public async Task PublishAsync<T>(T @event) where T : IntegrationEvent
    {
        await EnsureConnectionAsync();

        var eventName = @event.GetType().Name;

        // Propagate CorrelationId from HttpContext if not already set
        string? correlationId = @event.CorrelationId;
        if (string.IsNullOrEmpty(correlationId))
        {
            correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"] as string 
                            ?? _httpContextAccessor.HttpContext?.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                            ?? Guid.NewGuid().ToString();

            // Set the read-only/init CorrelationId property using reflection
            var backingField = typeof(IntegrationEvent).GetField($"<{nameof(IntegrationEvent.CorrelationId)}>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            backingField?.SetValue(@event, correlationId);
        }

        var message = JsonSerializer.Serialize(@event);
        var body = Encoding.UTF8.GetBytes(message);

        _logger.LogInformation("Publishing integration event {EventName} ({EventId}) to RabbitMQ with CorrelationId {CorrelationId}", eventName, @event.Id, correlationId);

        await _publishSemaphore.WaitAsync();
        try
        {
            _returnedRoutingKey = null;
            var properties = _publishChannel!.CreateBasicProperties();
            properties.Persistent = true; // Make message durable in disk
            properties.Type = eventName;

            properties.Headers = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(correlationId))
            {
                properties.Headers.Add("X-Correlation-Id", correlationId);
                properties.Headers.Add("correlation-id", correlationId);
                properties.CorrelationId = correlationId;
            }

            _publishChannel.BasicPublish(
                exchange: ExchangeName,
                routingKey: eventName,
                mandatory: true,
                basicProperties: properties,
                body: body
            );
            _publishChannel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            if (string.Equals(_returnedRoutingKey, eventName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"RabbitMQ returned unroutable event '{eventName}'.");
            }
        }
        finally
        {
            _publishSemaphore.Release();
        }
    }

}
