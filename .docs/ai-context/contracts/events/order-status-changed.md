# 🐇 Event Contract: OrderStatusChangedIntegrationEvent

- **Producer:** `OrderService` (Backend API Engine)
- **Consumer:** `NotificationService`, `AnalyticsService`, `AiEngineDispatcher`
- **Retry Policy:** Exponential Backoff (ลองซ้ำสูงสุด 5 ครั้ง เริ่มหน่วงที่ 2 วินาที) ➡️ หลุดเข้า Dead Letter Queue (DLQ)
- **Ordering Guarantees:** ⚠️ Strict Ordering Required. กำหนดค่าหัว `RoutingKey = order.id` เพื่อล็อกกลุ่มข้อความให้ลงพาร์ทิชันคิวเดียวกันบน RabbitMQ เสมอ ป้องกันสภาวะอีเวนต์วิ่งสวนทาง (เช่น ออเดอร์ส่งของสำเร็จ `COMPLETED` วิ่งแซงจุดรับของ `PICKING_UP`)

## 📦 Payload Schema Structure
```json
{
  "schemaVersion": 1,
  "eventId": "uuid-v4-unique-message-id",
  "correlationId": "uuid-v4-root-transaction-id",
  "causationId": "uuid-v4-parent-trigger-id",
  "timestamp": "2026-05-22T11:46:00Z",
  "payload": {
    "orderId": "string-uuid",
    "orderNumber": "string (e.g. ORD-000123)",
    "oldState": "string (Enum Status)",
    "newState": "string (Enum Status)",
    "riderId": "string-uuid"
  }
}
```
