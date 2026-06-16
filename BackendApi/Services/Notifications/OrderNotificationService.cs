using BackendApi.Hubs;
using BackendApi.Hubs.Chat;
using BackendApi.Hubs.Tracking;
using BackendApi.Core.StateMachines;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using Microsoft.AspNetCore.SignalR;

namespace BackendApi.Services.Notifications;

/// <summary>
/// Order Notification Service — รวบรวมการส่ง SignalR notifications ที่เกี่ยวกับ Order ไว้ที่เดียว
/// ลด code duplication จาก OrderService ที่มี broadcast pattern เหมือนกันหลายจุด
/// </summary>
public class OrderNotificationService
{
    private readonly IHubContext<TrackingHub> _hubContext;
    private readonly ILogger<OrderNotificationService> _logger;

    public OrderNotificationService(
        IHubContext<TrackingHub> hubContext,
        ILogger<OrderNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// แจ้ง admins + rider + customer เมื่อสถานะ Order เปลี่ยน
    /// </summary>
    public virtual Task NotifyOrderStatusChangedAsync(
        Order order,
        CancellationToken cancellationToken = default)
    {
        return NotifyOrderStatusChangedAsync(order, previousState: null, cancellationToken);
    }

    public virtual async Task NotifyOrderStatusChangedAsync(
        Order order,
        OrderState? previousState,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            orderId = order.Id,
            orderRefNumber = order.TrackingCode,
            previousStatus = previousState?.ToString(),
            newStatus = order.State.ToString(),
            riderId = order.AssignedRiderId,
            timestamp = DateTime.UtcNow
        };

        await _hubContext.Clients.Group("admins").SendAsync(
            "OrderStatusChanged",
            payload,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(order.AssignedRiderId))
        {
            await _hubContext.Clients.Group($"rider:{order.AssignedRiderId}").SendAsync(
                "OrderStatusChanged",
                payload,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(order.ShopId))
        {
            await _hubContext.Clients.Group($"store:{order.ShopId}").SendAsync(
                "OrderStatusChanged",
                payload,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(order.CustomerId))
        {
            await _hubContext.Clients.Group($"customer:{order.CustomerId}").SendAsync(
                "OrderStatusChanged",
                payload,
                cancellationToken);
        }
    }

    /// <summary>
    /// แจ้ง store group เมื่อ Order ใหม่ถูกสร้าง — ส่งเฉพาะร้านที่เกี่ยวข้อง
    /// </summary>
    public async Task NotifyOrderCreatedAsync(object orderDto, CancellationToken cancellationToken = default, string? shopId = null)
    {
        // ถ้ามี shopId → ส่งเฉพาะร้านนั้น (shop-specific group)
        // ถ้าไม่มี shopId → broadcast ไปทุก stores (fallback)
        var groupName = !string.IsNullOrWhiteSpace(shopId)
            ? $"store:{shopId}"
            : "stores";

        _logger.LogInformation("Notifying group {Group} of new OrderCreated event", groupName);
        await _hubContext.Clients.Group(groupName).SendAsync(
            "OrderCreated", orderDto, cancellationToken);
    }

    /// <summary>
    /// แจ้ง customer เมื่อร้านค้ายอมรับ Order
    /// </summary>
    public async Task NotifyOrderAcceptedByStoreAsync(string orderId, string state, string customerId, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"customer:{customerId}").SendAsync(
            "OrderAcceptedByStore",
            new { orderId, status = state },
            cancellationToken);
    }
}



