# QA Test Map & Execution Manual (RootScripts/scripts.test/README.md)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือสำหรับ **QA Engineer, Software Tester** และ **Developers** เพื่อใช้เป็นศูนย์รวมการรันชุดทดสอบทั้งหมดในระบบ (Testing Hub) ตรวจสอบความถูกต้องของโปรแกรมในห้องปฏิบัติการ และประเมินพฤติกรรมเมื่อระบบรับทราฟฟิกสูงระดับวิกฤต

---

## 1. แผนภาพลำดับการทดสอบ (Testing Map)

การทดสอบในโปรเจกต์นี้ได้รับการแบ่งออกเป็น 4 ระดับ เพื่อควบคุมความถูกต้องตั้งแต่หน่วยเล็กที่สุดไปจนถึงประสิทธิภาพระดับโครงสร้างพื้นฐาน:

```
[Unit Tests] ──────────► [Integration Tests] ──────────► [E2E Simulation] ──────────► [Load/Stress Tests]
(C# / Python)            (C# PostGIS DbContainers)       (Node.js OSRM Flow)           (SignalR Concurrency / k6)
```

1.  **Unit Tests (ระดับหน่วย):** ทดสอบลอจิกคณิตศาสตร์และการประมวลผลเชิงฟังก์ชัน เช่น สูตร ETA, ระยะทาง Haversine, ลอจิกคำนวณทิศทางการวิ่งของรถคนขับ
2.  **Integration Tests (ระดับบูรณาการ):** ทดสอบการติดต่อฐานข้อมูลเชิงพื้นที่จริง (PostGIS/NetTopologySuite) การสร้างออเดอร์ และการเปลี่ยนผ่านสถานะ Order Lifecycle ผ่าน Dockerized Testcontainers
3.  **End-to-End (E2E) Simulation:** จำลองหน้าคนขับและผู้ใช้ ทำธุรกรรมตั้งแต่สร้างออเดอร์, ส่งงานหา Rider, คนขับกดยอมรับงาน, วิ่งรถนำทางตามโครงข่ายถนน OSRM จริงของจังหวัดอุดรธานี
4.  **Load & Stress Tests (การทดสอบขีดจำกัด):** ระดมยิงตำแหน่ง GPS จากคนขับนับร้อยราย และผู้ใช้งานส่งคำขอผ่าน HTTP API พร้อมกัน เพื่อค้นหาจุดคอขวดระบบล็อก คิว และหน่วยความจำพัง (OOMKilled)

---

## 2. กฎเหล็กควบคุมโฟลเดอร์การทดสอบ (Strict Testing Directory Rules)

เพื่อความง่ายในการส่งมอบและควบคุมความสะอาดของซอร์สโค้ด ห้ามละเมิดกฎดังต่อไปนี้เด็ดขาด:

1.  **Single Test Hub Rule (กฎศูนย์ทดสอบหนึ่งเดียว):**  
    ชุดการทดสอบทั้งหมด (ยกเว้น Angular) จะต้องจัดเก็บอยู่ภายใต้ไดเรกทอรี [RootScripts/scripts.test/test/](test/) เท่านั้น ห้ามกระจายโฟลเดอร์ทดสอบปะปนกับ Source Code ฝั่งโปรดักชัน
2.  **No Test Files in Core Directories (ห้ามไฟล์ทดสอบปนเปื้อน):**  
    ห้ามสร้างโฟลเดอร์ชื่อ `tests/` หรือ `__tests__/` ปนอยู่ใน Subsystem แกนหลัก เช่น ห้ามสร้างใน `ai-engine/tests` แต่ให้ย้ายมาอยู่ที่ `RootScripts/scripts.test/test/ai-engine.tests/` แทน
3.  **Angular Spec Files Exception (ข้อยกเว้น Angular):**  
    สำหรับหน้า Angular Dashboard อนุญาตให้วางไฟล์ทดสอบระดับยูนิต (`*.spec.ts`) ควบคู่กับตัว Component นั้นๆ ได้ตามมาตรฐานของ Angular CLI เพื่อไม่ให้กระทบต่อ Pipeline การคอมไพล์หลัก
4.  **Load & Stress Test Log Rule (กฎบันทึกผลการ Stress Test):**  
    เมื่อรันระบบทดสอบความเครียด (Stress Test / Breaking-Point) ห้ามนำไฟล์ Log หรือไฟล์ CSV ผลลัพธ์ไปบันทึกทิ้งไว้ที่โฟลเดอร์รูทของ `LogsTest` ตรงๆ แต่ **บังคับให้จัดเก็บแยกโฟลเดอร์ตามวันที่ทดสอบจริง** ในฟอร์แมต `LogsTest/YYYY-MM-DD/` และต้องตั้งชื่อไฟล์ตามมาตรฐานนี้เท่านั้น:
    *   `stage5_stats.csv` (จัดเก็บข้อมูลดิบ CPU/Memory ของ Docker containers ขณะโดนถล่ม)
    *   `stage5_run.log` (ข้อมูล stdout และรายงานของตัวทดสอบ k6 หรือสคริปต์ Stress Test)
    *   `stage5_final_report.md` (เอกสารวิเคราะห์คอขวด ข้อเสนอแนะการแก้ปัญหาความเร็วระบบ)

---

## 3. วิธีการสั่งรันการทดสอบ (How to Run Tests)

### 3.1 การรัน Backend Unit & Integration Tests (C# .NET)
*   **Unit Tests:** ทดสอบโมเดลธุรกิจและ State machine ลอจิก
*   **Integration Tests:** รันผ่านระบบจำลองฐานข้อมูลจริงของ Docker Testcontainers (ต้องเปิดโปรแกรม Docker Desktop ก่อนรันเสมอ)

```powershell
# 1. ย้ายตำแหน่งเข้าโฟลเดอร์ทดสอบ
cd c:\Users\ASUS\Desktop\Project\Delivery\RootScripts\scripts.test

# 2. รันเทสยูนิตหลังบ้าน
dotnet test test/BackendApi.UnitTests/BackendApi.UnitTests.csproj

# 3. รันเทสอินทิเกรชันหลังบ้าน (พร้อมดู Log ละเอียด)
dotnet test test/BackendApi.IntegrationTests/BackendApi.IntegrationTests.csproj --logger "console;verbosity=detailed"
```

### 3.2 การรัน AI Engine Tests (Python FastAPI)
*   ทดสอบลอจิก VRP Solver และความเสถียรของ OR-Tools
*   *Prerequisites:* ต้องลงและเปิดใช้งาน Virtual Environment ในโฟลเดอร์ AI และมี pytest ติดตั้งอยู่

```powershell
# รันเทสฝั่ง AI Routing
pytest test/ai-engine.tests
```

### 3.3 การรัน Mobile Unit/Widget Tests (Flutter)
```bash
# รันเทสบน Flutter App
flutter test test/rider_app.tests/widget_test.dart
```

---

## 4. สคริปต์จำลองการทดสอบความล้าของระบบ (E2E & Load Testing)

### 4.1 สคริปต์ E2E Simulator (จำลองการวิ่งรับส่งสินค้าจริง)
จำลองพฤติกรรมคนขับ 5-10 คนเชื่อมต่อและยิง GPS อ้อมแผนที่ตามระยะเวลา OSRM:
```powershell
# ย้ายโฟลเดอร์ไปที่ e2e-simulator
cd c:\Users\ASUS\Desktop\Project\Delivery\RootScripts\scripts.test\test\e2e-simulator
npm install
node simulate-e2e.js
```

### 4.2 สคริปต์ Load & Stress Tests (ยิงพิกัด GPS ถล่ม)
*   **SignalR GPS Pushing Load:** ยิง GPS ticks พร้อมกันจาก 100 ไรเดอร์จำลองเพื่อเช็คอาการของ Redis Lock และ latency การเขียนลง DB:
    ```powershell
    cd c:\Users\ASUS\Desktop\Project\Delivery\RootScripts\scripts.test\test\load-test
    npm install
    npm run test:signalr
    ```
*   **REST API HTTP Load:** ส่งคำร้องเรียก API ดึงออเดอร์ปริมาณมากพร้อมกันเพื่อเช็คความเสถียรของ Rate limit:
    ```powershell
    npm run test:api
    ```
*   **Dispatch Queue Stress:** สร้างออเดอร์รัวๆ พร้อมกันเพื่อดูคิวประมวลผล VRP AI:
    ```powershell
    npm run test:dispatch
    ```
*   **Rider Reconnect Loop:** จำลองสถานการณ์เน็ตหลุดและต่อเข้าระบบใหม่พร้อมๆ กันเพื่อประเมินความเร็วในการกู้คืน State บน Redis:
    ```powershell
    npm run test:reconnect
    ```

---

## 5. การวิเคราะห์จุดคอขวด (Stress Test & OOMKilled Detection)

ระหว่างทำ Stress Test ทีม QA จะต้องเปิดบอร์ด Grafana สังเกตและวิเคราะห์:
1.  **OOMKilled Detection (แรมคอนเทนเนอร์ระเบิด):**  
    ตรวจสอบตัวชี้วัด `container_oom_events_total` ใน Prometheus หากมีค่าเพิ่มขึ้นกว่า 0 แสดงว่า OS ได้สั่งเชือดคอนเทนเนอร์ตัวที่ใช้แรมเกินขีดจำกัดความปลอดภัยของ Docker แล้ว (คอขวดมักอยู่ที่ AI Engine หรือ backend API)
2.  **DB Connections Overflow (คิวเชื่อมฐานข้อมูลตัน):**  
    เช็คจาก `DatabaseConnectionsHigh` เมื่อ Connections พุ่งชน 85% ของ PgBouncer จะทำให้คำขอบนแอปพลิเคชันค้าง
3.  **Telemetry Data Drop Rate:**  
    สังเกตจำนวนพิกัด GPS ที่ส่งล้มเหลว (Failed counts) บนกราฟ ซึ่งสัมพันธ์กับการปฏิเสธทราฟฟิกของ Rate limit ในช่วงโดนยิงถล่ม

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Context)
*   [Infrastructure, Telemetry & SLO Specification](../../.docs/ai-context/spec-infra-devops.md)
*   [State Machine & Telemetry Data Consistency Spec](../../.docs/ai-context/spec-consistency.md)
