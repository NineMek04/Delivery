# ⚖️ Data Consistency, State Authorities & Tracing Specs

## 1. ขอบเขตสิทธิ์การสลับสถานะ (State Transition Authority Rules)
เพื่อตัดปัญหา Multiple Writers Problem (เลเยอร์โค้ดแย่งกันเขียนสถานะออเดอร์มั่ว) ระบบจะล็อกสิทธิ์ให้เฉพาะชิ้นส่วนซอฟต์แวร์เหล่านี้เท่านั้นที่มีสิทธิ์ Mutation ข้อมูลสถานะ:

- **`CREATED`**: อนุมัติสิทธิ์ขาดให้ `OrderService` เท่านั้น (REST API Endpoint ขาเข้า)
- **`MATCHING`**: อนุมัติสิทธิ์ขาดให้ `DispatchEngineBackgroundWorker` (รันคิวจัดหาคนขับ)
- **`OFFERING`**: อนุมัติสิทธิ์ขาดให้ `Python FastAPI AI Engine` (หลังประมวลผล 2-Stage Scoring ประเมินค่าความมั่นใจเสร็จ)
- **`ASSIGNED`**: อนุมัติสิทธิ์ขาดให้ `Rider Accept Flow` ควบคุมผ่านกลไก **Redis Distributed Lock (RAM)** ป้องกันคนขับสองคนรัวกดแย่งงานชิ้นเดียวกัน
- **`PICKING_UP` / `DELIVERING`**: อนุมัติสิทธิ์ขาดให้ `Rider Action Hub` (รับสัญญาณดิบข้ามท่อ WebSocket มาจาก Flutter App)
- **`COMPLETED` / `CANCELLED`**: อนุมัติสิทธิ์ขาดให้ `Delivery Confirmation Worker` (ประมวลผลอีเวนต์หลังพ้นระยะประชิดพิกัดภูมิศาสตร์)

## 2. ระบบป้องกันการทำธุรกรรมซ้ำ (Distributed Idempotency Architecture)
- **Message Ledger Check:** ทุกๆ ระบบผู้รับฟังอีเวนต์ (RabbitMQ Consumers) ก่อนที่จะรัน Business Logic ต้องนำค่าหัวรหัสจำเพาะ `eventId` ไปวิ่งตรวจสอบกับตาราง `ProcessedEvents` บนฐานข้อมูลหลัก PostgreSQL เสมอ หากพบคีย์ไอดีนี้บันทึกอยู่แล้ว ➡️ ให้สั่ง **Acknowledge (ACK) ดีดข้อความทิ้งทันที** ห้ามรัน Logic ซ้ำสองเด็ดขาด
- **EF Core Concurrency Guard:** บนตารางฐานข้อมูลหลักธุรกรรม (`Orders`, `Riders`) ต้องสลักฟิลด์ตรวจเช็คแถวข้อมูล **`RowVersion [Timestamp]`** เพื่อให้ฐานข้อมูลตีตกคำสั่งเปลี่ยนสถานะที่เกิดจากสภาวะอ่านข้อมูลคลาดเคลื่อนย้อนยุค (Stale Reads)
- **Lightweight Saga Semantics:** ห้ามตั้งตู้ Coordinator หรืองาน Orchestration Engine ขนาดใหญ่ให้ระบบหน่วงช้า หากขั้นตอน async เกิดข้อผิดพลาดกลางคัน ให้ประยุกต์ใช้ **Compensating Action (ลอจิกคำสั่งชดเชยกรรม)** เช่น หากคนขับปฏิเสธงาน หรือระบบ Offer Timeout นับถอยหลังหมดเวลา 30 วินาที ให้สั่งล้างข้อมูลคิวใน Redis แล้วเปลี่ยนสถานะออเดอร์กลับไปเป็น `PENDING` เพื่อเริ่มนับรอบ Dispatch จัดหาไรเดอร์ใหม่อัตโนมัติทันที
