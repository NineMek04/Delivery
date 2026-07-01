// =================================================================
// Controllers/Business/
// =================================================================
// โฟลเดอร์นี้สำหรับ Controller ที่มี Business Logic ซับซ้อน
// ไม่ควรใช้ CrudControllerBase — ให้เขียน Action แต่ละตัวแบบ Explicit
//
// ตัวอย่าง Entity ที่ควรอยู่ที่นี่:
//   - OrderController      (จัดการออเดอร์ + เปลี่ยนสถานะ + แจ้งเตือน)
//   - DispatchController   (จัดส่ง + เรียก route optimizer)
//   - TrackingController   (Real-time tracking + SignalR)
//
// ตัวอย่างการสร้าง:
//
// [Route("api/v1/orders")]
// public class OrderController : DeliveryControllerBase
// {
//     [HttpPost("{id}/dispatch")]
//     public async Task<ActionResult> DispatchOrder(string id) { ... }
// }
