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
public class RabbitMqEventBus : IEventBus, IDisposable
{
    private const string ExchangeName = "delivery_event_bus";
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private IConnection? _connection;
    private IModel? _channel;
    private bool _disposed;
    private readonly Dictionary<string, List<Type>> _handlers = new();
    private readonly Dictionary<string, Type> _eventTypes = new();
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private readonly SemaphoreSlim _publishSemaphore = new(1, 1);

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
        if (_connection is { IsOpen: true }) return;

        await _connectionSemaphore.WaitAsync();
        try
        {
            if (_connection is { IsOpen: true }) return;

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
                        BackendApi.Security.SecurityMetrics.RabbitMqConnectionStatus.Set(0);
                    };
                    if (retryCount > 1)
                    {
                        BackendApi.Security.SecurityMetrics.RabbitMqReconnectsTotal.Inc();
                    }
                    connected = true;
                    BackendApi.Security.SecurityMetrics.RabbitMqConnectionStatus.Set(1);
                }
                catch (Exception ex)
                {
                    if (retryCount >= maxRetries)
                    {
                        BackendApi.Security.SecurityMetrics.RabbitMqConnectionStatus.Set(0);
                        _logger.LogCritical(ex, "Failed to connect to RabbitMQ broker after {MaxRetries} attempts.", maxRetries);
                        throw;
                    }

                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // Exponential backoff: 2s, 4s, 8s, 16s
                    _logger.LogWarning("RabbitMQ broker unreachable. Retrying in {Delay}s... Error: {Message}", delay.TotalSeconds, ex.Message);
                    await Task.Delay(delay);
                }
            }

            _channel = _connection!.CreateModel();

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
            var properties = _channel!.CreateBasicProperties();
            properties.Persistent = true; // Make message durable in disk
            properties.Type = eventName;

            properties.Headers = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(correlationId))
            {
                properties.Headers.Add("X-Correlation-Id", correlationId);
                properties.Headers.Add("correlation-id", correlationId);
                properties.CorrelationId = correlationId;
            }

            _channel.BasicPublish(
                exchange: ExchangeName,
                routingKey: eventName,
                mandatory: true,
                basicProperties: properties,
                body: body
            );
        }
        finally
        {
            _publishSemaphore.Release();
        }
    }

    public void Subscribe<T, TH>()
        where T : IntegrationEvent
        where TH : IIntegrationEventHandler<T>
    {
        var eventName = typeof(T).Name;
        var handlerType = typeof(TH);

        if (!_handlers.ContainsKey(eventName))
        {
            _handlers.Add(eventName, new List<Type>());
            _eventTypes.Add(eventName, typeof(T));
        }

        if (_handlers[eventName].Contains(handlerType))
        {
            throw new ArgumentException($"Handler type {handlerType.Name} already registered for '{eventName}'", nameof(handlerType));
        }

        _handlers[eventName].Add(handlerType);

        EnsureConnection();

        var channel = _channel!;
        channel.BasicQos(prefetchSize: 0, prefetchCount: 100, global: false);

        var queueName = $"delivery_queue_{eventName}";
        var dlxExchange = $"{ExchangeName}_dlx";
        var dlqQueue = $"{queueName}_dlq";

        // Declare Dead Letter Exchange + Queue
        channel.ExchangeDeclare(dlxExchange, "direct", durable: true);
        channel.QueueDeclare(dlqQueue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(dlqQueue, dlxExchange, eventName);

        // Declare Main Queue with x-dead-letter-exchange configuration
        channel.QueueDeclare(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", dlxExchange },
                { "x-dead-letter-routing-key", eventName }
            }
        );

        channel.QueueBind(
            queue: queueName,
            exchange: ExchangeName,
            routingKey: eventName
        );

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (sender, eventArgs) =>
        {
            var eventNameReceived = eventArgs.BasicProperties.Type ?? eventName;
            var body = eventArgs.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            // Extract CorrelationId from headers or properties
            string? correlationId = eventArgs.BasicProperties.CorrelationId;
            if (string.IsNullOrEmpty(correlationId) && eventArgs.BasicProperties.Headers != null)
            {
                if (eventArgs.BasicProperties.Headers.TryGetValue("X-Correlation-Id", out var correlationHeaderObj) ||
                    eventArgs.BasicProperties.Headers.TryGetValue("correlation-id", out correlationHeaderObj))
                {
                    if (correlationHeaderObj is byte[] correlationBytes)
                    {
                        correlationId = Encoding.UTF8.GetString(correlationBytes);
                    }
                    else
                    {
                        correlationId = correlationHeaderObj?.ToString();
                    }
                }
            }

            // Fallback: parse from event message itself
            if (string.IsNullOrEmpty(correlationId))
            {
                try
                {
                    using var doc = JsonDocument.Parse(message);
                    if (doc.RootElement.TryGetProperty("CorrelationId", out var prop) && prop.ValueKind == JsonValueKind.String)
                    {
                        correlationId = prop.GetString();
                    }
                }
                catch {}
            }

            if (string.IsNullOrEmpty(correlationId))
            {
                correlationId = Guid.NewGuid().ToString();
            }

            using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
            {
                // 1. Check retry count — if >= 5, nack without requeue (routing it to DLQ)
                var retryCount = GetRetryCount(eventArgs.BasicProperties);
                if (retryCount >= 5)
                {
                    _logger.LogError("Message for event {EventName} exceeded max retries ({Retries}). Sending to DLQ.", eventNameReceived, retryCount);
                    channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: false);
                    return;
                }

                try
                {
                    await ProcessEventAsync(eventNameReceived, message);
                    channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing integration event {EventName} via consumer. Attempt {Attempt} of 5.", eventNameReceived, retryCount + 1);
                    
                    // Increment retry count via header and requeue
                    IncrementRetryAndRequeue(channel, eventArgs);
                }
            }
        };

        channel.BasicConsume(
            queue: queueName,
            autoAck: false,
            consumer: consumer
        );

        _logger.LogInformation("Subscribed to event {EventName} with queue {QueueName} and DLQ {DlqName}", eventName, queueName, dlqQueue);
    }

    private int GetRetryCount(IBasicProperties properties)
    {
        if (properties.Headers == null) return 0;

        if (properties.Headers.TryGetValue("x-delivery-retry-count", out var retryObj))
        {
            return Convert.ToInt32(retryObj);
        }

        return 0;
    }

    private void IncrementRetryAndRequeue(IModel channel, BasicDeliverEventArgs eventArgs)
    {
        var properties = eventArgs.BasicProperties;
        var headers = properties.Headers ?? new Dictionary<string, object>();
        
        var currentRetry = 0;
        if (headers.TryGetValue("x-delivery-retry-count", out var retryObj))
        {
            currentRetry = Convert.ToInt32(retryObj);
        }

        currentRetry++;
        headers["x-delivery-retry-count"] = currentRetry;
        properties.Headers = headers;

        _logger.LogInformation("Re-publishing failed event {EventName} for retry attempt {RetryAttempt}", properties.Type, currentRetry);
        
        try
        {
            channel.BasicPublish(
                exchange: ExchangeName,
                routingKey: eventArgs.RoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: eventArgs.Body
            );

            channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish retry attempt {RetryAttempt} for event {EventName}. Requeuing original message on the queue.", currentRetry, properties.Type);
            channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private async Task ProcessEventAsync(string eventName, string message)
    {
        if (!_handlers.ContainsKey(eventName))
        {
            _logger.LogWarning("No handler registered for event {EventName}", eventName);
            return;
        }

        // Parse event ID for idempotency check
        Guid eventId;
        try
        {
            using var eventDoc = JsonDocument.Parse(message);
            if (eventDoc.RootElement.TryGetProperty("Id", out var idProp))
            {
                eventId = idProp.GetGuid();
            }
            else
            {
                throw new InvalidDataException(
                    $"Event payload for '{eventName}' is missing the required Id property.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse event ID from payload for event {EventName}", eventName);
            throw;
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BackendApi.Data.ApplicationDbContext>();

        foreach (var handlerType in _handlers[eventName])
        {
            // Idempotency: check if this event has already been processed by this handler
            var alreadyProcessed = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                dbContext.ProcessedEvents,
                p => p.EventId == eventId && p.HandlerName == handlerType.Name
            );

            if (alreadyProcessed)
            {
                _logger.LogWarning("Duplicate event detected. Skipping execution of handler {HandlerName} for event {EventId}", handlerType.Name, eventId);
                continue;
            }

            var handler = scope.ServiceProvider.GetService(handlerType);
            if (handler == null)
            {
                _logger.LogError("Could not resolve handler {HandlerName} for event {EventName}", handlerType.Name, eventName);
                continue;
            }

            var eventType = _eventTypes[eventName];
            var integrationEvent = JsonSerializer.Deserialize(message, eventType);
            if (integrationEvent == null)
            {
                _logger.LogError("Could not deserialize event body to {EventType}", eventType.Name);
                continue;
            }

            var concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
            var method = concreteType.GetMethod("Handle");
            if (method != null)
            {
                await (Task)method.Invoke(handler, new[] { integrationEvent })!;
            }

            // Record event as successfully processed by this handler
            dbContext.ProcessedEvents.Add(new BackendApi.Models.ProcessedEvent
            {
                EventId = eventId,
                HandlerName = handlerType.Name,
                ProcessedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _channel?.Dispose();
        _connection?.Dispose();
        _connectionSemaphore.Dispose();
        _publishSemaphore.Dispose();
        _disposed = true;
    }
}
