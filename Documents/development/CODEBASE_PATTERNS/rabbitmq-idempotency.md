# 🔒 การตรวจสอบความซ้ำซ้อนและการจัดคิวทำ Idempotency (RabbitMQ Idempotent Consumers)

เพื่อป้องกันปัญหาการประมวลผลซ้ำซ้อน (Double Processing) ที่เกิดขึ้นได้ทั่วไปในสถาปัตยกรรม Microservices เมื่อเครือข่ายขัดข้องและมีการ Retry ส่งข้อความซ้ำ (At-Least-Once Delivery):

- **กลไกการทำ Idempotency:**
  - ตัวรับข่าวสาร (Consumer) ของ RabbitMQ ทุกตัว เช่น [RabbitMqEventBus.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Infrastructure/EventBus/RabbitMqEventBus.cs#L455) หรือ [GpsRabbitMqConsumerWorker.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/FleetTracking/Telemetry/GpsRabbitMqConsumerWorker.cs#L307) จะต้องทำการตรวจสอบค่ารหัสอีเวนต์คู่กับชื่อตัวจัดการ (`EventId` และ `HandlerName`) เสมอก่อนรัน Logic หลักผ่านคิวรี:
    ```csharp
    var alreadyProcessed = await dbContext.ProcessedEvents.AnyAsync(pe => pe.EventId == eventId && pe.HandlerName == handlerName);
    ```
  - หากพบข้อมูลอยู่แล้ว ระบบจะทำการยกเลิก (Skip / Acknowledge Drop) ทันที
  - หากยังไม่เคยประมวลผลสำเร็จ จะรันธุรกรรมทางธุรกิจและบันทึกคีย์ลงตาราง `ProcessedEvents` ในทรานแซกชันเดียวกัน
- **การกวาดล้างข้อมูลประวัติเก่าโดยอัตโนมัติ (Automated Garbage Collection):**
  - ตัวจัดการ [DbMaintenanceWorker.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/BackgroundWorkers/DbMaintenanceWorker.cs) จะทำงานทุก ๆ 1 ชั่วโมงด้วย `PeriodicTimer` เพื่อทำการลบประวัติ `ProcessedEvents` ที่มีอายุเกิน 24 ชั่วโมงทิ้งผ่าน bulk `ExecuteDeleteAsync` เพื่อไม่ให้ตารางโตเกินจำเป็น
  - มีการจัดทำดัชนีระบุประวัติเพื่อความเร็วขารันในภายหลัง: `CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_ProcessedEvents_ProcessedAt" ON "ProcessedEvents" ("ProcessedAt");` เพื่อให้การทำงานในส่วนของ Pruning รวดเร็วและปราศจาก Table Lock บนตารางหลัก
