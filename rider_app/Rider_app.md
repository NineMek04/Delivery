# แผนการพัฒนาแอปพลิเคชันพนักงานขับรถ (Rider App - Flutter)
สถาปัตยกรรมและขั้นตอนการพัฒนาแบ่งออกเป็น 5 เฟสหลัก เพื่อรองรับการทำงานแบบ Real-time และการจัดการทรัพยากรบนอุปกรณ์เคลื่อนที่

---

## 🛠️ เฟส 1: โครงสร้างโปรเจคและ State Management (Project Setup)
เป้าหมาย: สร้างโครงสร้างพื้นฐานของแอปพลิเคชันให้แข็งแรงและรองรับการขยายสเกลด้วย Clean Architecture

* **Step 1.1: สร้างโปรเจคและจัดการ Dependencies**
  * รันคำสั่ง `flutter create rider_app`
  * เพิ่มไลบรารีใน `pubspec.yaml`: 
    * `flutter_bloc` (จัดการสถานะ)
    * `equatable` (เปรียบเทียบ Object)
    * `geolocator` (ดึงพิกัด)
    * `signalr_netcore` (เชื่อมต่อ WebSockets)
    * `Maps_flutter` (แสดงแผนที่)
* **Step 1.2: จัดระเบียบโฟลเดอร์ (Clean Architecture)**
  * สร้างโครงสร้างโฟลเดอร์ใน `lib/`:
    * `/models` (Data Class)
    * `/blocs` (Business Logic)
    * `/screens` (User Interface)
    * `/services` (External Services เช่น API, GPS)
* **Step 1.3: สร้าง Data Models**
  * สร้างไฟล์ `order_model.dart` และ `location_model.dart` 
  * เตรียมฟังก์ชัน `fromJson` สำหรับรับข้อมูล JSON จาก .NET Backend

---

## 📍 เฟส 2: ระบบติดตามพิกัด (GPS & Background Service)
เป้าหมาย: จัดการการดึงพิกัดอย่างต่อเนื่องและแม่นยำ แม้แอปพลิเคชันจะทำงานอยู่เบื้องหลัง (Background Mode)

* **Step 2.1: ประกาศขอสิทธิ์ระดับ OS (Permissions)**
  * แก้ไข `AndroidManifest.xml` (Android) และ `Info.plist` (iOS)
  * เพิ่มสิทธิ์อนุญาต: `ACCESS_FINE_LOCATION`, `ACCESS_COARSE_LOCATION`
  * เพิ่มสิทธิ์การทำงานเบื้องหลัง: `ACCESS_BACKGROUND_LOCATION`, `FOREGROUND_SERVICE`
* **Step 2.2: ฝัง Background Service**
  * สร้าง Persistent Notification ให้แสดงค้างบนหน้าจอ เพื่อป้องกันระบบปฏิบัติการ (OS) ปิดการทำงานของแอปพลิเคชันเพื่อประหยัดแบตเตอรี่
* **Step 2.3: เขียน Stream ดึงพิกัด (Location Stream)**
  * เรียกใช้ `Geolocator.getPositionStream()` ใน `LocationService`
  * กำหนด Distance Filter (เช่น ขยับขั้นต่ำ 5 เมตรจึงจะอัปเดต) เพื่อประหยัดข้อมูลและพลังงาน
* **Step 2.4: ตัวกรองพิกัด (Noise Filtering)**
  * เขียนเงื่อนไขตรวจสอบค่าความแม่นยำ (`accuracy`) ของ GPS หากความคลาดเคลื่อนสูงเกินกำหนด (เช่น > 50 เมตร) ให้ตัดทิ้งเพื่อป้องกันปัญหาพิกัดกระโดด (GPS Drift)

---

## ⚡ เฟส 3: การสื่อสารแบบ Real-time (SignalR Integration)
เป้าหมาย: สร้างช่องทางการสื่อสารสองทาง (Bi-directional) ระหว่างแอปพลิเคชันและเซิร์ฟเวอร์

* **Step 3.1: สร้างคลาส SignalR Service**
  * ตั้งค่า `HubConnectionBuilder` ชี้ไปยัง Endpoint ของ Backend (เช่น `http://[SERVER_IP]/riderHub`)
* **Step 3.2: ยิงข้อมูลพิกัดขึ้นเซิร์ฟเวอร์ (Client to Server)**
  * นำ Stream จาก GPS มาผูกกับ SignalR
  * สั่งทำงาน `hubConnection.invoke("UpdateLocation", [riderId, lat, lng])` เมื่อมีการเปลี่ยนแปลงพิกัด
* **Step 3.3: ดักฟังข้อมูลออเดอร์ (Server to Client)**
  * ตั้งค่า Listener ดักฟัง Events จากเซิร์ฟเวอร์ เช่น `ReceiveOrder` หรือ `OrderCancelled`
  * นำ JSON ที่ได้รับแปลงเป็น `OrderModel` และส่งเข้า BLoC เพื่ออัปเดตหน้าจอ

---

## 🗺️ เฟส 4: ส่วนแสดงผลและแผนที่ (UI & Map Integration)
เป้าหมาย: ออกแบบหน้าจอแสดงผลแผนที่และการนำทางที่ใช้งานง่ายขณะขับขี่

* **Step 4.1: ฝัง Google Maps**
  * ตั้งค่า API Key ของ Google Cloud Maps Platform
  * นำ Widget `GoogleMap` แสดงผลบนหน้าจอหลัก
  * ปรับค่า Camera Position ให้ติดตามหมุด (Marker) พิกัดปัจจุบันแบบไดนามิก
* **Step 4.2: สร้างปุ่มสวิตช์สถานะ (Toggle Online/Offline)**
  * ปุ่มสำหรับเปิด/ปิดรับงาน หากเลือก Offline ระบบจะตัดการเชื่อมต่อ SignalR และหยุดดึงพิกัด GPS
* **Step 4.3: วาดเส้นทาง (Polylines)**
  * นำชุดพิกัดจุดแวะพัก (Waypoint Sequence) ที่ได้จากระบบ AI มาวาดเส้นทาง (Polyline) บนแผนที่ เชื่อมระหว่างตำแหน่งปัจจุบัน -> ร้านค้า -> ลูกค้า

---

## 🔄 เฟส 5: วงจรการทำงานของออเดอร์ (Order Lifecycle)
เป้าหมาย: จัดการลำดับสถานะการจัดส่งตั้งแต่เริ่มต้นรับงานจนถึงการส่งมอบสำเร็จ

* **Step 5.1: หน้าต่างเตือนงานใหม่ (Incoming Order UI)**
  * แสดง Bottom Sheet แจ้งเตือนพร้อมเสียง/สั่น เมื่อ BLoC ตรวจพบสถานะงานใหม่
  * แสดงข้อมูลสรุป: ชื่อร้าน, ระยะทาง, รายได้ และปุ่มกดยอมรับ/ปฏิเสธงาน
* **Step 5.2: หน้าจอรายละเอียดงาน (Active Order View)**
  * สลับการแสดงผลหน้าจอไปยังโหมดนำทาง (Navigation Mode) พร้อมแบนเนอร์แสดงขั้นตอนปัจจุบัน
* **Step 5.3: ปุ่มอัปเดตสถานะงาน (State Transition)**
  * สร้างกลไกอัปเดตสถานะและส่งข้อมูลกลับไปยัง Backend ตามลำดับ:
    1. ถึงร้านแล้ว (Arrived at Store)
    2. รับสินค้าแล้ว (Picked Up) -> *สลับเป้าหมายนำทางไปยังลูกค้า*
    3. จัดส่งสำเร็จ (Delivered)
* **Step 5.4: รีเซ็ตระบบ (Clear State)**
  * ลบข้อมูลออเดอร์และเส้นทางบนแผนที่เมื่อจบงาน เพื่อกลับเข้าสู่สถานะรอรับงานใหม่