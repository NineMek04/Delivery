# 🪵 Unified Trace Correlation Logging

เพื่อการวิเคราะห์หาสาเหตุของปัญหาในระบบได้อย่างถูกต้องแบบรวดเร็ว ระบบบังคับส่งข้อมูล Context ประจำตัวลงไปกับไฟล์ Serilog ทุกครั้ง:

- **สิ่งที่ต้องบันทึก:** ในทุกล็อก (Logs) ที่เกิดขึ้นในระบบจะต้องประกอบไปด้วยข้อมูลระบุทรานแซกชันดังต่อไปนี้เสมอ:
  - `CorrelationId` (สำหรับเชื่อมโยงการไหลของทราฟฟิกข้าม Service)
  - `OrderId` (ถ้ามี เพื่อดูพฤติกรรมคำสั่งซื้อ)
  - `RiderId` (ถ้ามี เพื่อเช็คพฤติกรรมการเคลื่อนที่ของไรเดอร์)
- **การทำงานจริง:** ใช้การ Push Property ลงบน Serilog Context ใน Middleware และ RabbitMQ Message Envelope:
  ```csharp
  using (LogContext.PushProperty("CorrelationId", correlationId))
  using (LogContext.PushProperty("OrderId", orderId))
  using (LogContext.PushProperty("RiderId", riderId))
  {
      _logger.LogInformation("Processing rider offer acceptance...");
  }
  ```
