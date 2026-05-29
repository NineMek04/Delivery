using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Infrastructure.EventBus.Events;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Order = BackendApi.Models.Order;

namespace BackendApi.Services.Dispatch;

/// <summary>
/// State Machine Service — Validate และดำเนินการเปลี่ยนสถานะ Order/Rider
/// ป้องกัน Illegal State Transition ทุกกรณี
/// </summary>
public class StateMachineService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventBus _eventBus;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<StateMachineService> _logger;

    public StateMachineService(
        ApplicationDbContext dbContext, 
        IEventBus eventBus,
        IConnectionMultiplexer redis,
        ILogger<StateMachineService> logger)
    {
        _dbContext = dbContext;
        _eventBus = eventBus;
        _redis = redis;
        _logger = logger;
    }

    // ── Order State Transitions ────────────────────────────────────

    /// <summary>
    /// เปลี่ยนสถานะ Order พร้อม validate transition
    /// </summary>
    public virtual async Task<bool> TransitionOrderAsync(string orderId, OrderState newState)
    {
        var order = await _dbContext.Orders.FindAsync(orderId);
        if (order is null)
        {
            _logger.LogWarning("Order {OrderId} not found for state transition", orderId);
            return false;
        }

        return await TransitionOrderAsync(order, newState);
    }

    /// <summary>
    /// เปลี่ยนสถานะ Order (overload ที่รับ entity ตรง)
    /// </summary>
    public virtual async Task<bool> TransitionOrderAsync(Order order, OrderState newState)
    {
        if (!OrderStateRules.IsValidTransition(order.State, newState))
        {
            _logger.LogWarning(
                "Invalid order transition: {OrderId} from {From} → {To}",
                order.Id, order.State, newState);
            return false;
        }

        var oldState = order.State;
        order.State = newState;

        // Auto-set timestamps
        switch (newState)
        {
            case OrderState.ASSIGNED:
                order.AssignedAt = DateTime.UtcNow;
                break;
            case OrderState.COMPLETED:
                order.CompletedAt = DateTime.UtcNow;
                break;
        }

        await _dbContext.SaveChangesAsync();

        // Update active order cache for the rider in Redis
        try
        {
            var db = _redis.GetDatabase();
            if (!string.IsNullOrEmpty(order.AssignedRiderId))
            {
                var activeOrderKey = $"riders:active_order:{order.AssignedRiderId}";
                if (newState == OrderState.ASSIGNED || newState == OrderState.PICKING_UP || newState == OrderState.DELIVERING)
                {
                    await db.HashSetAsync(activeOrderKey, new[]
                    {
                        new HashEntry("order_id", order.Id),
                        new HashEntry("customer_id", order.CustomerId ?? string.Empty)
                    });
                    await db.KeyExpireAsync(activeOrderKey, TimeSpan.FromHours(24));
                }
                else
                {
                    await db.KeyDeleteAsync(activeOrderKey);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update active order cache in Redis for Rider {RiderId}", order.AssignedRiderId);
        }

        // Publish Order Status Changed Integration Event asynchronously to RabbitMQ
        try
        {
            await _eventBus.PublishAsync(new OrderStatusChangedIntegrationEvent(
                order.Id,
                order.RefNumber,
                oldState,
                order.State,
                order.AssignedRiderId,
                order.CustomerId
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish OrderStatusChangedIntegrationEvent for Order {OrderId}", order.Id);
        }

        _logger.LogInformation(
            "Order {OrderId} transitioned: {From} → {To}",
            order.Id, oldState, newState);

        return true;
    }

    // ── Rider State Transitions ────────────────────────────────────

    /// <summary>
    /// เปลี่ยนสถานะ Rider พร้อม validate transition
    /// </summary>
    public virtual async Task<bool> TransitionRiderAsync(string riderId, RiderState newState)
    {
        var rider = await _dbContext.Riders.FindAsync(riderId);
        if (rider is null)
        {
            _logger.LogWarning("Rider {RiderId} not found for state transition", riderId);
            return false;
        }

        return await TransitionRiderAsync(rider, newState);
    }

    /// <summary>
    /// เปลี่ยนสถานะ Rider (overload ที่รับ entity ตรง)
    /// </summary>
    public virtual async Task<bool> TransitionRiderAsync(Rider rider, RiderState newState)
    {
        if (!RiderStateRules.IsValidTransition(rider.State, newState))
        {
            _logger.LogWarning(
                "Invalid rider transition: {RiderId} from {From} → {To}",
                rider.Id, rider.State, newState);
            return false;
        }

        var oldState = rider.State;
        rider.State = newState;

        await _dbContext.SaveChangesAsync();

        // Update rider status cache in Redis
        try
        {
            var db = _redis.GetDatabase();
            var statusCacheKey = $"riders:status:{rider.Id}";
            await db.StringSetAsync(statusCacheKey, newState.ToString(), TimeSpan.FromHours(24));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update rider status cache in Redis for Rider {RiderId}", rider.Id);
        }

        _logger.LogInformation(
            "Rider {RiderId} transitioned: {From} → {To}",
            rider.Id, oldState, newState);

        return true;
    }
}
