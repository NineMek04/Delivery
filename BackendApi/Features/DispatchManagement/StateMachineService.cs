using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Infrastructure.EventBus;
using BackendApi.Infrastructure.EventBus.Events;
using BackendApi.Services.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Diagnostics;
using Order = BackendApi.Models.Entities.Order;

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
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<StateMachineService> _logger;

    public StateMachineService(
        ApplicationDbContext dbContext, 
        IEventBus eventBus,
        IConnectionMultiplexer redis,
        IHttpContextAccessor httpContextAccessor,
        ILogger<StateMachineService> logger)
    {
        _dbContext = dbContext;
        _eventBus = eventBus;
        _redis = redis;
        _httpContextAccessor = httpContextAccessor;
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
        var correlationId = BackendApi.Security.Services.CorrelationIdProvider.GetOrCreate(_httpContextAccessor);
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["OrderId"] = order.Id,
            ["RiderId"] = order.AssignedRiderId ?? string.Empty
        }))
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
                    if (order.ExpectedDeliveryTime != default)
                    {
                        var deviationSeconds = (DateTime.UtcNow - order.ExpectedDeliveryTime).TotalSeconds;
                        OperationalMetrics.RouteDeviationSeconds.Observe(deviationSeconds);
                    }
                    break;
            }

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Concurrency conflict detected during transition of Order {OrderId} from {OldState} to {NewState}", 
                    order.Id, oldState, newState);
                return false;
            }

            // Rebuild the complete recipient cache so multi-drop orders stay in sync.
            try
            {
                var db = _redis.GetDatabase();
                if (!string.IsNullOrEmpty(order.AssignedRiderId))
                {
                    var activeOrders = await _dbContext.Orders
                        .AsNoTracking()
                        .Where(candidate =>
                            candidate.AssignedRiderId == order.AssignedRiderId &&
                            (candidate.State == OrderState.ASSIGNED ||
                             candidate.State == OrderState.PICKING_UP ||
                             candidate.State == OrderState.DELIVERING))
                        .Select(candidate => new
                        {
                            candidate.Id,
                            candidate.CustomerId
                        })
                        .ToListAsync();

                    await ActiveOrderRecipientCache.ReplaceAsync(
                        db,
                        order.AssignedRiderId,
                        activeOrders.Select(candidate =>
                            new KeyValuePair<string, string?>(
                                candidate.Id,
                                candidate.CustomerId)));
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
                    order.CustomerId,
                    correlationId,
                    order.ShopId
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
        var correlationId = BackendApi.Security.Services.CorrelationIdProvider.GetOrCreate(_httpContextAccessor);
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RiderId"] = rider.Id
        }))
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

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Concurrency conflict detected during transition of Rider {RiderId} from {OldState} to {NewState}", 
                    rider.Id, oldState, newState);
                return false;
            }

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
}



