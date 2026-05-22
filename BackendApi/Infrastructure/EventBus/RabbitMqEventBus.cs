using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private IConnection? _connection;
    private IModel? _channel;
    private bool _disposed;
    private readonly Dictionary<string, List<Type>> _handlers = new();
    private readonly Dictionary<string, Type> _eventTypes = new();

    public RabbitMqEventBus(IConfiguration configuration, IServiceProvider serviceProvider, ILogger<RabbitMqEventBus> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private void EnsureConnection()
    {
        if (_connection is { IsOpen: true }) return;

        var host = _configuration["MessageBroker:Host"] ?? _configuration["MessageBroker__Host"] ?? "localhost";
        var portStr = _configuration["MessageBroker:Port"] ?? _configuration["MessageBroker__Port"] ?? "5672";
        var username = _configuration["MessageBroker:Username"] ?? _configuration["MessageBroker__Username"] ?? "guest";
        var password = _configuration["MessageBroker:Password"] ?? _configuration["MessageBroker__Password"] ?? "guest";

        int.TryParse(portStr, out var port);

        _logger.LogInformation("Connecting to RabbitMQ Host: {Host}:{Port}", host, port);

        var factory = new ConnectionFactory
        {
            HostName = host,
            Port = port == 0 ? 5672 : port,
            UserName = username,
            Password = password,
            DispatchConsumersAsync = true // Enable asynchronous event handlers
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        // Declare dynamic/direct exchange for the routing system
        _channel.ExchangeDeclare(
            exchange: ExchangeName,
            type: "direct",
            durable: true,
            autoDelete: false
        );

        _logger.LogInformation("Successfully connected to RabbitMQ and declared exchange {Exchange}", ExchangeName);
    }

    public Task PublishAsync<T>(T @event) where T : IntegrationEvent
    {
        EnsureConnection();

        var eventName = @event.GetType().Name;
        var message = JsonSerializer.Serialize(@event);
        var body = Encoding.UTF8.GetBytes(message);

        var properties = _channel!.CreateBasicProperties();
        properties.Persistent = true; // Make message durable in disk
        properties.Type = eventName;

        _logger.LogInformation("Publishing integration event {EventName} ({EventId}) to RabbitMQ", eventName, @event.Id);

        _channel.BasicPublish(
            exchange: ExchangeName,
            routingKey: eventName,
            mandatory: true,
            basicProperties: properties,
            body: body
        );

        return Task.CompletedTask;
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

        var queueName = $"delivery_queue_{eventName}";
        _channel!.QueueDeclare(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false
        );

        _channel.QueueBind(
            queue: queueName,
            exchange: ExchangeName,
            routingKey: eventName
        );

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (sender, eventArgs) =>
        {
            var eventNameReceived = eventArgs.BasicProperties.Type;
            var body = eventArgs.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            try
            {
                await ProcessEventAsync(eventNameReceived, message);
                _channel!.BasicAck(eventArgs.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing integration event {EventName} via consumer", eventNameReceived);
                
                // Requeue message if processing fails (could be transient exception). 
                // In full enterprise, a dead-letter-exchange (DLX) is typically preferred.
                _channel!.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: true);
            }
        };

        _channel.BasicConsume(
            queue: queueName,
            autoAck: false,
            consumer: consumer
        );

        _logger.LogInformation("Subscribed to event {EventName} with queue {QueueName}", eventName, queueName);
    }

    private async Task ProcessEventAsync(string eventName, string message)
    {
        if (!_handlers.ContainsKey(eventName))
        {
            _logger.LogWarning("No handler registered for event {EventName}", eventName);
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        foreach (var handlerType in _handlers[eventName])
        {
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
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _channel?.Dispose();
        _connection?.Dispose();
        _disposed = true;
    }
}
