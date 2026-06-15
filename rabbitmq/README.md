# RabbitMQ Event Broker Subsystem (rabbitmq/README.md)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือการบริหารจัดการ คอนฟิก และการจัดหมวดหมู่ข้อความเหตุการณ์ (Events) สำหรับตู้บริการ **RabbitMQ Message Broker** ในระบบจัดส่งอัจฉริยะ

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
`rabbitmq` ทำหน้าที่เป็นสื่อกลางส่งผ่านข้อความเหตุการณ์ (Decoupled Message Broker) เพื่อช่วยแยกการประสานงานของบริการหลังบ้านแบบ Asynchronous:
1.  **Work Load Isolation:** ช่วยหลีกเลี่ยงไม่ให้การประมวลผลระยะเวลายาวนาน (เช่น การ Snap พิกัด OSRM หรือจัดสรรออเดอร์ใหม่) ไปขัดขวางสายท่อหลักของ HTTP REST API
2.  **Telemetry Routing:** รองรับทราฟฟิกพิกัด GPS ความเร็วสูงที่ส่งต่อเนื่องมาจากคนขับ เพื่อกระจายส่งต่อไปยัง Worker เก็บประวัติพิกัด
3.  **Observability Ingestion:** ส่งออกข้อมูลสถิติของคิวไปหา Prometheus Scraper

---

## 2. โครงสร้างและการตั้งค่าพอร์ต (Port & Plugin Configurations)

ตู้ RabbitMQ รันบริการ AMQP มาตรฐานและตั้งค่าเสริมความพร้อมดังนี้:
*   **พอร์ตหลัก (AMQP Port):** `5672` (สำหรับ Backend API ส่งและดักรับข้อความ)
*   **แผงควบคุมหลัก (Management Dashboard):** `15672` (สำหรับแอดมินเข้าไปดูข้อความค้างคิว)
*   **พอร์ต Metrics (Prometheus Ingest):** `15692` (ส่งข้อมูล metrics ไปหา Prometheus)
*   **Enabled Plugins ([enabled_plugins](file:///c:/Users/ASUS/Desktop/Project/Delivery/rabbitmq/enabled_plugins)):**  
    ระบบเปิดใช้งานปลั๊กอิน `rabbitmq_management` และ `rabbitmq_prometheus` อัตโนมัติ เพื่อสนับสนุนระบบดักจับ Metrics ของ DevOps

---

## 3. หมวดหมู่อีเวนต์และกฎการตั้งชื่อ (Event Classification & Naming Conventions)

ตามข้อกำหนดใน **`AGENTS.md §2`** ระบบแยกเหตุการณ์ออกเป็น 3 ประเภทอย่างเด็ดขาด ห้ามเขียนปนกัน:

1.  **Domain Events (ภายใน):**  
    เหตุการณ์ที่เกิดขึ้นและสิ้นสุดภายในโมดูลเดียวกันของ Backend API (คลาส C# ภายใน) ห้ามส่งออกนอก RabbitMQ
2.  **Integration Events (ข้ามระบบ):**  
    เหตุการณ์ส่งข้ามตู้คอนเทนเนอร์เพื่อแลกเปลี่ยนข้อมูล (เช่น Backend -> OsrmSnapWorker) บังคับใช้รูปแบบการตั้งชื่อ:  
    `[Domain][Action]IntegrationEvent`  
    *ตัวอย่างอีเวนต์หลัก:*
    -   `OrderCreatedIntegrationEvent`: เมื่อมีออเดอร์สร้างใหม่ ส่งต่อหา AI Engine เพื่อคิวจัดเส้นทาง
    -   `OrderStatusChangedIntegrationEvent`: เมื่อสถานะออเดอร์เปลี่ยนผ่าน State Machine
    -   `RiderLocationUpdatedIntegrationEvent`: เมื่อ Rider ยิงพิกัดอัปเดต ส่งหา OsrmSnapWorker เพื่อแนบเส้นถนน
3.  **Telemetry Events (เรียลไทม์ความถี่สูง):**  
    ข้อมูลดิบสตรีมสดพิกัดผ่านระบบ WebSockets SignalR ไปหน้า Angular Dashboard (ไม่เอาเข้าคิว RabbitMQ เพื่อป้องกัน Queue Overflow)

---

## 4. กฎการทำงานซ้ำอย่างปลอดภัย (Idempotency Rule)

> [!IMPORTANT]
> **ระบบป้องกันข้อมูลซ้ำซ้อน (Consumer Idempotency Guard):**
> เพื่อป้องกันการทำรายการซ้ำซ้อนในกรณีเครือข่ายดีเลย์และ RabbitMQ ส่ง Message ซ้ำ (At-Least-Once Delivery):
> -   **ห้ามลบกฎนี้เด็ดขาด:** Consumer ทุกตัวบนหลังบ้านที่สมัครรับข้อมูลจาก RabbitMQ จะต้องนำรหัสอีเวนต์ (Event ID) ไปเช็คกับตาราง **`ProcessedEvents`** บน PostgreSQL เสมอก่อนรันลอจิกทำธุรกรรมใดๆ หากพบประวัติว่าเคยประมวลผลสำเร็จแล้ว ให้กดปฏิเสธ (Drop/Acknowledge) อีเวนต์นั้นทิ้งทันที

---

## 5. การรับมือข้อผิดพลาดและระบบคิวสำรอง Dead Letter Exchange (DLX)

> [!WARNING]
> **วิกฤต Poison Message ค้างคิวหลัก**
> หาก Message ที่วิ่งเข้ามาประมวลผลเกิดข้อผิดพลาดรุนแรงระดับ Business logic หรือ Database (เช่น เกิด Exception ชั่วคราว) และไม่มีกลไกควบคุมการวนซ้ำ Consumer จะกดยกเลิกและวน Message กลับไปที่หัวคิวใหม่ตลอดไป (Infinite Requeue) ส่งผลให้เกิด **Poison Message** ซึ่งจะสูบความร้อน CPU และขัดขวางไม่ให้ออเดอร์ใหม่ ๆ ได้รับการประมวลผล คิวหลักจะล้นและระบบจะหยุดชะงัก
>
> **นโยบายการป้องกันด้วย Dead Letter Exchange (DLX):**
> 1. **จำกัดจำนวนการพยายาม (Retry Limits):**
>    Consumer ทุกตัวจะต้องตรวจสอบจำนวนครั้งในการประมวลผลผิดพลาด (เช่น ผ่านฟิลด์ headers `x-death` หรือนับจำนวน Retry ในหน่วยความจำ/Redis)
> 2. **การโยนลงกระบะทราย (DLX Routing):**
>    หากระบบรัน Consumer ทำงานล้มเหลวติดต่อกันเกิน **3 ถึง 5 ครั้ง** ระบบจะต้องยกเลิกการ Requeue (Acknowledge หรือ Reject ด้วยพารามิเตอร์ `requeue=false`) เพื่อให้ RabbitMQ เตะ Message ดังกล่าวออกจากคิวหลักและส่งต่อไปยัง **Dead Letter Exchange (DLX)** โดยอัตโนมัติ
> 3. **โครงสร้างคิววิเคราะห์ปัญหา (DLQ):**
>    - **Dead Letter Exchange (DLX):** `delivery.dead-letter.exchange`
>    - **Dead Letter Queue (DLQ):** `delivery.dead-letter.queue`
>    - ข้อความที่ถูกเตะไปลง DLQ จะถูกบันทึกพร้อมหัวเรื่องสาเหตุความเสียหาย (Error details) เพื่อให้แอดมินนำมาดึงตรวจแก้และแก้ไขระบบต่อไปโดยไม่ขัดขวางเส้นทางขนส่งหลัก

---

## 6. วิธีการตรวจสอบระบบคิวจราจร (Verification Steps)

1.  เริ่มรัน Container ของ RabbitMQ:
    ```bash
    docker-compose up -d rabbitmq
    ```
2.  เข้าหน้าจอแผงควบคุม:
    *   **URL:** `http://localhost:15672`
    *   **บัญชีเริ่มต้น:** Username: `guest` | Password: `guest`
3.  ตรวจสอบการดึง Metrics:
    *   ทดสอบเปิด URL `http://localhost:15692/metrics` หากพบค่าข้อความสถิติแสดงว่าระบบ Prometheus Exporter ฝั่ง RabbitMQ พร้อมทำงาน

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Context)
*   [Integration Event and Consumer Details Spec](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-consistency.md)
*   [Dead Letter Exchange and Error Queue Spec](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-dead-letter.md)
