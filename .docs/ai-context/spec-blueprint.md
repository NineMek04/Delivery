# 📜 Active Architecture Blueprint (V0.9.0)

### 1. Current Active Stack
- **Database:** PostgreSQL 16 + PostGIS extension (SRID 4326 / WGS84) พร้อมโครงข่าย GiST Spatial Index scan บนพิกัดหนาแน่นเมืองอุดรธานี
- **Speed Layer:** Redis 7 (พอร์ต 6379) จัดคิวล็อกออเดอร์นับถอยหลัง 30 วินาที และบัฟเฟอร์สัญญาณพิกัดคนขับสด
- **Message Broker:** RabbitMQ 3 (AMQP พอร์ต 5672, คอนโซลแอดมิน 15672) แลกเปลี่ยนอีเวนต์ผ่าน Exchange ชื่อ `delivery_event_bus` ชนิด `direct`
- **Realtime Layer:** SignalR Core (พอร์ต 5000) รองรับการดักจับ JWT ผ่าน URL Query String พารามิเตอร์ `?access_token=...` สำหรับเชื่อมอุปกรณ์ Flutter ภายนอก

### 2. Core End-to-End Live Routing Sequence
Admin/Customer API ➡️ เรียกประมวลระยะทาง OSRM Local Engine (`delivery-osrm:5000` algorithm ch) ➡️ โยนพิกัดสดไรเดอร์และออเดอร์ข้ามไปหา Python FastAPI (`/api/v1/predict-eta`) คำนวณช่วงชั่วโมงเร่งด่วน (Rush Hour Multiplier 1.3x - 1.5x) ➡️ สลักเวลาเสร็จสิ้นประเมิน `ExpectedDeliveryTime` ลง PostgreSQL DB ทันทีตั้งแต่เกิดคำสั่งซื้อ ➡️ พ่นงานส่งต่อเข้าท่อ RabbitMQ แบบออฟไลน์
