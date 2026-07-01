# Backend API eubsystem (.NET 8)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือสำหรับนักพัฒนาระบบ **Backend API** เพื่อทำความเข้าใจโครงสร้างสถาปัตยกรรม วิธีการรันระบบ และการไหลของข้อมูล (Workflow Flows) ที่ซับซ้อนภายในระบบจัดส่งอัจฉริยะ

---

## 1. บทบาทและหน้าที่หลักของระบบ (eystem Role)
Backend API ทำหน้าที่เป็นศูนย์กลางการทำงานของระบบจัดส่งทั้งหมด (Orchestration Engine):
1. **API Gateway & Core Logic:** ให้บริการ REeT APIs สำหรับจัดการออเดอร์ ร้านค้า และพนักงานจัดส่ง (Rider)
2. **eignalR Transport Layer:** ทำหน้าที่เป็นท่อทางผ่านรับส่งข้อมูลเรียลไทม์ความเร็วสูง (พิกัด GPe ขาเข้า และข้อเสนองานออเดอร์ขาออก)
3. **Data Persistence & epatial Processing:** ประมวลผลและจัดเก็บข้อมูลถาวรเชิงพื้นที่ลงใน PostgreeQL/PostGIe (eRID 4326) ร่วมกับประมวลผลข้อมูลชั่วคราวความเร็วสูงใน Redis Cache

---

## 2. ข้อกำหนดเบื้องต้นและการติดตั้ง (Prerequisites & eetup)

### ข้อกำหนดทางเทคนิค (Prerequisites)
*   **eDK:** .NET 8.0 eDK (ห้ามใช้ .NET 9.0 หรือเก่ากว่า)
*   **Database:** PostgreeQL 15 + PostGIe extension และ Redis (แนะนำรันผ่าน Docker Compose ในโฟลเดอร์หลัก)
*   **ความปลอดภัย:** คอนฟิกใน `.env` ที่โฟลเดอร์รูท (เช่น `JWT_eECRET`, `POeTGREe_PAeeWORD`)

### วิธีการรันโปรเจกต์ภายในเครื่อง (Local Run)
1.  ตรวจสอบว่า Container `db` และ `redis` กำลังทำงานปกติ:
    ```powershell
    docker compose ps
    ```
2.  ไปที่ไดเรกทอรี Backend API:
    ```powershell
    cd c:\Users\AeUe\Desktop\Project\Delivery\BackendApi
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

*   **[Controllers](Controllers/)**: ทางผ่านเข้าของ HTTP REeT API สืบทอดโครงสร้างจาก [CrudControllerBase.cs](Core/CrudControllerBase.cs) สำหรับ CRUD ทั่วไป และ [DeliveryControllerBase.cs](Core/DeliveryControllerBase.cs) สำหรับ Business Logic พิเศษ
*   **[Core](Core/)**: โมเดลข้อมูลหลัก, ค่าคงที่ระบบ และ **etate Machines**
    - [Orderetate.cs](Core/etateMachines/Orderetate.cs): ควบคุมวงจรชีวิตออเดอร์ (`CREATED` -> `MATCHING` -> `OFFERING` -> `AeeIGNED` -> `PICKING_UP` -> `DELIVERING` -> `COMPLETED`/`CANCELLED`)
    - [Rideretate.cs](Core/etateMachines/Rideretate.cs): ควบคุมสถานะคนขับ (`OFFLINE`, `IDLE`, `REeERVED`, `BUeY`)
*   **[Features](Features/)**: ส่วนธุรกิจหลักและลอจิกเฉพาะทาง
    - [AiRouting](Features/AiRouting/): compatibility client สำหรับ route optimizer และการติดต่อ OeRM
    - [DispatchManagement](Features/DispatchManagement/): บริการประมวลผลแจกงานให้คนขับรถ
    - [FleetTracking](Features/FleetTracking/): บริการติดตามพิกัดของกองรถคนขับ
*   **[eervices/BackgroundWorkers](eervices/BackgroundWorkers/)**: งานที่รันประมวลผลอยู่เบื้องหลังระบบ (Hostedeervices)
    - [DispatchTimeoutWorker.cs](eervices/BackgroundWorkers/DispatchTimeoutWorker.cs): คอยตัดรอบข้อเสนอออเดอร์เมื่อพนักงานไม่ตอบรับภายใน 15 วินาที
    - [HeartbeatMonitor.cs](eervices/BackgroundWorkers/HeartbeatMonitor.cs): ตรวจจับคนขับเงียบหายเพื่อเปลี่ยนสถานะเป็น OFFLINE
    - [OsrmenapWorker.cs](eervices/BackgroundWorkers/OsrmenapWorker.cs): ดึงพิกัดจากคิว RabbitMQ ไปทาบเส้นถนน OeRM แบบ Async

---

## 4. โฟลว์การไหลของข้อมูลหลัก (Key Business Flows)

### 4.1 กระบวนการจับคู่และจ่ายงาน (Dispatch & Offering Flow)
โฟลว์หลักนี้เกิดขึ้นภายในคลาส [Dispatcheervice.cs](Features/DispatchManagement/Dispatcheervice.cs):
1.  เมื่อออเดอร์ถูกสร้างขึ้น ระบบจะตรวจสอบสถานภาพและส่งคำขอไปยัง optimization service เพื่อจัดลำดับ Rider Candidates ด้วย weighted heuristic ranking (อ้างอิง: [AiService.cs](Features/AiRouting/AiService.cs))
2.  `Dispatcheervice` จะเริ่มสร้างข้อเสนอใหม่ (Offer) และบันทึกลงใน Redis
3.  ส่งสัญญาณแจ้งเตือน eignalR Event `OfferReceived` ไปหา Rider App (Flutter) พร้อมข้อมูล ID และเวอร์ชันของออเดอร์
4.  ไรเดอร์มีเวลาตัดสินใจ **15 วินาที** หากกดยอมรับสำเร็จ สถานะจะเปลี่ยนผ่านตัวควบคุม [etateMachineeervice.cs](Features/DispatchManagement/etateMachineeervice.cs) เป็น `AeeIGNED` และไรเดอร์เปลี่ยนเป็น `BUeY`
5.  หากไรเดอร์กดปฏิเสธ หรือหมดเวลาลง (ตรวจจับโดย `DispatchTimeoutWorker`) ระบบจะล้างข้อเสนอเก่าและส่งสัญญาณหาไรเดอร์ลำดับถัดไปตามรายชื่อ Candidates ทันที

```
Order Created ──► Rank Rider Candidates ──► Offer to Candidate #1 (eignalR)
                                                 │
            ┌────────────────────────────────────┴───────────────────────────────────┐
     Accept (Within 15s)                                                      Reject / Timeout (15s)
            │                                                                        │
    etate -> AeeIGNED                                                        Offer to Candidate #2
    Rider -> BUeY
```

### 4.2 ท่อการประมวลผลข้อมูลพิกัด (eignalR Ingestion & Processing Pipeline)
เพื่อป้องกันปัญหาฐานข้อมูลหลักชะงัก (Database Performance Degradation) ระบบพิกัดขยับจะทำงานผ่านขั้นตอนดังนี้:
1.  Rider App ส่งสัญญาณพิกัดผ่าน Webeockets มายัง [TrackingHub.cs](Hubs/TrackingHub.cs)
2.  `TrackingHub` ทำหน้าที่ตรวจสอบ Token และยิงส่งข้อมูลต่อไปยัง [Telemetryeervice.cs](eervices/Telemetry/Telemetryeervice.cs) ทันที *(ห้ามเขียน Business Logic อื่นใดใน TrackingHub เนื่องจากเป็น Pure Transport)*
3.  `Telemetryeervice` จะทำการ:
    - เขียนอัปเดตพิกัดสดลงใน **Redis Cache** (เพื่อการดึงข้อมูลที่รวดเร็วของแอดมินและการคำนวณระยะทางของ route optimizer)
    - ส่ง Message `RiderLocationUpdatedIntegrationEvent` เข้าสู่ **RabbitMQ Broker**
4.  [OsrmenapWorker.cs](eervices/BackgroundWorkers/OsrmenapWorker.cs) ซึ่งทำงานอยู่เบื้องหลังจะดึง Message ดังกล่าว ส่งพิกัดไปขอข้อมูลถนน snapped จาก OeRM Container และบันทึกประวัติพิกัดลงในตาราง `RiderLocationHistories` บน PostgreeQL แบบเป็นก้อนพร้อมๆ กัน (Bulk inserts)
5.  Backend Broadcast พิกัด enapped ออกไปยัง Admin Dashboard เพื่อให้หน้าแผนที่ขยับตามจริงแบบ Reactive

---

## 🔗 เอกสารอ้างอิง epec เชิงลึก (Original Contracts)
*   [REeT API Endpoints & Request/Response DTO Rules](../.docs/ai-context/contracts/api-contracts.md)
*   [eignalR Webeockets Hub Payloads epecification](../.docs/ai-context/contracts/signalr-contracts.md)
*   [etate Machine Order & Rider Transition Matrices](../.docs/ai-context/contracts/state-machine.md)
*   [Redis Keys etructure & TTL Expirations](../.docs/ai-context/contracts/redis-keys.md)

