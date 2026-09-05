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
- [x] **Test Utility Scripts:**
  - `start-test.sh` (สั่งรันคอนเทนเนอร์)
  - `stop-test.sh` (สั่งปิดคอนเทนเนอร์)
  - `health-check.sh` (สคริปต์ตรวจความพร้อม Backend, Nginx, OSRM)
  - `reset-test-data.sh` (สคริปต์ล้างข้อมูลพิกัดเทส)
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

## 3. Pending Items (งานที่ยังไม่ได้ทำ / ต้องดำเนินการในขั้นตอนถัดไป ⏳)

| ลำดับ | รายการงานที่ต้องทำ (Task Title) | Phase อ้างอิง | สถานะ | สิ่งที่ต้องเตรียม / วิธีการทดสอบ |
|:---:|---|:---:|:---:|---|
| **1** | **Start & Validate Test Server** | Phase 5 & 6 | ⏳ รอทดสอบ | ก๊อปปี้ `.env` แล้วสั่งรัน `docker compose` พร้อมรัน `health-check.sh` |
| **2** | **Deploy Public Network Tunnel** | Phase 7 | ⏳ รอเปิด Tunnel | รัน `cloudflared tunnel --url http://localhost:80` เพื่อให้ได้ Public HTTPS Endpoint |
| **3** | **Build & Install Rider APK** | Phase 8 | ⏳ รอคอมไพล์ | กำหนด Base URL เป็น Tunnel URL แล้วรัน `flutter build apk --release` ติดตั้งลงเครื่องจริง |
| **4** | **Stationary Test (Test A)** | Phase 9 | ⏳ รอออกภาคสนาม | เปิดแอปในที่โล่งแจ้ง เช็คพิกัดเข้า Redis และ PostGIS |
| **5** | **Walking Test (Test B)** | Phase 10 | ⏳ รอออกภาคสนาม | เดิน 100–500m ตรวจสอบ Real-time Breadcrumb Track บน Admin Web Map |
| **6** | **Offline Buffer & Sync (Test C)** | Phase 13 & 14 | ⏳ รอออกภาคสนาม | ปิด 4G/5G เดินต่อ แล้วเปิดเน็ต ตรวจสอบการส่ง Batch เข้า Backend อัตโนมัติ |
| **7** | **Driving & Background Test (Test D)** | Phase 11, 12, 15 | ⏳ รอออกภาคสนาม | ขี่รถ 10–60 km/h พร้อมทดสอบล็อกหน้าจอโทรศัพท์ (Background Foreground Service) |

---

## 4. Definition of Done Checklist (เกณฑ์การผ่านงานขั้นสุดท้าย)

- [ ] **Docker:** ทุก Service (Backend, DB, Redis, OSRM, Nginx) ทำงานปกติไม่มี Error ใน Log
- [ ] **Network:** มือถือบน 4G/5G เรียกเข้า Public Tunnel URL และเชื่อมต่อ SignalR ได้สำเร็จ
- [ ] **Real GPS:** มือถืออ่านพิกัดจริงจากดาวเทียม ความแม่นยำ (Accuracy) < 15 เมตร
- [ ] **Storage & Ingestion:** พิกัดถูกบันทึกลง Redis สด และลงประวัติ PostGIS ครบถ้วน
- [ ] **Offline Resilience:** ช่วงเน็ตหลุด พิกัดเก็บลง SQLite และอัปโหลดกู้คืนครบ 100% เมื่อเน็ตกลับมา
- [ ] **Background Execution:** ปิดหน้าจอแล้ว Foreground Service ไม่ถูก OS ฆ่า และยังคงส่งพิกัดต่อเนื่อง
