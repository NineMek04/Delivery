using System;
using System.Collections.Generic;
using System.Security.Claims;
using BackendApi.Core.Models;
using BackendApi.Core.StateMachines;
using BackendApi.Core.Constants;
using BackendApi.Models;
using BackendApi.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using BackendApi.Core.DataHandlers;

namespace BackendApi.Hubs
{
    /// <summary>
    /// Hub สำหรับจัดการห้องแชทเรียลไทม์จำกัดสิทธิ์และขอบเขตตามออเดอร์ (Order-bound In-App Messaging)
    /// </summary>
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly DBHandlerCore _db;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(DBHandlerCore db, ILogger<ChatHub> logger)
        {
            _db = db;
            _logger = logger;
        }

        private static string OrderChatGroup(string orderId) => $"order-chat:{orderId}";

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

            if (userId != null && role == AuthConstants.RiderRole)
            {
                var user = await _db.GetQuery<User>()
                    .FirstOrDefaultAsync(u => u.Id == userId);
                if (user?.RiderId != null)
                {
                    Context.Items["RiderId"] = user.RiderId;
                }
            }

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// ไรเดอร์ ลูกค้า หรือร้านค้า ขอเข้าร่วมห้องแชทของออเดอร์
        /// เมื่อผ่านการตรวจสอบสิทธิ์ ระบบจะดึงประวัติข้อความเก่าส่งกลับไปให้ผ่าน ChatHistoryReceived
        /// </summary>
        public async Task JoinOrderChat(string orderId)
        {
            CheckRateLimit();
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

            if (userId == null || role == null)
            {
                throw new HubException("Unauthorized connection attempt.");
            }

            // ตรวจสอบความถูกต้องของออเดอร์
            var order = await _db.GetQuery<Order>()
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
            {
                throw new HubException("Order not found.");
            }

            // จำกัดการดึงประวัติเฉพาะออเดอร์ที่เกิดไม่เกิน 7 วัน
            if (order.CreatedAt < DateTime.UtcNow.AddDays(-7))
            {
                throw new HubException("Cannot access chat history for orders older than 7 days.");
            }

            // ตรวจสอบสิทธิ์การเข้าคุยในออเดอร์นี้
            bool isAuthorized = await VerifyUserAccess(order, userId, role);
            if (!isAuthorized)
            {
                throw new HubException("You do not have permission to join this chat.");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, OrderChatGroup(orderId));
            _logger.LogInformation("User {UserId} ({Role}) joined chat group for Order {OrderId}", userId, role, orderId);

            // ดึงประวัติแชทล่าสุด 50 ข้อความเรียงย้อนหลัง
            var history = await _db.GetQuery<ChatMessage>()
                .Where(m => m.OrderId == orderId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(50)
                .Select(m => new
                {
                    m.Id,
                    m.OrderId,
                    m.SenderId,
                    m.SenderRole,
                    m.Message,
                    m.CreatedAt
                })
                .ToListAsync();

            // Reverse ข้อความกลับเพื่อให้แสดงผลจากเก่าไปหาใหม่ (Chronological Order)
            history.Reverse();

            await Clients.Caller.SendAsync("ChatHistoryReceived", orderId, history);
        }

        /// <summary>
        /// ส่งข้อความแชทใหม่เข้าไปในห้องสนทนาของออเดอร์
        /// </summary>
        public async Task SendMessage(string orderId, string messageText)
        {
            CheckRateLimit();
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

            if (userId == null || role == null)
            {
                throw new HubException("Unauthorized connection attempt.");
            }

            if (string.IsNullOrWhiteSpace(messageText))
            {
                throw new HubException("Message content cannot be empty.");
            }

            // [DoS FIX] Prevent oversized payloads from being written to the database.
            // Aligned with ChatMessage.Message [MaxLength(1000)] constraint.
            const int MaxMessageLength = 1000;
            if (messageText.Length > MaxMessageLength)
            {
                throw new HubException($"Message exceeds the maximum allowed length of {MaxMessageLength} characters.");
            }

            // ตรวจสอบความถูกต้องของออเดอร์
            var order = await _db.GetQuery<Order>()
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
            {
                throw new HubException("Order not found.");
            }

            // ตรวจสอบสิทธิ์ส่งข้อความ
            bool isAuthorized = await VerifyUserAccess(order, userId, role);
            if (!isAuthorized)
            {
                throw new HubException("You do not have permission to send messages to this chat.");
            }

            // จำกัดการส่งข้อความเฉพาะช่วงเวลาปฏิบัติงาน (ASSIGNED ถึง DELIVERING เท่านั้น)
            if (order.State != OrderState.ASSIGNED && 
                order.State != OrderState.PICKING_UP && 
                order.State != OrderState.DELIVERING)
            {
                throw new HubException("Cannot send messages because the order has been completed or cancelled.");
            }

            // แมปชื่อบทบาทเพื่อส่งแสดงผลให้เข้าใจง่าย
            string senderRole = role switch
            {
                AuthConstants.RiderRole => "Rider",
                AuthConstants.CustomerRole => "Customer",
                AuthConstants.StorePartnerRole => "Shop",
                _ => "Admin"
            };

            // บันทึกข้อความลงฐานข้อมูล PostgreSQL ผ่าน DBHandlerCore
            var message = new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = orderId,
                SenderId = userId,
                SenderRole = senderRole,
                Message = messageText.Trim()
            };

            _db.InsertObject(message);
            await _db.CommitChangesAsync();

            // ส่งกระจายสัญญาณให้คนขับ ลูกค้า ร้านค้า และแอดมินในกลุ่มแชททันที
            await Clients.Group(OrderChatGroup(orderId)).SendAsync("MessageReceived", orderId, new
            {
                message.Id,
                message.OrderId,
                message.SenderId,
                message.SenderRole,
                message.Message,
                message.CreatedAt
            });
        }

        private async Task<bool> VerifyUserAccess(Order order, string userId, string role)
        {
            if (role == AuthConstants.AdminRole || role == AuthConstants.DispatcherRole)
            {
                return true;
            }

            if (role == AuthConstants.RiderRole)
            {
                // ใช้ RiderId จากแคชใน Context.Items เพื่อหลีกเลี่ยง SQL Query Spam
                if (Context.Items.TryGetValue("RiderId", out var cachedRiderId) && cachedRiderId is string riderId)
                {
                    return order.AssignedRiderId == riderId;
                }

                // Fallback ดึงค่าจาก DB ในกรณีจำกัด (หากหลุดแคช)
                var user = await _db.GetQuery<User>()
                    .FirstOrDefaultAsync(u => u.Id == userId);
                if (user?.RiderId != null)
                {
                    Context.Items["RiderId"] = user.RiderId;
                    return order.AssignedRiderId == user.RiderId;
                }
                return false;
            }

            if (role == AuthConstants.CustomerRole)
            {
                return order.CustomerId == userId;
            }

            if (role == AuthConstants.StorePartnerRole)
            {
                // ดึง ShopId จาก JWT Claims ตรงๆ (shop_id) ไม่ต้องเรียกฐานข้อมูลตาราง Users
                var shopId = Context.User?.FindFirst("shop_id")?.Value;
                return shopId != null && order.ShopId == shopId;
            }

            return false;
        }

        private void CheckRateLimit()
        {
            var now = DateTime.UtcNow;
            const string timestampsKey = "InvocationTimestamps";
            const int MaxRequests = 5;
            const int WindowSeconds = 5;

            if (!Context.Items.TryGetValue(timestampsKey, out var obj) || obj is not List<DateTime> timestamps)
            {
                timestamps = new List<DateTime>();
                Context.Items[timestampsKey] = timestamps;
            }

            // Remove expired timestamps
            var cutoff = now.AddSeconds(-WindowSeconds);
            timestamps.RemoveAll(t => t < cutoff);

            if (timestamps.Count >= MaxRequests)
            {
                throw new HubException("Rate limit exceeded. Please wait a moment before sending more messages.");
            }

            timestamps.Add(now);
        }
    }
}
