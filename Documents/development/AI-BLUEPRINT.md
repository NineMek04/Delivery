# AI-BLUEPRINT: Smart Delivery Routing System

> **⚠️ AI AGENT NOTICE:** 
> This file is now a **Historical Archive (Layer 1)**. For targeted context, start by reading `AI-INDEX.md` and navigate to `.docs/ai-context/` instead to save tokens and prevent context overload.

> **ชื่อโครงการ:** ระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์  
> **English:** AI-Optimized Smart Delivery Routing System  
> **ผู้พัฒนา:** นายนนท์ธรัตน์ ทาลา  
> **Version:** 0.9.0 (Phase 5: Real-world Routing & Real-time Dispatch Simulation)  
> **Last Updated:** 2026-05-20

---

## 1. Project Vision & Problem Statement

### ปัญหาที่ต้องแก้ไข
แพลตฟอร์มจัดส่งสินค้า/อาหาร ประสบปัญหาการคำนวณเส้นทางที่มีจุดแวะรับ-ส่งหลายจุด (Multi-drop / Batched Orders) ที่ไม่มีประสิทธิภาพ → สิ้นเปลือง **เวลา + เชื้อเพลิง** + ลดความพึงพอใจลูกค้า

### แนวทางแก้ปัญหา
- **Zero-Budget Prototype** — ใช้สมาร์ทโฟนเป็น GPS sensor (BYOD) แทนฮาร์ดแวร์ราคาแพง
- **AI (VRP Algorithm)** — คำนวณเส้นทางที่เหมาะสมที่สุดด้วย Google OR-Tools
- **Microservices on Docker** — deploy ง่าย ไม่ต้องตั้งค่า environment ซับซ้อน

### เป้าหมาย
1. ศึกษาและพัฒนาระบบ Microservices สำหรับขนส่งอัจฉริยะ
2. ประยุกต์ใช้ AI แก้ปัญหา Vehicle Routing Problem (VRP)
3. ระบบสื่อสาร Real-time ระหว่าง Mobile ↔ Web (SignalR)
4. ลดต้นทุน Prototype โดยใช้สมาร์ทโฟนแทนเซนเซอร์เฉพาะทาง

---

## 2. Technology Stack (ข้อเท็จจริงจาก Codebase)

| Layer              | Technology                  | Version           | Notes                                         |
|--------------------|-----------------------------|-------------------|-----------------------------------------------|
| **Backend API**    | .NET (ASP.NET Core)         | **.NET 8**        | Enhanced with Auth system & TrackingHub        |
| **Real-time**      | SignalR                     | ASP.NET Core built-in | **Ready** — TrackingHub implemented           |
| **AI Engine**      | Python + FastAPI            | 3.11-slim         | **Ready** — VRP Solver implemented with OR-Tools |
| **AI Algorithm**   | Google OR-Tools             | Latest            | PATH_CHEAPEST_ARC strategy                    |
| **Database**       | PostgreSQL + PostGIS        | 15-3.3 (Docker)   | SRID 4326 / WGS84                             |
| **Cache (Hot Data)** | Redis                      | 7-alpine (Docker) | **New** — Used for GPS, Heartbeat, and Locking |
| **ORM**            | EF Core + NetTopologySuite  | 8.0.11            | DBHandlerCore implemented for easy CRUD       |
| **Security**       | JWT Bearer + Refresh Token     | 8.0.11         | **Enhanced** — Refresh Token & Rotation added |
| **Frontend**       | Angular                     | **v19.2.0**       | Fluent API & OpenAPI Generator ready          |
| **Mobile App**     | Flutter (Dart)              | **^3.9.0**        | **Foundation Ready** (Auth & Refresh implemented) |
| **Container**      | Docker + Docker Compose     | v3.8              | ✅ All services have Dockerfiles              |
| **Web Server**     | Nginx                       | —                 | สำหรับ serve Angular ใน container               |

---

## 3. System Architecture

### 3.1 Microservices Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            Docker Network                                   │
│                                                                             │
│  ┌──────────────┐      ┌──────────────┐      ┌──────────────┐               │
│  │  delivery-db  │      │  delivery-   │      │  delivery-ai  │               │
│  │   PostGIS     │◄────►│   backend    │◄────►│Python FastAPI │               │
│  │  15-3.3       │      │   .NET 8     │      │ + OR-Tools    │               │
│  └──────────────┘      └──────┬───────┘      └──────────────┘               │
│                               │                                             │
│                        ┌──────▼──────┐                                      │
│                        │ delivery-   │                                      │
│                        │   redis     │ (Hot Data: GPS, Locks, Presence)     │
│                        │   7-alpine  │                                      │
│                        └─────────────┘                                      │
│                                                                             │
│  ┌──────────────────────────────────────────────────────┐                   │
│  │        delivery-frontend — Angular via Nginx          │                   │
│  │                    Port: 80                           │                   │
│  └──────────────────────────────────────────────────────┘                   │
└─────────────────────────────────────────────────────────────────────────────┘
                             │
                    ┌────────┴────────┐
                    │  Flutter App    │
                    │  (Rider Mobile) │
                    │  ✅ Foundation Ready │
                    └─────────────────┘
```

### 3.2 Data Flow

```
Flutter App ──(WebSocket/SignalR)──► .NET Backend ──(REST)──► Python AI Service
     │                                    │                         │
     │                                    ▼                         │
     │                              PostgreSQL/PostGIS              │
     │                                    │                         │
     │◄──(SignalR Broadcast)──────────────┤◄────(VRP Result)────────┘
     │                                    │
     │                              Angular Dashboard
     │                              (Real-time Map)
```

---

## 4. Repository Structure (สถานะจริง)

```
Delivery/
├── .cursorrules                 ← กฎสำหรับ AI assistant (Cursor)
├── AGENTS.md                    ← กฎเหล็กสูงสุดในการพัฒนา ห้ามลบ ห้ามละเมิดเด็ดขาด
├── AI-INDEX.md                  ← สารบัญและ Context Router แผนผังนำทางให้ AI
├── Delivery.sln                 ← Solution File ของระบบ .NET Core
├── docker-compose.yml           ← แฟ้มตั้งค่าคอนเทนเนอร์หลัก 12 เซอร์วิส
│
├── Documents/                   ← แหล่งรวบรวมเอกสารการออกแบบและสถาปัตยกรรมของโครงการ
│   ├── README.md                ← สารบัญและสาระสำคัญของระบบเอกสารทั้งหมด
│   ├── infrastructure/          ← ข้อมูลด้านระบบ DevOps & Deployments
│   ├── setup/                   ← ข้อมูลขั้นตอนการติดตั้งและการเซ็ตอัพ
│   └── development/             
│       ├── AI-BLUEPRINT.md      ← ไฟล์นี้ — สรุปโครงสร้างสถาปัตยกรรมระดับภาพรวม
│       ├── PROJECT-SPEC.md      ← เอกสารข้อกำหนดเชิงลึกระบบและการวิจัย (Master Spec)
│       └── CODEBASE_PATTERNS/   ← โฟลเดอร์รวมแบบแผนการเขียนโค้ดและการแก้ปัญหาเชิงลึก
│           ├── README.md        ← ดัชนีสารบัญแบบแผนโค้ดทั้งหมด 19 ฉบับ
│           ├── sqlite-local-db.md ← รายละเอียด SQLite offline buffering
│           ├── gorouter-rbac.md ← รายละเอียด GoRouter RBAC pattern
│           └── api-response-wrapper.md ← รายละเอียด REST JSON wrapping pattern
│
├── BackendApi/                  ← .NET 8 Web API (Core Services & Tracking Gateway)
│   ├── Core/                    ← Models, Filters, DataHandlers, Base Controllers
│   ├── Controllers/             ← REST Controller endpoints
│   ├── Hubs/                    ← SignalR WebSockets (TrackingHub)
│   ├── Models/DTOs/             ← Data Transfer Objects & Entity Models
│   ├── Security/                ← JWT Token Service & Password management
│   ├── Setup/                   ← Dependency Injection & Configuration
│   └── Dockerfile               ← Multi-stage production build configuration
│
├── ai-engine/                   ← Python FastAPI (Routing & VRP Solver)
│   ├── main.py                  ← จุดเชื่อมต่อ FastAPI + Google OR-Tools
│   ├── requirements.txt         
│   └── Dockerfile               ← Build and deployment config
│
├── admin-dashboard/             ← Angular 19 (Web Admin Dashboard)
│   ├── src/app/core/            ← Core Services, HTTP Clients, Interceptors
│   ├── openapitools.json        ← OpenAPI Generator config
│   └── Dockerfile               ← Angular production deployment server
│
├── rider_app/                   ← Flutter (Rider Mobile Application)
│   ├── lib/                     ← Code base (Dio, SignalR, Riverpod, Local DB)
│   ├── pubspec.yaml             
│   └── Rider_app.md             ← แผนการพัฒนาแอปพลิเคชันมือถือไรเดอร์
│
└── scripts.test/                ← โฟลเดอร์เดี่ยว (Single Test Hub) รวบรวมเทสทั้งหมด
```

---

## 5. Current State of Development

### สถานะรวม: 🔵 **Phase 5 — Data-Driven Observability (Testing Dashboard)** (สำเร็จ 100%)
### สถานะรวม: 🟢 **Phase 6 — Production Readiness & Operational Intelligence** (สำเร็จ 100% สำหรับ Core Backend/AI/Testing/Dashboard)

| Component            | Status            | รายละเอียด                                                    |
|----------------------|-------------------|--------------------------------------------------------------|
| **docker-compose**   | ✅ Created         | 6 services: db, backend, ai-service, frontend, redis, **test-dashboard-redis** (ระบบอัตโนมัติคุมการรัน) |
| **PostGIS DB**       | ✅ Optimized       | GiST Indexes บนพิกัด Geometry, ตาราง Location History แบบ Range Partitioning รายเดือน, Clustering จูน Disk IO |
| **Redis Cache**      | ✅ Running         | Used for GPS speed layer, presence, and distributed locking  |
| **BackendApi**       | ✅ 100% Ready      | ขจัดปัญหา N+1 Query, ย้าย Haversine Math ไปหา PostGIS Engine, เพิ่มระบบ Provision Partition อัตโนมัติ |
| **Universal Tracking**| ✅ 100% Ready      | ติดตั้งรหัสอ้างอิงสวยงาม ORD-, RID-, SHP-, USR- คิวรี O(1)/O(log N) ผนวกการค้นหาแบบผสมผสาน |
| **OSRM Routing**     | ✅ 100% Ready      | ติดตั้งระบบ Offline-First Dijkstra สำหรับวาดเส้นทางโค้งจริง พร้อมการบีบอัด Polyline |
| **ai-engine**        | ✅ 100% Ready      | FastAPI + OR-Tools VRP solver, Phase A Scorer, และ ETA Prediction Engine |
| **admin-dashboard**  | ✅ 100% Ready      | Live maps, Smooth Linear Interpolation, On-Demand Route history, และ SignalR live tracking |
| **test-dashboard**   | ✅ 100% Ready      | Testing Dashboard (Node.js + BullMQ + Angular + Socket.IO + Xterm + Chart.js Gauge/Line charts) พร้อมดึง Metrics (RPS, Latency, Errors) |
| **rider_app**        | 🟢 50% Ready       | SQLite Local Buffer (via sqflite), Timer Sync Engine, and Riverpod integration complete. |
| **E2E Simulator**    | ✅ 100% Ready      | Node.js script เชื่อมต่อเต็มรูปแบบ รองรับเส้นทาง OSRM, GPS Jitter, SignalR Flow พร้อมกันหลาย Rider |
| **SignalR Hub**      | ✅ Ready           | `TrackingHub` refactored to use Redis presence & GPS buffer  |
| **Database Migration**| ✅ Applied         | Run `dotnet ef database update` สำหรับ Spatial Index, Partitioning, และ RefNumber ล่าสุด |
| **Backend Security** | ✅ Enhanced        | JWT, Refresh Token, Rotation, Role policy, Serilog logging added |
| **Enterprise Audit** | ✅ Ready           | Layered Base Entities, Soft Delete, IP Tracking, Concurrency Tokens (RowVersion) |
| **E2E Testcontainers**| ✅ 100% Passed     | รัน integration tests แบบ End-to-End บน Testcontainers PostGIS Docker จริง ผ่านฉลุย 5/5 เคส |
| **Integration Tests**| ✅ 100% Passed     | รันผ่าน 43/43 เคส (รวม Telemetry Batch Ingestion) ด้วย Testcontainers PostGIS + Redis แบบ Hermetic Sandbox |
| **Stress/Load Tests**| ✅ 100% Passed     | สคริปต์ Node.js สำหรับเทสโหลด SignalR, API, Dispatch และ Reconnect stability |
| **Analytics & ETA**  | ✅ 100% Ready      | AI ประเมินเวลาส่งอัตโนมัติ และ Dashboard summary endpoints สำหรับ Admin |
| **AI-OS Context**    | ✅ Implemented     | ใช้งาน `AI-INDEX.md` ในการจัดการ context อย่างแม่นยำและประหยัด Token |


---

## 6. Docker Compose Config (ปัจจุบัน)

| Service        | Image / Build                    | Port  | Container Name       | Status              |
|----------------|----------------------------------|-------|----------------------|---------------------|
| **db**         | `postgis/postgis:15-3.3`          | 5432  | delivery-db          | ✅ ใช้งานได้         |
| **backend**    | `./BackendApi/Dockerfile`        | 5000  | delivery-backend     | ✅ Ready            |
| **redis**      | `redis:7-alpine`                 | 6379  | delivery-redis       | ✅ Ready            |
| **ai-service** | `./ai-engine/Dockerfile`          | 8000  | delivery-ai          | ✅ Ready            |
| **frontend**   | `./admin-dashboard/Dockerfile`   | 80    | delivery-frontend    | ✅ Ready            |
| **rider-app**  | `./rider_app/Dockerfile`         | 8080  | delivery-rider-app   | ✅ Ready (Web Prototype) |
| **osrm**       | `osrm/osrm-backend`              | 5001  | delivery-osrm        | ✅ Ready (Offline OSRM)  |
| **nginx-proxy**| `nginx:alpine`                   | 8081  | delivery-nginx       | ✅ Ready            |
| **seq**        | `datalust/seq:latest`            | 5341  | delivery-seq         | ✅ Ready (Central Logs)  |
| **prometheus** | `prom/prometheus:latest`         | 9090  | delivery-prometheus  | ✅ Ready            |
| **grafana**    | `grafana/grafana-enterprise`     | 3000  | delivery-grafana     | ✅ Ready            |
| **rabbitmq**   | `rabbitmq:3-management-alpine`   | 5672  | delivery-rabbitmq    | ✅ Ready (AMQP Broker)   |


---

## 7. Next Tasks (Priority Order)

### 🔴 Critical — ขาดและ block การทำงาน
1. **พัฒนา Angular Dashboard** — Map view (Leaflet/Google Maps) + real-time tracking UI + รัน OpenAPI Generator(🟢 ผ่านแล้ว)
### 🔴 Critical — สเต็ปต่อไปสำหรับ Mobile App
2. **พัฒนา Flutter Rider App ต่อ** — Implement UI จริง, รัน `build_runner`, และทำระบบส่ง GPS พื้นหลัง (Background Service)

### 🟡 Important — ต้องทำเร็วๆ นี้
3. **Backend ↔ AI Integration** — (✅ สำเร็จแล้ว) มี `AiService` พร้อมเรียกใช้จาก Controller/Service อื่น
4. **ขยาย Business Logic** — (✅ สำเร็จแล้ว) มี `OrdersController` สำหรับจัดการออเดอร์และทริกเกอร์ Dispatch
6. **Dispatch Integration Test** — (✅ สำเร็จแล้ว) ทดสอบ End-to-End Flow: Admin -> Backend -> AI -> SignalR -> Rider ได้รับ Offer งานสำเร็จ
7. **พัฒนา Mobile App ฝั่ง Customer / Store** — อิงจาก Prototype หน้าเว็บที่สร้างไว้ (ใช้ Flutter หรือ Web-based PWA)
9. **Backend Features (Tier 2/3)** — FCM Push Notifications, ระบบชำระเงิน, ETA Calculation
10. **พัฒนา Mobile App ฝั่ง Customer / Store** — อิงจาก Prototype หน้าเว็บที่สร้างไว้ (ใช้ Flutter หรือ Web-based PWA)
11. **Backend Features (Tier 2/3)** — FCM Push Notifications และระบบชำระเงิน
### 🟢 Nice-to-have
12. **CI/CD Workflows** — GitHub Actions
13. **Integration Tests** — ทดสอบการเชื่อมต่อระหว่าง services
14. **CI/CD Workflows** — GitHub Actions & Automated deployments
15. **RabbitMQ Event Bus** — เปลี่ยนจาก Synchronous เป็น Event-Driven สำหรับบาง Module
---

## 8. AI-Specific Notes (สำหรับ AI Assistant)

### ⚠️ สิ่งที่ต้องระวัง
- **Security:** JWT config ต้องตั้งผ่าน environment variable/user secrets (`Jwt__Key` >= 32 chars)
- **JWT Auth:** รองรับทั้ง `Authorization: Bearer` และ HttpOnly Cookie `access_token`
- **Refresh Token:** ระบบ Token Rotation (Access + Refresh) เพื่อความปลอดภัยสูงสุด
- **Role Policies:** `AdminOnly`, `Operations`, `Rider`
- **Rate Limiting:** มี `auth` policy สำหรับป้องกัน Brute-force
- **Security Headers:** ตั้งค่า `Referrer-Policy`, `X-Frame-Options`, ฯลฯ ผ่าน Middleware เพื่อให้ DTO ตรงกับ Backend
- **Spatial:** ใช้ `NetTopologySuite` ใน .NET และ `geometry(Point, 4326)` ใน DB
- **API Contract:** ใช้ OpenAPI Generator ใน Angular เพื่อให้ DTO ตรงกับ Backend

### 📋 Files ที่ต้องอ่านก่อนเริ่มงาน
1. `AI-BLUEPRINT.md` — ไฟล์นี้ (Context Ledger)
2. `AI-CHANGELOG.md` — สถานะงานล่าสุด
3. `PROJECT-SPEC.md` — รายละเอียด Spec เชิงลึก
4. `.cursorrules` — กฎและ workflow ของ AI

---

## 9. Flutter Mobile App — Feature List & Planning

> **Last Updated:** 2026-06-15 | **Status:** Aligning with the Active Stack

รายละเอียดสเปก ข้อกำหนด และฟังก์ชันการใช้งานของแอปพลิเคชันมือถือ (Rider, Customer, StorePartner) ได้ถูกย้ายและแยกโครงสร้างออกเป็นเอกสารจำเพาะเพื่อประหยัด Token และป้องกันการซ้ำซ้อนในแบบแผนที่บวม โดยสามารถเปิดอ่านข้อมูลสถานะล่าสุดและสถาปัตยกรรมทางเทคนิคได้ที่:

- **สเปกข้อกำหนดและการเชื่อมต่อ:** [.docs/ai-context/spec-mobile-rider.md](../../.docs/ai-context/spec-mobile-rider.md) (Rider Auth, GPS Rules, Background Sync, OSRM/SignalR contracts)
- **สเปกการสำรองข้อมูลในเครื่อง (SQLite):** [sqlite-local-db.md](CODEBASE_PATTERNS/sqlite-local-db.md) (ตาราง `pending_gps_points`, `pending_status_updates` และระบบ FIFO trimming)
- **การจัดสิทธิ์และเส้นทางแผนผัง:** [gorouter-rbac.md](CODEBASE_PATTERNS/gorouter-rbac.md) (GoRouter RBAC & Refresh Listenable)

### 9.1 สถานะภาพรวมโครงการโมบาย (ณ ปัจจุบัน)
- **Rider App (Flutter):** 🟢 50% Ready (โครงสร้าง Auth, Secure Storage, SignalR, Background GPS, SQLite/sqflite offline buffer และ GoRouter RBAC พร้อมใช้งาน)
- **Customer / Store Partner App:** 🟡 วางแผนพัฒนาในเฟสถัดไปหลังจาก REST API/WS ในส่วนที่เกี่ยวข้อง of Backend API เสร็จสิ้น

---
## Latest Working Context - 2026-05-20 Sim Realtime Dispatch Map

### Current Routing
- Admin route `/map` now opens the new simulation-focused map: `admin-dashboard/src/app/features/sim-map/`.
- Original live map is still available at `/map-live`: `admin-dashboard/src/app/features/map/`.
- Sidebar label now points operators to `Sim Map` during simulator testing.

### Sim Map Behavior
- Shows scan circle around the order shop when dispatch starts.
- Shows nearby rider candidates and ranked candidates from SignalR.
- Smoothly animates rider marker movement with `requestAnimationFrame`.
- Auto-follows and zooms to the selected rider while pickup/dropoff route simulation is running.
- Draws pickup and dropoff markers, full route, and remaining route progress.
- Includes a lightweight HUD for flow phases: Scan -> Offer -> Assign -> Pickup -> Dropoff.

### Backend Realtime Bridge
- `BackendApi/Hubs/TrackingHub.cs` now keeps `Rider.CurrentLocation` in PostGIS synced with realtime rider GPS updates.
- `BackendApi/Services/Dispatch/DispatchService.cs` broadcasts dispatch simulation events to admin clients:
  - `DispatchScanStarted`
  - `DispatchCandidatesRanked`
  - `DispatchOfferSent`
- Dispatch offers include pickup route details when available from OSRM.
- `OrdersController` broadcasts `OrderStatusChanged` to admin and rider groups after status transitions.

### Simulator
- Main file: `scripts/e2e-simulator/simulate-e2e.js`.
- It creates 5-10 simulated riders near a random shop/order area, sends realtime GPS through SignalR, accepts the selected offer, then moves through pickup and delivery routes.
- Route source order is backend encoded route -> local OSRM fallback -> straight-line fallback.
- Useful env vars: `DELIVERY_API_URL`, `DELIVERY_HUB_URL`, `DELIVERY_HEALTH_URL`, `DELIVERY_OSRM_URL`, `DELIVERY_ADMIN_EMAIL`, `DELIVERY_ADMIN_PASSWORD`, `DELIVERY_SIM_PASSWORD`, `DELIVERY_SIM_RIDERS`.

### Verification Notes
- `node --check scripts\e2e-simulator\simulate-e2e.js` passed.
- `npm.cmd run build` in `admin-dashboard` passed with existing budget/CommonJS warnings.
- `dotnet build BackendApi\BackendApi.csproj` passed with existing warnings.
- Full simulator execution may require Docker API access outside the sandbox on this machine.

### Next Suggested Work
- Run the full Docker-backed simulator against `/map` and tune animation timing/zoom if needed.
- When Flutter rider app is ready, replace the simulator GPS/acceptance flow with real rider app events while keeping the same SignalR/backend event contract.

### สิ่งที่ยังเหลือต้องทำ (Next Action Items) สำหรับโปรเจกต์ Delivery
 - เมื่อระบบแกนกลาง (Core Backend, AI, Database) เสถียรและทดสอบ E2E ผ่านหมดแล้ว 
### งานที่เหลือจะมุ่งเน้นไปที่ส่วนที่เชื่อมต่อกับ ผู้ใช้งานจริง (End Users) ครับ:
  1. 📱 ฝั่งแอปพลิเคชันมือถือ (Priority สูงสุด)
    - Rider App (Flutter): ขึ้นโครง UI จริงทั้งหมด (Login, Home Dashboard แบบมี Toggle รับงาน, หน้า Active Delivery, และ Map Tracking) และเขียนระบบ Service ในการส่ง GPS เบื้องหลัง (Background Service)
    - Customer App / Store Partner App: เนื่องจากเราทำ Prototype ของ 2 ฝั่งนี้บน Angular เรียบร้อยแล้ว (สามารถกดสั่ง, เปิดปิดร้าน, สร้างออเดอร์) ขั้นต่อไปคือการยกฟีเจอร์นี้ไปทำเป็น Mobile App หรือ Web PWA ครับ
  2. ⚙️ ฝั่ง Backend API (Tier 2/3)
    - ระบบการแจ้งเตือน (Push Notifications): ฝัง Firebase Cloud Messaging (FCM) เพื่อให้เวลา AI หางานเจอ หรือเวลาสถานะออเดอร์เปลี่ยน แจ้งเตือนจะเด้งเข้ามือถือแม้แอปปิดอยู่
    - ETA & Pricing: ปรับแต่งสูตรคำนวณเวลาถึงโดยประมาณ (ETA) ให้ละเอียดขึ้นโดยใช้ความเร็วรถและระยะทางจาก OSRM รวมถึงทำระบบ Payment (ถ้าจำเป็น)
  3. 💻 ฝั่ง Admin Dashboard
    - เก็บตกหน้าจอที่เหลือ เช่น หน้าจัดการ Rider เชิงลึก (Activate/Deactivate, ดูประวัติรายบุคคล) และระบบจัดการร้านค้าครับ