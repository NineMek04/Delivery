# Seq Centralized Logging Manual (Documents/setup/SEQ-SETUP.md)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือการตั้งค่าและการคิวรีสืบค้นล็อกโครงสร้างวัตถุ (**Centralized Structured Logging**) บนตู้บริการ **Seq Server** ร่วมกับตัวขับล็อก Serilog หลังบ้าน

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
`seq` ทำหน้าที่เป็นศูนย์กลางรวบรวมและวิเคราะห์ข้อมูลการรันระบบ (Operations Flight Recorder):
1.  **Structured Ingestion:** รับสัญญาณล็อกวัตถุพฤติกรรม (JSON format) ที่ถูกยิงออกมาจากหลังบ้านผ่านโปรโตคอล Serilog Ingestion (พอร์ต 5341)
2.  **Fast Debugging:** ช่วยให้นักพัฒนาและแอดมินสืบสวนหาสาเหตุอาการบั๊ก ข้อยกเว้น (Exceptions) และลำดับเหตุการณ์การชนพิกัดโดยไม่ต้องล็อกอินเข้าไปคุ้ยในไฟล์ล็อกดิบของโฮสต์
3.  **Audit Trail:** จัดทำดัชนีตรวจเช็คสิทธิ์ใช้งานและการทำงานของ API Gateway

---

## 2. พอร์ตการทำงานและการผูกสัญญาล็อก (Port & Log Ingestion Specs)

ตู้บริการ Seq ทำงานแยกกันเป็น 2 ทางผ่านข้อมูล:
*   **Web Dashboard UI:** ปล่อยพอร์ตออกนอกเกตเวย์ที่ **`8082`** (ภายใน 80) เพื่อใช้เป็นหน้าจอกราฟสีสำหรับสืบค้น
*   **Log Ingestion API:** ปล่อยพอร์ตขารับที่ **`5341`** (ภายใน 5341) เป็นช่องรับยิงตั๋วล็อกแบบ HTTP JSON

---

## 3. การกำหนดตัวขับฝั่งหลังบ้าน (Serilog Setup & Core Log Rules)

ฝั่ง Backend API มีการตั้งค่า Serilog Sink ชี้พิกัดในไฟล์ [appsettings.json](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/appsettings.json) ไปหาที่อยู่ `http://seq:5341`:

### 3.1 กฎการยึดรอย Correlation ID (Trace Correlation Rules)
ตามข้อกำหนดในการตรวจสอบประวัติข้อผิดพลาดข้าม Microservices (**`AGENTS.md §4`**) ล็อกของ Backend API ทุกเส้นบันทึกจะต้องแนบตัวแปรสามฟิลด์ต่อไปนี้เสมอ (ถ้ามี) เพื่อใช้เชื่อมโยงเหตุการณ์:
*   **`CorrelationId`:** รหัสจำเพาะของคำร้องขอทรานแซกชัน UUID (ถูกสร้างขึ้นตั้งแต่เริ่มทราฟฟิก Gateway และส่งผ่านตัวแปร Headers)
*   **`OrderId`:** รหัสออเดอร์จัดส่งสินค้าที่กำลังเกี่ยวข้อง
*   **`RiderId`:** รหัสพนักงานขับรถที่เป็นเจ้าของพิกัดหรือสัญญางาน

---

## 4. คู่มือการสืบค้นข้อมูลเชิงปฏิบัติงาน (Seq Search Queries Reference)

ตารางสรุปคำสั่งคิวรี (Queries) ที่ใช้บ่อยบน Web UI สำหรับสืบหาเหตุการณ์ในห้องทดสอบ:

| จุดประสงค์การค้นหา | คำสั่งคิวรีสืบค้น (Seq Query Syntax) | คำอธิบายรายละเอียด |
| :--- | :--- | :--- |
| **หาข้อผิดพลาดทั้งหมด** | `IsDefined(@Exception) or @Level = 'Error'` | ดึงเฉพาะบรรทุกล็อกที่พบบั๊ก Exception แตกหรือแจ้งข้อความเตือนระดับ Error |
| **ค้นตามทรานแซกชัน** | `CorrelationId = 'c02e12a4-db01-443b-a5d6...'` | สืบลำดับเส้นทางเดินของคำร้องขอนั้นๆ ตั้งแต่จุดเริ่มจนถึง DB |
| **ติดตามพฤติกรรม AI** | `Component = 'AiOptimizer'` | ดึงเฉพาะล็อกการรันคำนวณ Candidates หรือ VRP Route sequence |
| **ดูทราฟฟิกพิกัด GPS** | `SourceContext = 'DeliveryBackendApi.Hubs.TrackingHub'` | ตรวจความเคลื่อนไหวการล็อกอิน Socket ของคนขับรถ |
| **เช็คออเดอร์ตัวชี้วัด** | `OrderId = 'ORD-2026-99120'` | เจาะหาประวัติคนขับ Accept/Reject และความคืบหน้าออเดอร์นั้นๆ |

---

## 5. วิธีการขึ้นระบบและเปิดใช้งาน (Setup & Verification Steps)

1.  ตรวจสอบการตั้งค่าตู้ Seq ในไฟล์ [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml):
    ```yaml
      seq:
        image: datalust/seq:latest
        container_name: delivery-seq
        ports:
          - "8082:80"
          - "5341:5341"
        environment:
          - ACCEPT_EULA=Y
        networks:
          - delivery-network
    ```
2.  เริ่มรันคอนเทนเนอร์:
    ```bash
    docker-compose up -d seq
    ```
3.  ตรวจสอบการรับล็อก:
    *   เปิดบราวเซอร์เข้าสู่หน้าจอ `http://localhost:8082`
    *   ยิงคำขอทดสอบบน Swagger Backend API หรือรันสคริปต์ `simulate-e2e.js` เพื่อสังเกตการณ์ว่ามีบรรทุกล็อกไหลเข้ามายังแดชบอร์ดตามเวลาจริง

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Context)
*   [DevOps Infrastructure Manual (README-DEVOPS.md)](file:///c:/Users/ASUS/Desktop/Project/Delivery/README-DEVOPS.md)
*   [Trace Correlation & Logging Standard Rules](file:///c:/Users/ASUS/Desktop/Project/Delivery/CRITICAL-CODE-PROTECTION.md#L4)
