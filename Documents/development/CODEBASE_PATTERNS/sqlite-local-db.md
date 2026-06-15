# 💾 รูปแบบการทำข้อมูลสำรองท้องถิ่น (SQLite Local Database & Offline Buffering)

ในโมดูลของแอปพลิเคชันมือถือ Flutter (Rider App) มีความจำเป็นต้องทำงานแบบ Offline-First เนื่องจากคนขับรถต้องเคลื่อนที่ผ่านพื้นที่อับสัญญาณเน็ตเวิร์ก โดยใช้ SQLite ผ่านไลบรารี `sqflite` เพื่อป้องกันข้อมูลสูญหาย:

- **ตำแหน่งตัวควบคุมหลัก:** [local_database_service.dart](file:///c:/Users/ASUS/Desktop/Project/Delivery/rider_app/lib/core/database/local_database_service.dart)

---

## 📊 1. โครงสร้างตารางและสเปกข้อมูล (SQLite Database Schemas)

ฐานข้อมูลมีเวอร์ชัน Schema (Version 4) และถูกจัดตั้งด้วยตารางต่างๆ ดังนี้:

### 🟢 1.1 ตารางจัดการพิกัดออฟไลน์ (`pending_gps_points`)
*   **บทบาท:** เก็บสะสมพิกัด GPS ระหว่างออฟไลน์เพื่อส่งแบบ Batch ขึ้นเซิร์ฟเวอร์เมื่อเน็ตฟื้นตัว
*   **โครงสร้างคอลัมน์:**
    - `id`: INTEGER PRIMARY KEY AUTOINCREMENT
    - `latitude`: REAL NOT NULL (ละติจูด)
    - `longitude`: REAL NOT NULL (ลองจิจูด)
    - `accuracy`: REAL NOT NULL (ความแม่นยำพิกัด)
    - `timestamp`: TEXT NOT NULL (เวลาพิกัดจีพีเอส)
    - `created_at`: INTEGER NOT NULL (เวลาบันทึกลง SQLite)
*   **ดัชนีระบุประวัติเพื่อความรวดเร็ว (Index Acceleration):**
    ```sql
    CREATE INDEX IX_pending_gps_points_created_at ON pending_gps_points (created_at, id)
    ```

### 🟠 1.2 ตารางจัดการคำสั่งอัปเดตสถานะออฟไลน์ (`pending_status_updates`)
*   **บทบาท:** เก็บคำร้องขอส่งเปลี่ยนสถานะของออเดอร์ (เช่น เปลี่ยนจาก PICKED_UP เป็น DELIVERED) ระหว่างที่ไรเดอร์ออฟไลน์อยู่ เพื่อกลับมารีไทร์ส่งอีกครั้งเมื่อจับสัญญาณเครือข่ายได้
*   **โครงสร้างคอลัมน์:**
    - `id`: INTEGER PRIMARY KEY AUTOINCREMENT
    - `order_id`: TEXT (รหัสสั่งซื้อ)
    - `status`: TEXT (สถานะเป้าหมาย)
    - `timestamp`: INTEGER (วันเวลาทำรายการ)

### 🔴 1.3 ตารางจัดเก็บบันทึกข้อผิดพลาดออฟไลน์ (`local_error_logs`)
*   **บทบาท:** บันทึก HTTP Payload และ Error Message ที่ยิงไม่ผ่านเก็บไว้สำหรับดึงขึ้น Seq Dashboard ภายหลังเมื่อออนไลน์
*   **โครงสร้างคอลัมน์:**
    - `id`: INTEGER PRIMARY KEY AUTOINCREMENT
    - `timestamp`: INTEGER
    - `endpoint`: TEXT (ปลายทางที่เรียก)
    - `error_message`: TEXT (ข้อความเออเรอร์)
    - `payload`: TEXT (ข้อมูลที่ส่งล้มเหลว)

---

## ⚡ 2. เทคนิคการจัดการคิวแบบ FIFO และจำกัดแรม (FIFO Trimming Logic)

เพื่อป้องกันปัญหาฐานข้อมูล SQLite ท้องถิ่นบวมเกินขีดจำกัดจนแอปบนอุปกรณ์พกพาค้างหรือกินหน่วยความจำมากเกินไป ระบบได้กำหนดขีดจำกัดไว้ที่ **10,000 จุดพิกัด** โดยใช้เทคนิคดังนี้:

- **การทำธุรกรรมแบบอะตอมมิก (Atomic Transactions):**  
  เมื่อเรียกสั่งบันทึก `savePendingGpsPoint` ระบบจะทำงานในระดับ `db.transaction()` เพื่อเขียนข้อมูลและนับจำนวนแถวในเวลาเดียวกัน:
- **คำสั่งจำกัดจำนวนแบบ FIFO:**
  ```dart
  final count = Sqflite.firstIntValue(
    await txn.rawQuery('SELECT COUNT(*) FROM pending_gps_points'),
  ) ?? 0;
  final overflow = count - 10000;
  if (overflow > 0) {
    await txn.rawDelete('''
      DELETE FROM pending_gps_points
      WHERE id IN (
        SELECT id FROM pending_gps_points
        ORDER BY created_at ASC, id ASC
        LIMIT ?
      )
    ''', [overflow]);
  }
  ```
  *คำอธิบาย:* หากพบว่าแถวพิกัดสะสมเกิน 10,000 จุด ตัวระบบจะไล่ล้างข้อมูลแถวที่มีอายุเก่าสุดออกไปทันทีก่อนบันทึกพิกัดใหม่เสร็จสิ้น (First-In, First-Out)

---

## 🖥️ 3. ระบบรองรับสภาพแวดล้อมข้ามแพลตฟอร์ม (Cross-Platform Guard Check)

เนื่องจากไลบรารี `sqflite` ไม่สามารถคอมไพล์หรือรันบน Web Browser ได้ (จะเกิดการ Crash จากการเรียกใช้ native library ของ iOS/Android):

- **Web Fallback Guard:**  
  ระบบทำเช็คสภาวะ `kIsWeb` จากโครงสร้างของ Flutter เพื่อสลับโหมดการเก็บข้อมูล:
  ```dart
  if (kIsWeb) {
    _webPendingGpsPoints.add({ ... });
    // ทำงานผ่าน In-Memory Map/List แทน SQLite เสมอเพื่อความเสถียรบนเบราว์เซอร์
    return;
  }
  ```
  ทำให้แอปพลิเคชันยังคงสามารถเปิดรันบนโหมด Web Mock GPS เพื่อจุดประสงค์ด้านการทดสอบของทีม QA ได้อย่างปลอดภัย

---

## 🧠 เหตุผลทางวิศวกรรมในการเลือกใช้ SQLite แทน Isar NoSQL (Engineering Reasons)

การเลือกเปลี่ยน Stack ฐานข้อมูลท้องถิ่นของ Rider App จาก Isar NoSQL มาเป็น SQLite (sqflite) มีเหตุผลประกอบการออกแบบสถาปัตยกรรม 3 ประการหลัก:

### 1. กฎ YAGNI (You Aren't Gonna Need It) กับลักษณะข้อมูลของเรา
*   **วิเคราะห์:** ข้อมูลที่เราต้องทำการบัฟเฟอร์เก็บไว้ชั่วคราวระหว่างออฟไลน์คือ "พิกัด GPS" และ "สถานะออเดอร์" ซึ่งมีโครงสร้างเรียบง่าย เป็นแบบ Time-Series/Append-only (บันทึกเรียงต่อท้ายไปเรื่อยๆ) การใช้คำสั่ง SQL ทั่วไปในการทำระบบคิวจำกัดขนาดแบบ FIFO (First-In, First-Out) 10,000 จุด เช่น `DELETE FROM pending_gps_points WHERE id IN (...)` บน SQLite สามารถทำได้ง่าย รวดเร็ว และพิสูจน์แล้วว่ามีเสถียรภาพสูง
*   **ข้อเปรียบเทียบ:** Isar เป็น Object Database ที่มีความสามารถสูงมากก็จริง แต่ความสามารถเหล่านั้นเน้นการค้นหาข้อมูลที่มีโครงสร้างความสัมพันธ์ซับซ้อน (Complex Graph Queries) ซึ่งแอป Rider ในเฟสนี้ไม่มีความจำเป็นต้องทำงานในระดับนั้น การใช้ Isar จึงเป็นการสร้างความซับซ้อนเกินจำเป็น (Over-engineering)

### 2. หลีกเลี่ยงภาระเรื่อง Code Generation (Reducing Build-Time Bottleneck)
*   **วิเคราะห์:** การใช้งาน Isar บังคับให้นักพัฒนาและระบบ CI/CD Pipeline ต้องรันเครื่องมือสร้างโค้ดอัตโนมัติ (`build_runner`) เพื่อสร้างไฟล์ `.g.dart` ทุกครั้งที่มีการปรับปรุงแก้ไขโครงสร้าง Schema ของฐานข้อมูลท้องถิ่น
*   **ข้อดีของการเลี่ยง Isar:** ช่วยประหยัดเวลาการคอมไพล์โปรแกรม (Build Time) ของทีมพัฒนาโมบายและลดระยะเวลาการรันงานบน CI/CD Pipeline ลงอย่างเห็นได้ชัด ทำให้คลอดฟีเจอร์ใหม่ๆ ได้รวดเร็วขึ้น

### 3. ความเสถียรและประสิทธิภาพของ Web Simulator สำหรับทีม QA
*   **วิเคราะห์:** การทำโครงสร้าง Web Fallback ด้วย In-Memory Map/List ผ่านตัวแปรตรวจสอบเงื่อนไข `kIsWeb` ช่วยให้แอปพลิเคชันสามารถทำงานได้อย่างสมบูรณ์บน Web Browser
*   **ข้อเปรียบเทียบ:** แม้ว่า Isar จะรองรับ Web Platform แต่เบื้องหลังการทำงานต้องอาศัย WebAssembly (Wasm) ซึ่งการกำหนดค่าคอนฟิกบน Docker/Web Simulator มักจะมีความจุกจิกและก่อให้เกิดบั๊กจุกจิกได้ง่ายกว่า โครงสร้าง SQLite ร่วมกับ kIsWeb Guard ในปัจจุบันจึงตอบโจทย์ความลื่นไหลในการรันเทสจำลองของทีม QA ได้ดีที่สุด

---

## 💡 คำแนะนำสำหรับการย้ายระบบ (Future Migration to Isar NoSQL)

> [!TIP]
> **หากในอนาคตต้องการย้ายระบบ (Migrate) จาก SQLite (sqflite) ไปเป็น Isar NoSQL จะต้อง:**
> 1. **เพิ่ม Dependency:** เพิ่ม `isar` และ `isar_flutter_libs` ควบคู่กับการเรียกใช้งานตัวสร้าง `isar_generator` และ `build_runner` ใน `pubspec.yaml`
> 2. **ปรับเปลี่ยน Model:** เปลี่ยนโครงสร้างการดึงคลาสโมเดลจากตารางแถว (Relational Rows) ไปเป็นคลาส `@Collection` เพื่อสร้าง Schema แบบ NoSQL
> 3. **แก้ไข Service หลัก:** แก้ไขตัวบริการหลัก [local_database_service.dart](file:///c:/Users/ASUS/Desktop/Project/Delivery/rider_app/lib/core/database/local_database_service.dart) ให้เปลี่ยนไปเปิด `Isar.open()` และเรียกใช้ `isar.writeTxn()` แทนการคิวรีผ่าน raw SQL และ `Sqflite.firstIntValue` ในปัจจุบัน
