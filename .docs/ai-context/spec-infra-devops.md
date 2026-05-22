---
module: Infrastructure, Telemetry Streaming & Operational SLO Targets
dependencies: [ "SignalR Windowed Broadcast", "Angular Change Detection Guard" ]
---

# ⚙️ Telemetry Streaming Optimization & Operational Limits (SLO)

## 1. ระบบควบคุม Backpressure และหน่วงเวลา (Windowed Push Architecture)
เพื่อป้องกันหน้าจอระบบ Admin Dashboard เกิดอาการหน่วง ค้าง หรือ DOM Re-rendering ถี่เกินไป ระบบหลังบ้านจะทำหน้าที่เป็นเขื่อนกั้นแรงกระแทกข้อมูลความถี่สูง (High-frequency GPS จากไรเดอร์ 100+ จุด/วิ):
- **RAM-Only Atomic Increment:** สัญญาณ GPS ขาเข้าจะวิ่งไปบวกตัวเลข Metric ขึ้นทีละ 1 ติ๊กแบบ Thread-safe บน RAM ผ่าน `TelemetryAggregator` ทันที โดยไม่มีการเรียกเขียนฮาร์ดดิสก์ฐานข้อมูลในจังหวะนี้
- **Database Throttle Policy:** บังคับดักให้ `TelemetryBroadcastWorker` ยิงคำสั่ง Query สรุปภาพสถานะสดจาก PostgreSQL **ทุกๆ 5 วินาทีเท่านั้น** เพื่อจำกัดอัตราการกดดันฐานข้อมูลให้คงที่
- **2-Second Windowed Push:** ทุกๆ **2 วินาที** หลังบ้านจะทำการแพ็กรวมข้อมูลสรุป Telemetry ยิงข้ามท่อ SignalR ก้อนเดียว (Event: `'TelemetryUpdated'`) ไปหาหน้าจอแอดมิน

## 2. 🎯 มาตรฐานและข้อจำกัดเชิงปฏิบัติการ (Operational Limits & SLO Targets)
โปรเจกต์นี้ได้รับการควบคุมขีดจำกัดทรัพยากรและการประมวลผล เพื่อให้สอดคล้องกับแนวคิดความมั่นคงเชิงระบบ (Service Level Objectives):

- ⏱️ **Max telemetry broadcast rate: 0.5 Hz** (หลังบ้านจะสั่งยิงสรุปสถิติจราจรสดออกไปหาหน้าจอแอดมินสูงสุดแค่ 1 ครั้งต่อ 2 วินาทีเท่านั้น ผ่านระบบ 0.5 Hz Filter Noise Guard สกัดอาการกล่องกราฟกะพริบกระตุก)
- 🔒 **Max Redis lock TTL: 30s** (ระยะเวลาการจองและล็อกงานให้ไรเดอร์ตัดสินใจในระบบ RAM มีเวลาเด็ดขาดสูงสุด 30 วินาที หากเกินเวลาต้องถอน Lock คืนสู่ระบบกลางทันทีป้องกันสภาวะแอปค้าง)
- 🔄 **Max retry attempts: 5** (จำนวนครั้งสูงสุดในการพยายามประมวลผลข้อความบน RabbitMQ ก่อนที่จะสั่งย้ายข้อมูลเข้าสู่ตู้ DLQ เพื่อไม่ให้คิวหลักเกิดสภาวะคอขวดสะสม)
- 🏎️ **Max queue processing delay target: < 3s** (เป้าหมายสูงสุดความล่าช้าในการสับเปลี่ยนข้อความผ่านระบบ Message Broker ต้องน้อยกว่า 3 วินาที เพื่อการันตีความเป็นระบบเวลาจริงยืดหยุ่นสูง)

## 3. Hard Operational Limits
- Max SignalR connections target: 500
- Max GPS ingestion target: 100/sec
- Max telemetry payload size: 16 KB
- Max RabbitMQ consumer lag target: < 3s
