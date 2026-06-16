# Rider Mobile App Subsystem (Flutter)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือการพัฒนาสำหรับทีม **Mobile Developer (Flutter)** อธิบายโครงสร้างสถาปัตยกรรม Clean Architecture ระบบจัดการพิกัดเบื้องหลัง (Background Location) และกลไกทำงานออฟไลน์ (Offline-First SQLite Buffer)

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
Rider App เป็นแอปพลิเคชันบนมือถือสำหรับพนักงานขับรถขนส่งสินค้า (Rider) ทำงานเรียลไทม์ร่วมกับ Backend API และ AI Engine:
1.  **Status Sync:** รายงานความพร้อมของตนเองให้กับเซิร์ฟเวอร์
2.  **GPS Telemetry Engine:** ส่งตำแหน่งพิกัด GPS ละติจูด/ลองจิจูดอย่างต่อเนื่อง (ทั้งแบบ Online SignalR Stream และ Offline SQLite Batch Update)
3.  **Order Actions:** รับแจ้งเตือนคำเสนอสั่งอาหาร กดยอมรับ/ปฏิเสธข้อเสนอ และยืนยันสถานะจัดส่ง (Arrived/Picked Up/Delivered)

---

## 2. ข้อกำหนดเบื้องต้นและการติดตั้ง (Prerequisites & Setup)

### ข้อกำหนดทางเทคนิค (Prerequisites)
*   **Flutter SDK:** แนะนำเวอร์ชัน 3.22.x หรือสูงกว่า ร่วมกับ **Dart SDK 3.4.x** (หรือเวอร์ชันที่สัมพันธ์กัน)
*   **เครื่องมือรัน:** Android Studio / VS Code (แนะนำติดตั้งปลั๊กอิน Flutter & Dart)
*   **เป้าหมายทดสอบ:** โทรศัพท์จริง Android/iOS หรือ Emulator/Simulator (สำหรับทดสอบพิกัด GPS)

### วิธีการรันโปรเจกต์ภายในเครื่อง (Local Run)
1.  ย้ายหน้าต่าง Terminal มายังโฟลเดอร์ของแอปมือถือ:
    ```bash
    cd c:\Users\ASUS\Desktop\Project\Delivery\rider_app
    ```
2.  ติดตั้ง Libraries ทั้งหมดตามระบุใน `pubspec.yaml`:
    ```bash
    flutter pub get
    ```
3.  รันตัวสร้างโค้ดอัตโนมัติ (Model parsing และ Riverpod Code Generation):
    ```bash
    dart run build_runner build --delete-conflicting-outputs
    ```
4.  รันคอมไพล์โปรแกรมขึ้นหน้าจอทดสอบ:
    *   **โหมดจำลองพิกัดบนเว็บ (Mock GPS Mode):**
        ```bash
        flutter run -d chrome --dart-define=ENABLE_MOCK_GPS=true
        ```
    *   **โหมดคุยกับเซิร์ฟเวอร์จริง (Physical Device):**
        ```bash
        flutter run -d <device_id> --dart-define=API_URL=http://<YOUR_LAN_IP>:5000/api/v1
        ```

---

## 3. โครงสร้างโฟลเดอร์โครงการ (Folder Structure)
แอป Flutter ได้รับการพัฒนาภายใต้โครงสร้างสไตล์ **Feature-First Clean Architecture** เพื่อการขยายขนาดที่เหมาะสม:

```
lib/
├── core/                  # โค้ดกลางที่แชร์ข้ามคุณลักษณะ (Horizontal Core)
│   ├── api/               # ไคลเอนต์ติดต่อ HTTP API (Dio Client, Interceptors)
│   ├── auth/              # ลอจิกจัดการสิทธิ์การเข้าถึง Token / Role
│   ├── config/            # ค่าคงที่และตัวแปรแวดล้อม (Environment)
│   ├── database/          # ตัวควบคุมฐานข้อมูล SQLite ท้องถิ่น (sqflite)
│   ├── location/          # บริการจัดการ GPS Stream, Filters และ Local buffering
│   ├── session/           # จัดการวงจรชีวิตการทำงานออนไลน์/ออฟไลน์ของคนขับ
│   └── signalr/           # บริการควบคุมท่อส่งข้อมูลเรียลไทม์ WebSockets
├── features/              # จัดเก็บโมดูลธุรกิจแนวตั้ง (Vertical Features)
│   ├── auth/              # หน้าจอ Login / Logout
│   ├── delivery/          # หน้าจอแผนที่นำทางส่งอาหาร polyline
│   ├── orders/            # หน้าจอรายการออเดอร์
│   └── tracking/          # หน้าการติดตามสถานะงานแบบเวลาจริง
├── models/                # คลาสโครงสร้างข้อมูล DTOs (Freezed & Generated)
└── shared/                # วิดเจ็ตหน้าจอ (UI Widgets), ธีม (Theme) และตัวแปร CSS
```

---

## 4. ลอจิกสำคัญของระบบ (Core Subsystem Logic)

### 4.1 วงจรสถานะของคนขับ (Rider State Machine)
สถานะของคนขับมี 4 สถานะหลัก ซึ่งซิงค์ตรงกับคีย์เวิร์ดของ Backend เสมอ:
1.  **`OFFLINE`:** คนขับไม่ได้ทำงาน ปิด SignalR Connection และยกเลิก GPS Tracking เพื่อประหยัดพลังงาน
2.  **`IDLE`:** คนออนไลน์พร้อมรับงาน ดึง GPS Stream ส่งข้อมูลขึ้น Backend ทุกๆ 5 วินาที
3.  **`RESERVED`:** ได้รับคำเสนอส่งอาหาร (Offer) หน้าจอเตือนจะกระพริบสั่นเตือนและนับถอยหลัง 15 วินาที ช่วงนี้ระบบจะล็อคไม่ให้รับข้อเสนอออเดอร์อื่น
4.  **`BUSY`:** ไรเดอร์ตอบตกลงรับออเดอร์สำเร็จ กำลังวิ่งเดินทางไปส่งอาหาร (PICKUP -> DELIVERING)

### 4.2 การดึง GPS Telemetry & ระบบตรวจสอบความปลอดภัย (Anti-Spoofing & Filtering)
การจัดการพิกัดมีความสำคัญสูงสุดในการประเมินประสิทธิภาพ จึงประมวลผลผ่าน [location_service.dart](lib/core/location/location_service.dart):
*   **GPS Noise Filtering (กฎ 300 เมตร):** ค่าพิกัดใดๆ ที่ได้รับเข้ามาจากจีพีเอสมือถือ แต่มีรัศมีความคลาดเคลื่อน (`accuracy`) สูงกว่า **300 เมตร** จะถูกคัดกรองทิ้ง (Noise Filtered) ทันที เพื่อป้องกันพิกัดกระโดดบนแผนที่ (GPS Jitter/Drift)
*   **ระบบตรวจสอบพิกัดปลอม (Anti-Spoofing/Mock GPS Block):**  
    ก่อนยอมรับพิกัดใดๆ แอปจะดึงพารามิเตอร์ `position.isMocked` มาตรวจสอบเสมอ หากตรวจพบค่าเป็นจริง (ผู้ใช้เปิดโปรแกรมโกงพิกัด/Fake GPS) แอปจะทำตามขั้นตอน:
    1.  ยกเลิกการเก็บตำแหน่งและปิดระบบ GPS Tracking ทันที
    2.  ส่งการแจ้งเตือนสยบผู้ใช้งาน (Alert Notification)
    3.  เรียกใช้คำสั่งบังคับ Rider เป็น `OFFLINE` ทันที ([rider_session_service.dart](lib/core/session/rider_session_service.dart)) เพื่อตัดการรบกวนความสอดคล้องข้อมูลของ Backend

### 4.3 ระบบจัดการข้อมูลพิกัดออฟไลน์ (Offline SQLite Buffer & Ingestion)
หากคนขับวิ่งรถผ่านจุดอับสัญญาณเน็ตเวิร์ก แอปจะเปลี่ยนเข้าสู่โหมดกักเก็บข้อมูลท้องถิ่น:
1.  [location_service.dart](lib/core/location/location_service.dart) จะนำพิกัดที่กรองแล้วบันทึกลง SQLite ตาราง `pending_gps_points` ผ่าน [local_database_service.dart](lib/core/database/local_database_service.dart#L410) (จำกัดจำนวนสูงสุดที่ 10,000 จุด หากเกินจะทยอยลบจุดเก่าสุดออก)
2.  เมื่อกลับเข้าสู่เครือข่ายสัญญาณปกติ [gps_buffer_service.dart](lib/core/location/gps_buffer_service.dart#L144) จะดึงพิกัดขึ้นมาทยอยส่ง (Batch Ingestion) ไปที่ endpoint `POST /api/v1/telemetry/gps/batch` ครั้งละ 100 จุดแบบเรียงตามเวลา (FIFO)
3.  ใช้ระบบ **Adaptive Jitter Delay** (ถ่วงเวลาส่ง 500ms - 2000ms เพื่อป้องกันการยิงถล่มเซิร์ฟเวอร์หลังฟื้นคืนสัญญาณ)
4.  ประยุกต์ใช้ **Backpressure Response Header** (`X-Recommended-Ping` จากเซิร์ฟเวอร์) มาปรับลดหรือเพิ่มรอบความถี่ในการส่งพิกัดให้อัตโนมัติ

### 4.4 การแสดงผลเส้นทาง OSRM และการจำลองพิกัดจริงบนแผนที่
*   เมื่อคนขับกดยอมรับงาน ไคลเอนต์จะไม่ติดต่อ OSRM Container ตรงๆ (เพื่อความปลอดภัยทางเน็ตเวิร์ก) แต่จะยิงคำขอรับรายละเอียดเส้นทางผ่าน Backend API แทน
*   ข้อมูลโพลีไลน์ที่ Backend คืนกลับมา (Snapped OSRM route) จะนำมาวาดเส้นทับเส้นถนนเดินทางจริง (Polyline) บนแผนที่นำทาง
*   **การจำลองพิกัดเคลื่อนที่ (OSRM Coordinates Simulation):** การเคลื่อนที่จำลองของไรเดอร์ทำงานบนเส้นทางโครงข่ายพิกัดจริงของ OSRM (แทนที่การคำนวณระยะกระจัดแบบเส้นตรงลอยข้ามอาคาร) โดยคำนวณและแสดงระยะทางถนนที่เหลือจากการรวมความยาวของแต่ละเซกเมนต์พิกัดที่เหลืออยู่จริงๆ
*   **การวาดเส้นทางแบบเรียลไทม์ (Dynamic Route Cropping):** ในหน้าจอติดตามเส้นทาง ระบบจะตัดพิกัดที่คนขับวิ่งผ่านไปแล้วออกด้วยฟังก์ชัน `_getTailRoute` ทำให้เส้น Polyline บนแผนที่หดสั้นลงเรื่อยๆ ตามความจริง ช่วยอำนวยความสะดวกในการติดตามของคนขับ

### 4.5 ลอจิกสำคัญฝั่งลูกค้า (Customer Flow Enhancements)
*   **การซื้อทันที (Direct Checkout / Buy Now):** ในหน้าจอรายละเอียดร้านค้าและตัวเลือกเมนู เพิ่มปุ่ม "ซื้อทันที" เพื่ออำนวยความสะดวกให้กับลูกค้า โดยระบบจะล้างรายการสินค้าเดิมทั้งหมดในตะกร้า แล้วเพิ่มเฉพาะสินค้ารายการที่เลือกนี้พร้อมออปชัน จากนั้นจะแสดงบานหน้าต่างสำหรับสั่งซื้อและชำระเงิน (`CartBottomSheet`) ทันทีเพื่อทำรายการจ่ายเงินอย่างรวดเร็ว
*   **การล้างประวัติคำสั่งซื้อ (Clear Order History):** ในหน้าจอแสดงรายการคำสั่งซื้อของลูกค้า สามารถใช้ปุ่มล้างประวัติเพื่อลบข้อมูลคำสั่งซื้อทั้งหมดที่ทำเสร็จไปแล้วออกจากการแสดงผล โดยระบบหลังบ้านจะจัดการทำ Soft Delete (อัปเดตสถานะ `DelFlag = 'Y'`) เพื่อรักษาความสอดคล้องของข้อมูลทางบัญชีและการวิเคราะห์

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Contracts)
*   [Flutter Subsystem Core Specification](../.docs/ai-context/spec-mobile-rider.md)
*   [State Machine Configuration Matrix](../.docs/ai-context/contracts/state-machine.md)
*   [SignalR WebSockets Connection Payloads](../.docs/ai-context/contracts/signalr-contracts.md)
