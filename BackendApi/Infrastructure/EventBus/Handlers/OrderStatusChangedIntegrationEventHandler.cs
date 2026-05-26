using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BackendApi.Core.DataHandlers;
using BackendApi.Core.StateMachines;
using BackendApi.Hubs;
using BackendApi.Models;
using BackendApi.Services.Notifications;
using BackendApi.Infrastructure.EventBus.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

        public OrderStatusChangedIntegrationEventHandler(
            IHubContext<TrackingHub> hubContext,
            IFcmNotificationService fcmService,
            IServiceScopeFactory scopeFactory,
            ILogger<OrderStatusChangedIntegrationEventHandler> logger)
        {
            _hubContext = hubContext;
            _fcmService = fcmService;
            _scopeFactory = scopeFactory;
            _logger = logger;
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

            // 1. บรอดแคสต์ SignalR ไปยังกลุ่ม Admin
            await _hubContext.Clients.Group("admins").SendAsync(
                "OrderStatusChanged",
                @event.OrderId,
                @event.NewState.ToString());

            // 2. บรอดแคสต์ SignalR ไปยังกลุ่ม Rider ที่รับผิดชอบ
            if (!string.IsNullOrWhiteSpace(@event.AssignedRiderId))
            {
                await _hubContext.Clients.Group($"rider:{@event.AssignedRiderId}").SendAsync(
                    "OrderStatusChanged",
                    @event.OrderId,
                    @event.NewState.ToString());
            }

            // 3. บรอดแคสต์ SignalR ไปยังกลุ่ม Customer ของออเดอร์
            if (!string.IsNullOrWhiteSpace(@event.CustomerId))
            {
                await _hubContext.Clients.Group($"customer:{@event.CustomerId}").SendAsync(
                    "OrderStatusChanged",
                    @event.OrderId,
                    @event.NewState.ToString());

                // 4. พุชแจ้งเตือน FCM แจ้งความคืบหน้าถึง Customer ใน Background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var scopedFcmService = scope.ServiceProvider.GetRequiredService<IFcmNotificationService>();
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
                });
            }

            // 5. หากออเดอร์เสร็จสิ้นหรือยกเลิก ให้ยิงแจ้งเตือน FCM หาคนขับด้วย
            if (!string.IsNullOrWhiteSpace(@event.AssignedRiderId) && 
                (@event.NewState == OrderState.COMPLETED || @event.NewState == OrderState.CANCELLED))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<DBHandlerCore>();

                        var riderUser = await db.GetQuery<User>()
                            .AsNoTracking()
                            .FirstOrDefaultAsync(u => u.RiderId == @event.AssignedRiderId);

                        if (riderUser != null)
                        {
                            var scopedFcmService = scope.ServiceProvider.GetRequiredService<IFcmNotificationService>();
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
