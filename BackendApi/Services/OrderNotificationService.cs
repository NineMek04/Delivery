using BackendApi.Hubs;
using BackendApi.Models;
using Microsoft.AspNetCore.SignalR;

namespace BackendApi.Services;

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
    public async Task NotifyOrderStatusChangedAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group("admins").SendAsync(
            "OrderStatusChanged",
            order.Id,
            order.State.ToString(),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(order.AssignedRiderId))
        {
            await _hubContext.Clients.Group($"rider:{order.AssignedRiderId}").SendAsync(
                "OrderStatusChanged",
                order.Id,
                order.State.ToString(),
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(order.CustomerId))
        {
            await _hubContext.Clients.Group($"customer:{order.CustomerId}").SendAsync(
                "OrderStatusChanged",
                order.Id,
                order.State.ToString(),
                cancellationToken);
        }
    }

    /// <summary>
    /// แจ้ง stores group เมื่อ Order ใหม่ถูกสร้าง
    /// </summary>
    public async Task NotifyOrderCreatedAsync(object orderDto, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group("stores").SendAsync(
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
