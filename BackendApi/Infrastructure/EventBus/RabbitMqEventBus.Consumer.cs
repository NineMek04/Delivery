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
public partial class RabbitMqEventBus
{
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
                    await IncrementRetryAndRequeueAsync(channel, eventArgs);
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

    private async Task IncrementRetryAndRequeueAsync(
        IModel consumerChannel,
        BasicDeliverEventArgs eventArgs)
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
            await _publishSemaphore.WaitAsync();
            try
            {
                await EnsureConnectionAsync();
                _returnedRoutingKey = null;
                _publishChannel!.BasicPublish(
                    exchange: ExchangeName,
                    routingKey: eventArgs.RoutingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: eventArgs.Body
                );
                _publishChannel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
                if (string.Equals(
                    _returnedRoutingKey,
                    eventArgs.RoutingKey,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"RabbitMQ returned unroutable retry event '{eventArgs.RoutingKey}'.");
                }
            }
            finally
            {
                _publishSemaphore.Release();
            }

            consumerChannel.BasicAck(eventArgs.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish retry attempt {RetryAttempt} for event {EventName}. Requeuing original message on the queue.", currentRetry, properties.Type);
            consumerChannel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: true);
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
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            var lockKey = $"{eventId:N}:{handlerType.FullName}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");

            var alreadyProcessed = await dbContext.ProcessedEvents.AnyAsync(
                p => p.EventId == eventId && p.HandlerName == handlerType.Name);

            if (alreadyProcessed)
            {
                _logger.LogWarning("Duplicate event detected. Skipping execution of handler {HandlerName} for event {EventId}", handlerType.Name, eventId);
                await transaction.CommitAsync();
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
            dbContext.ProcessedEvents.Add(new BackendApi.Models.SystemModels.ProcessedEvent
            {
                EventId = eventId,
                HandlerName = handlerType.Name,
                ProcessedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _channel?.Dispose();
        _publishChannel?.Dispose();
        _connection?.Dispose();
        _connectionSemaphore.Dispose();
        _publishSemaphore.Dispose();
        _disposed = true;
    }
}
