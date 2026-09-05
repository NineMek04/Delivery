# Road Test & DevOps Status Conclusion & Readiness Checklist

> **Purpose:** สรุปสถานะความพร้อมของการทดสอบภาคสนาม (Real Road Test) และระบบ DevOps/Monitoring ตามข้อกำหนดใน [Real-Road-Test-Development-Plan-v2.md](../../Real-Road-Test-Development-Plan-v2.md) และ [README-DEVOPS.md](../../README-DEVOPS.md)

---

## 1. Architectural Guardrails (ข้อปฏิบัติที่ถูกต้อง vs สิ่งที่ผิดและห้ามทำ)

| หัวข้อ | ✅ แนวทางที่ถูกต้อง (Correct Status / Allowed) | ❌ แนวทางที่ผิด / ห้ามทำเด็ดขาด (Incorrect / Prohibited) |
|---|---|---|
| **สถาปัตยกรรม Road Test** | ใช้ `road-test/` เป็น **Test Workspace / Config / Scripts / Docs** เท่านั้น ควบคุมแอปหลัก | ห้ามก๊อปปี้โค้ดมาสร้างเป็นแอปแยก หรือ duplicate backend/services ลงใน `road-test/` |
| **GPS Architecture** | ใช้ `LocationService`, `GpsBufferService`, `Geolocator` ของเดิมที่มีอยู่แล้ว | ห้ามเขียน `RoadTestLocationService` หรือสร้างระบบอ่าน GPS ซ้อนทับ |
| **Backend Telemetry** | ส่งพิกัดเข้า `POST /api/v1/telemetry/gps` และ `TrackingHub` เดิม | ห้ามสร้าง `RoadTestTelemetryController` หรือ `RoadTestTrackingHub` |
| **Database & Cache** | บันทึกพิกัดสดลง Redis (`riders:locations`) และประวัติลง PostGIS (`RiderLocationHistories`) | ห้ามสร้างตารางฐานข้อมูลหรืออินสแตนซ์ Redis แยกสำหรับเทสโดยไม่จำเป็น |
| **โหมด GPS บน Android จริง** | ปิด Mock GPS (`ENABLE_MOCK_GPS: false`) เพื่อรับพิกัดดาวเทียมจริงจากชิปมือถือ | ห้ามเปิด Mock GPS ในการทดสอบวิ่งบนถนนจริง |
| **Network Access** | ใช้ Tunnel (Cloudflare Tunnel, ngrok, Tailscale) ชี้เข้าพอร์ต 80 ของ Nginx Reverse Proxy | ห้ามต่อตรงแบบ Local IP (192.168.x.x) เพราะเมื่อออกนอกบ้านจะหลุดการเชื่อมต่อ |

---

## 2. Completed Items Summary (งานที่เสร็จสมบูรณ์แล้ว ✅)

### A. โครงสร้างและการเตรียมความพร้อม (Workspace & Tooling)
- [x] **Phase 1: Repository & Dependency Inspection** — วิเคราะห์โค้ดและระบุคอมโพเนนต์ที่นำกลับมาใช้ซ้ำ (Reuse 100%)
- [x] **Phase 2: Road Test Workspace Structure** — สร้างโฟลเดอร์ `road-test/` (README, docker, config, scripts, docs)
- [x] **Phase 3: Docker Test Compose** — สร้าง `road-test/docker/docker-compose.test.yml` สำหรับรัน Environment สะอาด (ปิด Mock GPS)
- [x] **Phase 4: Test Environment Template** — สร้าง `road-test/config/.env.test.example`
- [x] **Phase 5 & 6: Test Server Active & Validated** — รัน Docker Test Server สำเร็จ Backend, PostGIS, Redis, OSRM, Nginx ทุกตัว Healthy 100%
- [x] **Phase 7: Public Network Tunnel Deployed** — สร้าง Cloudflare Tunnel สู่พอร์ต 80 เรียบร้อยแล้ว
- [x] **Test Utility Scripts (Cross-Platform Bash & PowerShell):**
  - `start-test.sh` / `start-test.ps1` (สั่งรันคอนเทนเนอร์)
  - `stop-test.sh` / `stop-test.ps1` (สั่งปิดคอนเทนเนอร์)
  - `health-check.sh` / `health-check.ps1` (สคริปต์ตรวจความพร้อม Backend, Nginx, OSRM)
  - `reset-test-data.sh` / `reset-test-data.ps1` (สคริปต์ล้างข้อมูลพิกัดเทส)
- [x] **Test Runbook Documentation:**
  - `01-server-setup.md` (การตั้งค่าเซิร์ฟเวอร์และ Tunnel)
  - `02-android-setup.md` (การ Build APK และตั้งค่า Permission)
  - `03-gps-test.md` (ขั้นตอน Stationary & Walking Test)
  - `04-offline-test.md` (ขั้นตอน Offline SQLite Buffer Test)
  - `05-road-test.md` (ขั้นตอน Driving & Background Test)

### B. ระบบตรวจสอบสถานะและ DevOps (Monitoring & Alerts)
- [x] การตั้งค่า Prometheus Metrics Scraper สำหรับ Redis, Postgres, cAdvisor, Node Exporter, Backend
- [x] กฎการแจ้งเตือนและ Alerting Rules (Infrastructure & Security Alerts)
- [x] แผง The Critical 9 NOC Dashboard สำหรับติดตามจุดวิกฤต (Saturation, Eviction, Queue Backlog, AI Latency, OSRM Fallback)

---

## 3. Team Action Guide: Step 3 & Step 4 (คู่มือปฏิบัติการสำหรับทีม)

### 📲 STEP 3: Build & Install Rider APK บนโทรศัพท์ Android จริง (Phase 8)

#### 1. คำสั่ง Build Release APK พร้อมกำหนด Tunnel URL
เปิด Terminal / Command Line แล้วเข้าไปที่โฟลเดอร์ `rider_app/`:
```bash
cd rider_app
flutter build apk --release --dart-define=API_BASE_URL=https://soldier-competitive-transport-oregon.trycloudflare.com
```
*(หมายเหตุ: หาก Tunnel URL มีการรีสตาร์ท ให้เปลี่ยนค่า `API_BASE_URL` เป็น URL ล่าสุด)*

#### 2. ตำแหน่งไฟล์ผลลัพธ์ (APK Artifact)
ไฟล์ APK ที่พร้อมติดตั้งจะอยู่ที่:
```text
rider_app/build/app/outputs/flutter-apk/app-release.apk
```

#### 3. การติดตั้งลงบนโทรศัพท์ Android
* **วิธีที่ 1 (ผ่านสาย USB / ADB):**
  ```bash
  adb install -r rider_app/build/app/outputs/flutter-apk/app-release.apk
  ```
* **วิธีที่ 2 (ไร้สาย):** ส่งไฟล์ `app-release.apk` เข้าโทรศัพท์ผ่าน Google Drive, LINE, หรือ Chat แล้วกดติดตั้งบนเครื่อง

#### 4. การตั้งค่าสิทธิ์ที่จำเป็นบนมือถือ (Mandatory Permissions)
* **Location Permission (สิทธิ์ตำแหน่ง):** ให้เลือก **"Allow all the time" (อนุญาตตลอดเวลา)** เพื่อให้ส่งพิกัดได้แม้พับหน้าจอ
* **Battery Optimization (การจัดการพลังงาน):** ตั้งเป็น **"Unrestricted" (ไม่จำกัด)** เพื่อป้องกันไม่ให้ Android OS ปิดแอปขณะทำงานในพื้นหลัง

---

### 🗺️ STEP 4: แผนการทดสอบภาคสนามตามลำดับ (Field Testing Runbook)

| ลำดับการทดสอบ | ขั้นตอนการปฏิบัติ | เกณฑ์การผ่าน (Expected Result) | เอกสารอ้างอิง |
|---|---|---|---|
| **Test A: Stationary Test** (ทดสอบอยู่นิ่งกลางแจ้ง) | 1. วางมือถือในที่โล่งแจ้งเปิด 4G/5G<br>2. ล็อกอินเข้าแอปแล้วกด **"พร้อมรับงาน"** | พิกัดส่งเข้า Backend, บันทึกลง Redis สด และลงประวัติ `RiderLocationHistories` | [03-gps-test.md](03-gps-test.md) |
| **Test B: Walking Test** (ทดสอบการเดิน) | 1. ถือโทรศัพท์เดิน 100 – 500 เมตร<br>2. เปิดหน้า Admin Web Map (`http://localhost:4201`) ดูหมุดสด | Marker เคลื่อนที่ตามตำแหน่งจริงผ่าน SignalR แบบ Real-time เส้นทางต่อเนื่อง | [03-gps-test.md](03-gps-test.md) |
| **Test C: Offline Buffer Test** (ทดสอบเน็ตหลุด) | 1. ปิดเน็ตมือถือ (4G/5G OFF)<br>2. เคลื่อนที่ต่อ 200–300 เมตร<br>3. เปิดเน็ตมือถือกลับมา (4G/5G ON) | ขณะเน็ตหลุด พิกัดเก็บลง SQLite ในเครื่อง พอเน็ตกลับมา แอปยิง Batch อัปโหลดเข้า PostGIS ครบ 100% | [04-offline-test.md](04-offline-test.md) |
| **Test D: Driving & Screen-Lock** (ทดสอบขับรถ + ล็อกหน้าจอ) | 1. ขับขี่ยานพาหนะความเร็ว 10–60 km/h<br>2. **กดล็อกหน้าจอโทรศัพท์ (Lock Screen)** วิ่งต่อ 1–2 กม.<br>3. ปลดล็อกและเช็คประวัติ | Foreground Service ทำงานต่อเนื่อง, GPS ไม่หลุด, เส้นทางบนแผนที่ครบถ้วนสมบูรณ์ | [05-road-test.md](05-road-test.md) |

---

## 4. Definition of Done Checklist (เกณฑ์การผ่านงานขั้นสุดท้าย)

- [x] **Docker Server:** ทุก Service (Backend, DB, Redis, OSRM, Nginx) ทำงานปกติไม่มี Error ใน Log
- [x] **Network Tunnel:** เปิด Cloudflare Tunnel สู่สาธารณะสำเร็จ
- [ ] **Rider APK:** Build และติดตั้งบนเครื่อง Android จริงเรียบร้อย
- [ ] **Real GPS Ingestion:** มือถือบน 4G/5G ส่งพิกัดจริงเข้า Redis และ PostGIS ได้
- [ ] **Offline Resilience:** ช่วงเน็ตหลุด พิกัดเก็บลง SQLite และส่งแบบ Batch ไม่สูญหาย
- [ ] **Background Execution:** ปิดหน้าจอแล้ว Foreground Service ส่งพิกัดต่อเนื่อง
