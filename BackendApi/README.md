# Backend API Subsystem (.NET 8)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือสำหรับนักพัฒนาระบบ **Backend API** เพื่อทำความเข้าใจโครงสร้างสถาปัตยกรรม วิธีการรันระบบ และการไหลของข้อมูล (Workflow Flows) ที่ซับซ้อนภายในระบบจัดส่งอัจฉริยะ

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
Backend API ทำหน้าที่เป็นศูนย์กลางการทำงานของระบบจัดส่งทั้งหมด (Orchestration Engine):
1. **API Gateway & Core Logic:** ให้บริการ REST APIs สำหรับจัดการออเดอร์ ร้านค้า และพนักงานจัดส่ง (Rider)
2. **SignalR Transport Layer:** ทำหน้าที่เป็นท่อทางผ่านรับส่งข้อมูลเรียลไทม์ความเร็วสูง (พิกัด GPS ขาเข้า และข้อเสนองานออเดอร์ขาออก)
3. **Data Persistence & Spatial Processing:** ประมวลผลและจัดเก็บข้อมูลถาวรเชิงพื้นที่ลงใน PostgreSQL/PostGIS (SRID 4326) ร่วมกับประมวลผลข้อมูลชั่วคราวความเร็วสูงใน Redis Cache

---

## 2. ข้อกำหนดเบื้องต้นและการติดตั้ง (Prerequisites & Setup)

### ข้อกำหนดทางเทคนิค (Prerequisites)
*   **SDK:** .NET 8.0 SDK (ห้ามใช้ .NET 9.0 หรือเก่ากว่า)
*   **Database:** PostgreSQL 15 + PostGIS extension และ Redis (แนะนำรันผ่าน Docker Compose ในโฟลเดอร์หลัก)
*   **ความปลอดภัย:** คอนฟิกใน `.env` ที่โฟลเดอร์รูท (เช่น `JWT_SECRET`, `POSTGRES_PASSWORD`)

### วิธีการรันโปรเจกต์ภายในเครื่อง (Local Run)
1.  ตรวจสอบว่า Container `db` และ `redis` กำลังทำงานปกติ:
    ```powershell
    docker compose ps
    ```
2.  ไปที่ไดเรกทอรี Backend API:
    ```powershell
    cd c:\Users\ASUS\Desktop\Project\Delivery\BackendApi
    ```
3.  รันคำสั่ง Migration เพื่อปรับปรุงโครงสร้างฐานข้อมูลล่าสุด (ระบบมี Automatic Database Migration รันตอนเริ่มงานอยู่แล้ว แต่หากต้องการรันด้วยมือ):
    ```powershell
    dotnet ef database update --project ../BackendApi
    ```
4.  รันคอมไพล์และเปิดบริการ:
    ```powershell
    dotnet run --launch-profile "http"
    ```
    *(สามารถเข้าถึงระบบได้ทาง `http://localhost:5000` และดูสเปก API ได้ที่ `http://localhost:5000/swagger`)*

---

## 3. รูปแบบสถาปัตยกรรม (Architecture Pattern)
โครงสร้างโปรเจกต์ถูกจัดกลุ่มแยกออกจากกันแบบ **Feature-based & Decoupled Architecture** เพื่อความคล่องตัวในการแก้ไขระบบ:

*   **[Controllers](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Controllers/)**: ทางผ่านเข้าของ HTTP REST API สืบทอดโครงสร้างจาก [CrudControllerBase.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Core/CrudControllerBase.cs) สำหรับ CRUD ทั่วไป และ [DeliveryControllerBase.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Core/DeliveryControllerBase.cs) สำหรับ Business Logic พิเศษ
*   **[Core](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Core/)**: โมเดลข้อมูลหลัก, ค่าคงที่ระบบ และ **State Machines**
    - [OrderState.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Core/StateMachines/OrderState.cs): ควบคุมวงจรชีวิตออเดอร์ (`CREATED` -> `MATCHING` -> `OFFERING` -> `ASSIGNED` -> `PICKING_UP` -> `DELIVERING` -> `COMPLETED`/`CANCELLED`)
    - [RiderState.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Core/StateMachines/RiderState.cs): ควบคุมสถานะคนขับ (`OFFLINE`, `IDLE`, `RESERVED`, `BUSY`)
*   **[Features](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/)**: ส่วนธุรกิจหลักและลอจิกเฉพาะทาง
    - [AiRouting](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/AiRouting/): บริการส่งคำนวณ VRP AI และการติดต่อ OSRM
    - [DispatchManagement](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/DispatchManagement/): บริการประมวลผลแจกงานให้คนขับรถ
    - [FleetTracking](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/FleetTracking/): บริการติดตามพิกัดของกองรถคนขับ
*   **[Services/BackgroundWorkers](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/BackgroundWorkers/)**: งานที่รันประมวลผลอยู่เบื้องหลังระบบ (HostedServices)
    - [DispatchTimeoutWorker.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/BackgroundWorkers/DispatchTimeoutWorker.cs): คอยตัดรอบข้อเสนอออเดอร์เมื่อพนักงานไม่ตอบรับภายใน 15 วินาที
    - [HeartbeatMonitor.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/BackgroundWorkers/HeartbeatMonitor.cs): ตรวจจับคนขับเงียบหายเพื่อเปลี่ยนสถานะเป็น OFFLINE
    - [OsrmSnapWorker.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/BackgroundWorkers/OsrmSnapWorker.cs): ดึงพิกัดจากคิว RabbitMQ ไปทาบเส้นถนน OSRM แบบ Async

---

## 4. โฟลว์การไหลของข้อมูลหลัก (Key Business Flows)

### 4.1 กระบวนการจับคู่และจ่ายงาน (Dispatch & Offering Flow)
โฟลว์หลักนี้เกิดขึ้นภายในคลาส [DispatchService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/DispatchManagement/DispatchService.cs):
1.  เมื่อออเดอร์ถูกสร้างขึ้น ระบบจะตรวจสอบสถานภาพและส่งคำขอไปยัง AI Engine เพื่อให้คิดจัดลำดับ Rider Candidates ที่เหมาะสมที่สุด (อ้างอิง: [AiService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/AiRouting/AiService.cs))
2.  `DispatchService` จะเริ่มสร้างข้อเสนอใหม่ (Offer) และบันทึกลงใน Redis
3.  ส่งสัญญาณแจ้งเตือน SignalR Event `OfferReceived` ไปหา Rider App (Flutter) พร้อมข้อมูล ID และเวอร์ชันของออเดอร์
4.  ไรเดอร์มีเวลาตัดสินใจ **15 วินาที** หากกดยอมรับสำเร็จ สถานะจะเปลี่ยนผ่านตัวควบคุม [StateMachineService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/DispatchManagement/StateMachineService.cs) เป็น `ASSIGNED` และไรเดอร์เปลี่ยนเป็น `BUSY`
5.  หากไรเดอร์กดปฏิเสธ หรือหมดเวลาลง (ตรวจจับโดย `DispatchTimeoutWorker`) ระบบจะล้างข้อเสนอเก่าและส่งสัญญาณหาไรเดอร์ลำดับถัดไปตามรายชื่อ Candidates ทันที

```
Order Created ──► Query AI Candidates ──► Offer to Candidate #1 (SignalR)
                                                 │
            ┌────────────────────────────────────┴───────────────────────────────────┐
     Accept (Within 15s)                                                      Reject / Timeout (15s)
            │                                                                        │
    State -> ASSIGNED                                                        Offer to Candidate #2
    Rider -> BUSY
```

### 4.2 ท่อการประมวลผลข้อมูลพิกัด (SignalR Ingestion & Processing Pipeline)
เพื่อป้องกันปัญหาฐานข้อมูลหลักชะงัก (Database Performance Degradation) ระบบพิกัดขยับจะทำงานผ่านขั้นตอนดังนี้:
1.  Rider App ส่งสัญญาณพิกัดผ่าน WebSockets มายัง [TrackingHub.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Hubs/TrackingHub.cs)
2.  `TrackingHub` ทำหน้าที่ตรวจสอบ Token และยิงส่งข้อมูลต่อไปยัง [TelemetryService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/Telemetry/TelemetryService.cs) ทันที *(ห้ามเขียน Business Logic อื่นใดใน TrackingHub เนื่องจากเป็น Pure Transport)*
3.  `TelemetryService` จะทำการ:
    - เขียนอัปเดตพิกัดสดลงใน **Redis Cache** (เพื่อการดึงข้อมูลที่รวดเร็วของแอดมินและการคำนวณระยะทางของ AI)
    - ส่ง Message `RiderLocationUpdatedIntegrationEvent` เข้าสู่ **RabbitMQ Broker**
4.  [OsrmSnapWorker.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/BackgroundWorkers/OsrmSnapWorker.cs) ซึ่งทำงานอยู่เบื้องหลังจะดึง Message ดังกล่าว ส่งพิกัดไปขอข้อมูลถนน snapped จาก OSRM Container และบันทึกประวัติพิกัดลงในตาราง `RiderLocationHistories` บน PostgreSQL แบบเป็นก้อนพร้อมๆ กัน (Bulk inserts)
5.  Backend Broadcast พิกัด Snapped ออกไปยัง Admin Dashboard เพื่อให้หน้าแผนที่ขยับตามจริงแบบ Reactive

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Contracts)
*   [REST API Endpoints & Request/Response DTO Rules](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/contracts/api-contracts.md)
*   [SignalR WebSockets Hub Payloads Specification](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/contracts/signalr-contracts.md)
*   [State Machine Order & Rider Transition Matrices](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/contracts/state-machine.md)
*   [Redis Keys Structure & TTL Expirations](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/contracts/redis-keys.md)
