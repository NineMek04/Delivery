using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BackendApi.Core.Constants;
using BackendApi.Core.DataHandlers;
using BackendApi.Core.Helpers;
using BackendApi.Core.StateMachines;
using BackendApi.Hubs;
using BackendApi.Hubs.Chat;
using BackendApi.Hubs.Tracking;
using BackendApi.Models;
using BackendApi.Models.Entities;
using BackendApi.Models.SystemModels;
using BackendApi.Services.Notifications;
using BackendApi.Infrastructure.EventBus.Events;
using BackendApi.Services.BackgroundWorkers;
using BackendApi.Services.BackgroundWorkers.Queues;
using BackendApi.Services.BackgroundWorkers.Maintenance;
using BackendApi.Services.BackgroundWorkers.Jobs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Infrastructure.EventBus.Handlers
{
    /// <summary>
    /// ตัวประมวลผลข้อความเปลี่ยนผ่านสถานะออเดอร์แบบอะซิงโครนัสผ่าน RabbitMQ
    /// บรอดแคสต์ผ่าน SignalR หาผู้ใช้ทุกกลุ่ม และพุช FCM Notification
    /// </summary>
    public class OrderStatusChangedIntegrationEventHandler : IIntegrationEventHandler<OrderStatusChangedIntegrationEvent>
    {
        private readonly IHubContext<TrackingHub> _hubContext;
        private readonly IFcmNotificationService _fcmService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderStatusChangedIntegrationEventHandler> _logger;
        private readonly IBackgroundTaskQueue _backgroundTaskQueue;

        public OrderStatusChangedIntegrationEventHandler(
            IHubContext<TrackingHub> hubContext,
            IFcmNotificationService fcmService,
            IServiceScopeFactory scopeFactory,
            ILogger<OrderStatusChangedIntegrationEventHandler> logger,
            IBackgroundTaskQueue backgroundTaskQueue)
        {
            _hubContext = hubContext;
            _fcmService = fcmService;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _backgroundTaskQueue = backgroundTaskQueue;
        }

        public async Task Handle(OrderStatusChangedIntegrationEvent @event)
        {
            _logger.LogInformation(
                "Handling integration event: {EventName} ({EventId}) - Order {OrderId} (Ref: {RefNumber}) changed from {OldState} to {NewState}",
                nameof(OrderStatusChangedIntegrationEvent),
                @event.Id,
                @event.OrderId,
                @event.RefNumber,
                @event.OldState,
                @event.NewState
            );

            var payload = new
            {
                orderId = @event.OrderId,
                orderRefNumber = TrackingCodeFormatter.Format(
                    TrackingPrefixes.Order,
                    @event.RefNumber),
                previousStatus = @event.OldState.ToString(),
                newStatus = @event.NewState.ToString(),
                riderId = @event.AssignedRiderId,
                timestamp = @event.CreationDate
            };

            // 1. บรอดแคสต์ SignalR ไปยังกลุ่ม Admin
            await _hubContext.Clients.Group("admins").SendAsync(
                "OrderStatusChanged",
                payload);

            // 2. บรอดแคสต์ SignalR ไปยังกลุ่ม Rider ที่รับผิดชอบ
            if (!string.IsNullOrWhiteSpace(@event.AssignedRiderId))
            {
                await _hubContext.Clients.Group($"rider:{@event.AssignedRiderId}").SendAsync(
                    "OrderStatusChanged",
                    payload);
            }

            if (!string.IsNullOrWhiteSpace(@event.ShopId))
            {
                await _hubContext.Clients.Group($"store:{@event.ShopId}").SendAsync(
                    "OrderStatusChanged",
                    payload);
            }

            // 3. บรอดแคสต์ SignalR ไปยังกลุ่ม Customer ของออเดอร์
            if (!string.IsNullOrWhiteSpace(@event.CustomerId))
            {
                await _hubContext.Clients.Group($"customer:{@event.CustomerId}").SendAsync(
                    "OrderStatusChanged",
                    payload);

                // 4. พุชแจ้งเตือน FCM แจ้งความคืบหน้าถึง Customer ใน Background
                var correlationId = @event.CorrelationId ?? Guid.NewGuid().ToString();
                _ = _backgroundTaskQueue.QueueBackgroundWorkItemAsync(async (serviceProvider, cancellationToken) =>
                {
                    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
                    {
                        try
                        {
                            var scopedFcmService = serviceProvider.GetRequiredService<IFcmNotificationService>();
                            var statusThai = GetStatusThaiDescription(@event.NewState);
                            await scopedFcmService.SendNotificationToUserAsync(
                                @event.CustomerId,
                                "อัปเดตสถานะออเดอร์ของคุณ",
                                $"ออเดอร์ของคุณรหัส ORD-{@event.RefNumber.ToString("D6")} สถานะเปลี่ยนเป็น: {statusThai}",
                                new Dictionary<string, string>
                                {
                                    { "orderId", @event.OrderId },
                                    { "status", @event.NewState.ToString() },
                                    { "type", "ORDER_STATUS_CHANGED" }
                                });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send FCM status notification to Customer {CustomerId}", @event.CustomerId);
                        }
                    }
                });
            }

            // 5. หากออเดอร์เสร็จสิ้นหรือยกเลิก ให้ยิงแจ้งเตือน FCM หาคนขับด้วย
            if (!string.IsNullOrWhiteSpace(@event.AssignedRiderId) && 
                (@event.NewState == OrderState.COMPLETED || @event.NewState == OrderState.CANCELLED))
            {
                var correlationId = @event.CorrelationId ?? Guid.NewGuid().ToString();
                _ = _backgroundTaskQueue.QueueBackgroundWorkItemAsync(async (serviceProvider, cancellationToken) =>
                {
                    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
                    {
                        try
                        {
                            var db = serviceProvider.GetRequiredService<DBHandlerCore>();

                            var riderUser = await db.GetQuery<User>()
                                .AsNoTracking()
                                .FirstOrDefaultAsync(u => u.RiderId == @event.AssignedRiderId, cancellationToken);

                            if (riderUser != null)
                            {
                                var scopedFcmService = serviceProvider.GetRequiredService<IFcmNotificationService>();
                                var statusThai = @event.NewState == OrderState.COMPLETED ? "เสร็จสิ้นแล้ว" : "ยกเลิกแล้ว";
                                await scopedFcmService.SendNotificationToUserAsync(
                                    riderUser.Id,
                                    "อัปเดตสถานะออเดอร์จัดส่ง",
                                    $"ออเดอร์จัดส่งรหัส ORD-{@event.RefNumber.ToString("D6")} ได้{statusThai}",
                                    new Dictionary<string, string>
                                    {
                                        { "orderId", @event.OrderId },
                                        { "status", @event.NewState.ToString() },
                                        { "type", "ORDER_FINISHED" }
                                    });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send FCM completion notification to Rider {RiderId}", @event.AssignedRiderId);
                        }
                    }
                });
            }
        }

        private static string GetStatusThaiDescription(OrderState state)
        {
            return state switch
            {
                OrderState.CREATED => "สร้างออเดอร์สำเร็จ",
                OrderState.MATCHING => "กำลังค้นหาคนขับ",
                OrderState.OFFERING => "พบคนขับแล้ว กำลังเสนอรับงาน",
                OrderState.ASSIGNED => "ได้รับคนขับเรียบร้อย",
                OrderState.PICKING_UP => "คนขับกำลังเดินทางไปรับอาหาร",
                OrderState.DELIVERING => "อาหารของคุณอยู่ระหว่างการจัดส่ง",
                OrderState.COMPLETED => "จัดส่งสำเร็จเรียบร้อยแล้ว",
                OrderState.CANCELLED => "ออเดอร์ถูกยกเลิก",
                _ => state.ToString()
            };
        }
    }
}


