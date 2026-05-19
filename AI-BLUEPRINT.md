# AI-BLUEPRINT: Smart Delivery Routing System

> **ชื่อโครงการ:** ระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์  
> **English:** AI-Optimized Smart Delivery Routing System  
> **ผู้พัฒนา:** นายนนท์ธรัตน์ ทาลา  
> **Version:** 0.8.0 (Phase 4: Universal Tracking & Sequential Reference Numbers)  
> **Last Updated:** 2026-05-19

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
├── .cursorrules             ← กฎสำหรับ AI assistant (Cursor)
├── AI-BLUEPRINT.md          ← ไฟล์นี้ — AI Context Ledger
├── AI-CHANGELOG.md          ← Log การเปลี่ยนแปลงโดย AI
├── PROJECT-SPEC.md          ← รายละเอียด Spec เชิงลึก
├── Delivery.sln             
├── docker-compose.yml       ← ✅ 4 services: db, backend, ai-service, frontend
│
├── BackendApi/              ← .NET 8 Web API
│   ├── Core/                ← Models, Filters, DataHandlers, Base Controllers
│   ├── Controllers/         ← AuthController, RidersController (Ready)
│   ├── Hubs/                ← TrackingHub (SignalR)
│   ├── Models/DTOs/         ← OrderDto, RiderDto, AuthDtos
│   ├── Security/            ← JwtTokenService, PasswordHasher, LoginAttemptService
│   ├── Setup/               ← DI, Security, Pipeline Setup
│   └── Dockerfile           ← ✅ Ready
│
├── ai-engine/               ← Python FastAPI
│   ├── main.py              ← ✅ FastAPI + OR-Tools VRP Solver
│   ├── requirements.txt     
│   └── Dockerfile           ← ✅ Ready
│
├── admin-dashboard/         ← Angular 19
│   ├── src/app/core/        ← Fluent HTTP, Interceptors, AuthService
│   ├── openapitools.json    ← OpenAPI Gen Config
│   └── Dockerfile           ← ✅ Ready
│
└── rider_app/               ← Flutter (Mobile)
    ├── lib/                 ← Foundation Ready (Dio, SignalR, Riverpod, Location)
    ├── pubspec.yaml         
    └── Rider_app.md         ← Implementation Plan
```

---

## 5. Current State of Development

### สถานะรวม: 🔵 **Phase 4 — Universal Tracking & Sequential Reference Numbers** (สำเร็จ 100%)

| Component            | Status            | รายละเอียด                                                    |
|----------------------|-------------------|--------------------------------------------------------------|
| **docker-compose**   | ✅ Created         | 5 services: db, backend, ai-service, frontend, **redis** (ปรับจูน RAM & DB Connections สำหรับ PostGIS) |
| **PostGIS DB**       | ✅ Optimized       | GiST Indexes บนพิกัด Geometry, ตาราง Location History แบบ Range Partitioning รายเดือน, Clustering จูน Disk IO |
| **Redis Cache**      | ✅ Running         | Used for GPS speed layer, presence, and distributed locking  |
| **BackendApi**       | ✅ 100% Ready      | ขจัดปัญหา N+1 Query, ย้าย Haversine Math ไปหา PostGIS Engine, เพิ่มระบบ Provision Partition อัตโนมัติ |
| **Universal Tracking**| ✅ 100% Ready      | ติดตั้งรหัสอ้างอิงสวยงาม ORD-, RID-, SHP-, USR- คิวรี O(1)/O(log N) ผนวกการค้นหาแบบผสมผสาน |
| **ai-engine**        | 🟢 95% Ready       | FastAPI + OR-Tools VRP solver & Phase A Scorer. Waiting for Backend to call. |
| **admin-dashboard**  | 🟢 65% Ready       | อัปเดต Map Component พิกัดอุดรธานี เพิ่ม Cockpit/Portal ฝั่งผู้ซื้อและร้านค้าคู่ค้าแบบ Real-time |
| **rider_app**        | 🟡 30% Ready       | Foundation ready. Needs real UI, build_runner, and Background GPS logic. |
| **SignalR Hub**      | ✅ Ready           | `TrackingHub` refactored to use Redis presence & GPS buffer  |
| **Database Migration**| ✅ Applied         | Run `dotnet ef database update` สำหรับ Spatial Index, Partitioning, และ RefNumber ล่าสุด |
| **Backend Security** | ✅ Enhanced        | JWT, Refresh Token, Rotation, Role policy, Serilog logging added |
| **Enterprise Audit** | ✅ Ready           | Layered Base Entities, Soft Delete, IP Tracking, Concurrency Tokens (RowVersion) |
| **E2E Testcontainers**| ✅ 100% Passed     | รัน integration tests แบบ End-to-End บน Testcontainers PostGIS Docker จริง ผ่านฉลุย 5/5 เคส |

---

## 6. Docker Compose Config (ปัจจุบัน)

| Service        | Image / Build          | Port  | Container Name       | Status              |
|----------------|------------------------|-------|----------------------|---------------------|
| **db**         | `postgis/postgis:15-3.3`| 5432  | delivery-db          | ✅ ใช้งานได้          |
| **backend**    | `./BackendApi/Dockerfile`| 5000 | delivery-backend     | ✅ Ready            |
| **ai-service** | `./ai-engine/Dockerfile` | 8000 | delivery-ai          | ✅ Ready            |
| **frontend**   | `./admin-dashboard/Dockerfile`| 80| delivery-frontend    | ✅ Ready            |

---

## 7. Next Tasks (Priority Order)

### 🔴 Critical — ขาดและ block การทำงาน
1. **พัฒนา Angular Dashboard** — Map view (Leaflet/Google Maps) + real-time tracking UI + รัน OpenAPI Generator
2. **พัฒนา Flutter Rider App ต่อ** — Implement UI จริง, รัน `build_runner`, และทำระบบส่ง GPS พื้นหลัง (Background Service)

### 🟡 Important — ต้องทำเร็วๆ นี้
3. **Backend ↔ AI Integration** — (✅ สำเร็จแล้ว) มี `AiService` พร้อมเรียกใช้จาก Controller/Service อื่น
4. **ขยาย Business Logic** — (✅ สำเร็จแล้ว) มี `OrdersController` สำหรับจัดการออเดอร์และทริกเกอร์ Dispatch
5. **Dispatch Integration Test** — (✅ สำเร็จแล้ว) ทดสอบ End-to-End Flow: Admin -> Backend -> AI -> SignalR -> Rider ได้รับ Offer งานสำเร็จ

### 🟢 Nice-to-have
7. **CI/CD Workflows** — GitHub Actions
8. **Integration Tests** — ทดสอบการเชื่อมต่อระหว่าง services

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
> **Last Updated:** 2026-05-19 | **Session:** Planning Discussion
> 
> Section นี้รวบรวม Feature List ที่วางแผนไว้จากการพูดคุย ครอบคลุม 3 แอป: Rider App, Customer App, Store Partner App
> ใช้เป็น Reference สำหรับการพัฒนาในเฟสถัดไป

---

### 9.1 สถานะ Flutter Foundation (ณ ปัจจุบัน)

| ส่วน | ไฟล์ | สถานะ |
|---|---|---|
| GoRouter + Auth Guard | `lib/app/app_router.dart` | ✅ พร้อม |
| Riverpod State Management | ทั่วทั้งแอป | ✅ พร้อม |
| AuthService (JWT + Refresh Token + Token Clocking) | `lib/core/auth/auth_service.dart` | ✅ พร้อม |
| SignalRService (connect/disconnect/send GPS) | `lib/core/signalr/signalr_service.dart` | ✅ พร้อม |
| LocationService (Background GPS + Noise Filter) | `lib/core/location/location_service.dart` | ✅ พร้อม |
| Dio HTTP Client + Auth/Error Interceptors | `lib/core/api/delivery_api_client.dart` | ✅ พร้อม |
| Bottom Navigation Shell (4 tabs) | `lib/app/app_router.dart` → `MainShell` | ✅ พร้อม |
| Login Screen | `lib/features/auth/screens/login_screen.dart` | 🟡 Placeholder |
| Home Screen | `lib/features/home/screens/home_screen.dart` | 🟡 Placeholder |
| Active Delivery Screen | `lib/features/delivery/screens/active_delivery_screen.dart` | 🟡 Placeholder |
| Map Tracking Screen | `lib/features/tracking/screens/map_tracking_screen.dart` | 🟡 Placeholder (flutter_map มีแล้ว) |
| Delivery History Screen | `lib/features/delivery/screens/delivery_history_screen.dart` | 🟡 Placeholder |

---

### 9.2 Rider App — Feature List (เรียงตาม Execution Order)

> **หมายเหตุ Priority:**
> - 🔴 **Must Have** — ขาดไม่ได้ แอปใช้งานไม่ได้ถ้าไม่มี
> - 🟡 **Should Have** — ควรมีก่อน Production
> - 🟢 **Nice to Have** — ทำเมื่อมีเวลา

---

#### หน้า 1 — Login Screen
**Route:** `/login` | **File:** `features/auth/screens/login_screen.dart`

| # | Feature | Priority |
|---|---|---|
| 1 | Email input + Password input (obscure text) | 🔴 |
| 2 | ปุ่ม Login — เรียก `POST /api/v1/auth/login` ผ่าน Dio | 🔴 |
| 3 | บันทึก JWT + Refresh Token ผ่าน `AuthService.setTokens()` | 🔴 |
| 4 | Redirect ไป Home อัตโนมัติเมื่อ login สำเร็จ (GoRouter redirect มีแล้ว) | 🔴 |
| 5 | แสดง error message เมื่อ credentials ผิด (401) | 🔴 |
| 6 | Loading state บนปุ่มระหว่างรอ API | 🔴 |
| 7 | Validation — email format, password ไม่ว่าง (ก่อนยิง API) | 🟡 |
| 8 | ปุ่ม show/hide password | 🟡 |
| 9 | กด Enter บน keyboard แล้ว submit ได้ | 🟡 |
| 10 | Remember me (บันทึก email ไว้) | 🟢 |
| 11 | Biometric login (Face ID / Fingerprint) สำหรับครั้งถัดไป | 🟢 |

---

#### หน้า 2 — Home Screen (Dashboard)
**Route:** `/` | **File:** `features/home/screens/home_screen.dart`

| # | Feature | Priority |
|---|---|---|
| 1 | แสดงชื่อ Rider จาก JWT claims (`AuthService.userName`) | 🔴 |
| 2 | Toggle Online/Offline — ปุ่มใหญ่กลางหน้าจอ เปิด/ปิดรับงาน (Online → `SignalRService.connect()` + `LocationService.startTracking()`) | 🔴 |
| 3 | แสดงสถานะปัจจุบัน (IDLE / BUSY / OFFLINE) พร้อมสีบ่งบอก | 🔴 |
| 4 | Stats วันนี้ — งานที่ได้รับ, ส่งสำเร็จ, ระยะทางรวม (ดึงจาก API) | 🔴 |
| 5 | **Incoming Offer Bottom Sheet** — Pop-up อัตโนมัติเมื่อได้รับ `OfferReceived` จาก SignalR | 🔴 |
| 6 | Bottom Sheet: ชื่อร้าน + ระยะทาง + ค่าส่ง + Countdown 30 วินาที (progress bar) | 🔴 |
| 7 | Bottom Sheet: ปุ่ม "รับงาน" → `SignalR.AcceptOffer(offerId, version)` | 🔴 |
| 8 | Bottom Sheet: ปุ่ม "ปฏิเสธ" → `SignalR.RejectOffer(offerId, orderId)` | 🔴 |
| 9 | Bottom Sheet: ปิดอัตโนมัติเมื่อ countdown หมด | 🔴 |
| 10 | แสดงรายได้วันนี้ (คำนวณจาก completed orders × deliveryFee) | 🟡 |
| 11 | แสดงสถานะ SignalR connection (connected / reconnecting) | 🟡 |
| 12 | แสดงสถานะ GPS (active / inactive / error) | 🟡 |
| 13 | Leaderboard ตำแหน่งของ Rider เทียบกับคนอื่น | 🟢 |

---

#### หน้า 3 — Active Delivery Screen
**Route:** `/delivery/active` | **File:** `features/delivery/screens/active_delivery_screen.dart`

| # | Feature | Priority |
|---|---|---|
| 1 | ดึงออเดอร์ที่ assigned ให้ตัวเองจาก `GET /api/v1/orders/my` | 🔴 |
| 2 | แสดงชื่อร้าน + ที่อยู่ร้าน (จุดรับ) | 🔴 |
| 3 | แสดงที่อยู่ลูกค้า (จุดส่ง) | 🔴 |
| 4 | แสดงค่าส่ง | 🔴 |
| 5 | แสดง Banner สถานะปัจจุบันชัดเจน (สีต่างกันตาม state) | 🔴 |
| 6 | ปุ่ม "ออกเดินทางไปร้านแล้ว" (ASSIGNED → PICKING_UP) → `PATCH /api/v1/orders/{id}/status` | 🔴 |
| 7 | ปุ่ม "รับของแล้ว กำลังส่ง" (PICKING_UP → DELIVERING) | 🔴 |
| 8 | ปุ่ม "ส่งสำเร็จ" (DELIVERING → COMPLETED) | 🔴 |
| 9 | ปุ่ม "ดูแผนที่" → navigate ไป Map Tracking Screen | 🔴 |
| 10 | ปุ่มโทรหาลูกค้า (Click-to-call ด้วย `url_launcher`) | 🟡 |
| 11 | ปุ่มโทรหาร้านค้า | 🟡 |
| 12 | แสดง ETA (เวลาโดยประมาณ) จาก Backend | 🟡 |
| 13 | แสดงหมายเหตุจากลูกค้า (customer note) | 🟡 |
| 14 | แสดงรายการสินค้า (OrderItems — เมื่อ Backend พร้อม) | 🟡 |
| 15 | ปุ่มรายงานปัญหา (ลูกค้าไม่รับ, ที่อยู่ผิด) | 🟢 |
| 16 | ถ่ายรูปหลักฐานการส่ง | 🟢 |

---

#### หน้า 4 — Map Tracking Screen
**Route:** `/tracking` | **File:** `features/tracking/screens/map_tracking_screen.dart`

| # | Feature | Priority |
|---|---|---|
| 1 | แสดงแผนที่ OpenStreetMap (flutter_map — มีแล้ว) | 🔴 |
| 2 | หมุดตำแหน่งตัวเอง — อัปเดต real-time จาก `LocationService` (Riverpod watch) | 🔴 |
| 3 | Auto-follow — กล้องติดตามตำแหน่ง Rider อัตโนมัติ | 🔴 |
| 4 | หมุดร้านค้า (จุดรับ) — แสดงเมื่อมีงาน active | 🔴 |
| 5 | หมุดลูกค้า (จุดส่ง) — แสดงเมื่อมีงาน active | 🔴 |
| 6 | Polyline เส้นทาง — เส้นตรงจาก Rider → ร้าน → ลูกค้า | 🔴 |
| 7 | ปุ่ม "ตำแหน่งของฉัน" — recenter กล้องกลับมาที่ตัวเอง | 🔴 |
| 8 | แสดงระยะทางที่เหลือ (remaining distance) บน info card ด้านล่าง | 🟡 |
| 9 | แสดง ETA ที่เหลือ | 🟡 |
| 10 | ปุ่ม zoom in / zoom out | 🟡 |
| 11 | เปลี่ยน map style (standard / satellite) | 🟢 |
| 12 | Turn-by-turn navigation (deep link ไป Google Maps / Waze) | 🟢 |
| 13 | แสดงเส้นทางจริงตามถนน (ต้องใช้ Routing API เช่น OSRM) | 🟢 |

---

#### หน้า 5 — Delivery History Screen
**Route:** `/delivery/history` | **File:** `features/delivery/screens/delivery_history_screen.dart`

| # | Feature | Priority |
|---|---|---|
| 1 | ดึงรายการออเดอร์ที่ COMPLETED ของตัวเองจาก `GET /api/v1/orders/my` | 🔴 |
| 2 | แสดงรายการ: วันที่, ร้านค้า, ที่อยู่ส่ง, ค่าส่ง, สถานะ | 🔴 |
| 3 | Pagination หรือ infinite scroll | 🔴 |
| 4 | กรองตามวันที่ (วันนี้ / สัปดาห์นี้ / เดือนนี้) | 🟡 |
| 5 | แสดงยอดรวมรายได้ตามช่วงเวลา | 🟡 |
| 6 | แสดงระยะทางรวม | 🟡 |
| 7 | Export ประวัติเป็น PDF | 🟢 |
| 8 | กราฟรายได้รายวัน | 🟢 |

---

#### หน้า 6 — Profile Screen
**Route:** `/profile` | **File:** `features/profile/screens/profile_screen.dart` *(ยังไม่มี — ต้องสร้าง)*

| # | Feature | Priority |
|---|---|---|
| 1 | แสดงชื่อ, Email, Role จาก `AuthService` | 🔴 |
| 2 | ปุ่ม Logout — เรียก `AuthService.logout()` + navigate ไป Login | 🔴 |
| 3 | แก้ไขชื่อ / เบอร์โทร | 🟡 |
| 4 | เปลี่ยนรหัสผ่าน | 🟡 |
| 5 | แสดงรูปโปรไฟล์ (placeholder avatar ก่อน) | 🟡 |
| 6 | อัปโหลดรูปโปรไฟล์ (ต้องมี Image Upload endpoint ใน Backend ก่อน) | 🟢 |
| 7 | ตั้งค่าการแจ้งเตือน | 🟢 |
| 8 | เปลี่ยนภาษา (ไทย / อังกฤษ) | 🟢 |

---

### 9.3 Rider App — Shared Components ที่ต้องสร้าง

| Component | ใช้ที่ไหน | Priority |
|---|---|---|
| `LoadingOverlay` | ทุกหน้าที่รอ API | 🔴 |
| `ErrorSnackBar` | แสดง error message | 🔴 |
| `StatusBadge` | แสดงสถานะ Order/Rider พร้อมสี | 🔴 |
| `OfferBottomSheet` | Home Screen — pop-up รับ/ปฏิเสธงาน + countdown | 🔴 |
| `ConfirmDialog` | ก่อน logout / ปฏิเสธงาน | 🔴 |
| `EmptyStateWidget` | History / Active Delivery ตอนไม่มีข้อมูล | 🟡 |
| `ConnectionStatusBar` | แสดงสถานะ SignalR ด้านบน | 🟡 |
| `CountdownTimer` | ใน OfferBottomSheet | 🔴 |

---

### 9.4 Rider App — Riverpod Providers ที่ต้องสร้าง

| Provider | หน้าที่ | Priority |
|---|---|---|
| `orderProvider` | ดึงและ cache ออเดอร์ปัจจุบัน | 🔴 |
| `activeOrderProvider` | ออเดอร์ที่กำลัง active อยู่ | 🔴 |
| `riderStatusProvider` | สถานะ IDLE/BUSY/OFFLINE | 🔴 |
| `incomingOfferProvider` | เก็บ Offer ที่เข้ามาจาก SignalR | 🔴 |
| `orderHistoryProvider` | ประวัติออเดอร์ | 🔴 |
| `earningsProvider` | คำนวณรายได้วันนี้ | 🟡 |
| `etaProvider` | เวลาโดยประมาณ | 🟡 |

---

### 9.5 Rider App — Execution Order (Step-by-Step)

```
Step 1  Login Screen        — UI จริง + เชื่อม AuthService
Step 2  Home Screen         — Toggle Online/Offline + Stats
Step 3  OfferBottomSheet    — SignalR OfferReceived → UI + Countdown
Step 4  Active Delivery     — Order detail + Status transition buttons
Step 5  Map Tracking        — GPS marker + Polyline + Auto-follow
Step 6  Delivery History    — List + Pagination
Step 7  Profile Screen      — ข้อมูล + Logout
Step 8  Shared Components   — LoadingOverlay, ErrorSnackBar, StatusBadge ฯลฯ
```

> **Core Flow:** Step 1–5 คือสิ่งที่ขาดไม่ได้ ถ้าทำครบ 5 step นี้ Rider App ใช้งานได้จริง

---

### 9.6 Customer App — Feature List (ยังไม่มี — ต้องสร้างใหม่)

> **หมายเหตุ:** ต้องรอให้ Backend Tier 1 เสร็จก่อน (Customer Real-time Events, Customer Addresses, Menu System, OrderItems)

#### หน้าที่ต้องมี

| # | หน้า | Feature หลัก | Priority |
|---|---|---|---|
| 1 | **Login / Register** | Form, validation, JWT, Role = Customer | 🔴 |
| 2 | **Home / Store List** | รายการร้านค้า, ระยะทาง (PostGIS), Rating, สถานะเปิด/ปิด | 🔴 |
| 3 | **Store Detail + Menu** | รายการเมนู, Option Groups (ขนาด/ท็อปปิ้ง), ราคา, ปุ่มเพิ่มตะกร้า | 🔴 |
| 4 | **Cart Screen** | รายการสินค้า, ราคารวม, ค่าส่ง, หมายเหตุ, ปุ่มสั่งซื้อ | 🔴 |
| 5 | **Address Picker** | ปักหมุดบนแผนที่, ค้นหาที่อยู่ (Geocoding), บันทึกที่อยู่ประจำ | 🔴 |
| 6 | **Order Confirmation** | สรุปออเดอร์ก่อนจ่าย, เลือกวิธีชำระ (เงินสด/โอน) | 🔴 |
| 7 | **Live Order Tracking** | แผนที่ Rider เคลื่อนที่ Real-time (SignalR), Timeline สถานะ, ETA | 🔴 |
| 8 | **Order History** | รายการออเดอร์ที่ผ่านมา, ปุ่มสั่งซ้ำ (Reorder) | 🟡 |
| 9 | **Rating Screen** | ให้ดาว Rider + ร้านค้า หลังส่งสำเร็จ | 🟡 |
| 10 | **Profile + Saved Addresses** | แก้ไขข้อมูล, จัดการที่อยู่ | 🟡 |

#### Backend ที่ต้องพร้อมก่อน Customer App
- `CustomerId` field ใน Order model
- `CustomerAddress` entity + CRUD endpoints
- `OrderItem` entity + Menu system
- `OrderStatusChanged` SignalR broadcast → `customer:{userId}` group
- `RiderLocationUpdated` broadcast → customer เมื่อ Rider กำลังส่งของ
- FCM Push Notification (แจ้งเตือนแม้แอปปิด)
- ETA Calculation Service

---

### 9.7 Store Partner App — Feature List (ยังไม่มี — ต้องสร้างใหม่)

> **หมายเหตุ:** ต้องรอให้ Backend Menu System และ Store Order Management เสร็จก่อน

#### หน้าที่ต้องมี

| # | หน้า | Feature หลัก | Priority |
|---|---|---|---|
| 1 | **Login / Register** | Form, Role = StorePartner | 🔴 |
| 2 | **Store Dashboard** | ยอดขายวันนี้, จำนวนออเดอร์, Toggle เปิด/ปิดร้าน | 🔴 |
| 3 | **Incoming Orders Board** | Real-time list ออเดอร์ใหม่ (SignalR), เสียงแจ้งเตือน, ปุ่มรับ/ปฏิเสธ | 🔴 |
| 4 | **Order Detail** | รายการสินค้า, หมายเหตุลูกค้า, ปุ่ม "อาหารพร้อมแล้ว" → trigger Rider | 🔴 |
| 5 | **Menu Management** | เพิ่ม/แก้ไข/ลบเมนู, อัปโหลดรูป, Mark Sold Out, Option Groups | 🔴 |
| 6 | **Order History** | ประวัติออเดอร์, ยอดขายรายวัน | 🟡 |
| 7 | **Store Profile** | แก้ไขข้อมูลร้าน, เวลาเปิด-ปิด, รูปร้าน | 🟡 |

#### Backend ที่ต้องพร้อมก่อน Store Partner App
- `MenuItem` + `MenuCategory` entities + CRUD endpoints
- `IsOpen`, `PrepTimeMinutes`, `OpeningHours` ใน Shop model
- `GET /api/v1/shops/{id}/orders` — ดูออเดอร์ของร้านตัวเอง
- `POST /api/v1/orders/{id}/ready` — ร้านกด "อาหารพร้อม" → trigger Rider
- `PATCH /api/v1/shops/{id}/toggle-status` — เปิด/ปิดร้าน
- Image Upload endpoint
- FCM Push Notification

---

### 9.8 Backend — สิ่งที่ต้องทำก่อน Mobile App (Priority Order)

#### 🔴 Tier 1 — Critical (Mobile ทำงานไม่ได้ถ้าไม่มี)

| # | งาน | เหตุผล |
|---|---|---|
| 1 | Customer Real-time Events — broadcast `OrderStatusChanged` + `RiderLocationUpdated` → `customer:{id}` | Customer App ติดตาม order ไม่ได้ |
| 2 | `CustomerAddress` entity + `GET/POST/PUT/DELETE /api/v1/customer/addresses` | Customer ต้องระบุที่อยู่จัดส่ง |
| 3 | `MenuItem` + `MenuCategory` + `MenuItemsController` | Customer สั่งอาหารไม่ได้ |
| 4 | `OrderItem` entity + อัปเดต `CreateOrderDto` ให้รับ Items[] + ShopId + CustomerId | Order ไม่รู้ว่าสั่งอะไร |
| 5 | FCM Push Notification — `FcmToken` field + `POST /api/v1/notifications/register-token` + `FcmNotificationService` | แจ้งเตือนแม้แอปปิด |

#### 🟡 Tier 2 — Important (ต้องมีก่อน Production)

| # | งาน |
|---|---|
| 6 | ETA Calculation Service + `EstimatedArrivalAt` field ใน Order |
| 7 | Rating & Review System — `Rating` entity + `POST /api/v1/orders/{id}/rate` |
| 8 | Pricing Service — ย้ายออกจาก hardcode ใน OrdersController |
| 9 | Password Reset Flow — `POST /api/v1/auth/forgot-password` + `reset-password` |
| 10 | Image Upload — `POST /api/v1/uploads/image` |
| 11 | Store Order Management — `GET /api/v1/shops/{id}/orders` + `POST /api/v1/orders/{id}/ready` |

#### 🟢 Tier 3 — Nice-to-have

| # | งาน |
|---|---|
| 12 | Payment / Transaction System |
| 13 | Admin User Management endpoints |
| 14 | Analytics endpoints |
| 15 | Service Area / Geofencing |
| 16 | Promo Code System |

---

### 9.9 Angular Admin Dashboard — สิ่งที่ต้องทำเพิ่ม

| # | งาน | Priority |
|---|---|---|
| 1 | **Map — Route Follow Fix** — แก้ Defect หมุดลอย ให้ Rider เดินตามเส้นทางจริง + Auto-zoom | 🔴 |
| 2 | **Riders Management Page** — ตาราง Rider ทั้งหมด, สถานะ, ตำแหน่งล่าสุด, Activate/Deactivate | 🔴 |
| 3 | **Order Detail Modal** — คลิกออเดอร์แล้วเห็น Items, ร้านค้า, ลูกค้า, Timeline สถานะ | 🟡 |
| 4 | **Shop Management Page** — ตาราง Shop, แก้ไขข้อมูล, เปิด/ปิดร้าน | 🟡 |
| 5 | **User Management Page** — Admin จัดการ User ทั้งหมด, เปลี่ยน Role | 🟡 |

---

### 9.10 Overall Progress Summary (ณ 2026-05-19)

```
Backend API           ████████████████████  100%  ✅ Phase 4 Complete
AI Engine             ███████████████████░   95%  🟢 Waiting for full integration
Admin Dashboard       ██████████████░░░░░░   70%  🟢 Core working, needs fixes
Rider App             ██████░░░░░░░░░░░░░░   30%  🟡 Foundation ready, UI needed
Customer App          ░░░░░░░░░░░░░░░░░░░░    0%  ❌ Not started (needs Backend Tier 1)
Store Partner App     ░░░░░░░░░░░░░░░░░░░░    0%  ❌ Not started (needs Backend Menu System)

Overall Project       ████████████░░░░░░░░  ~60%
```

**Next Immediate Action:** เริ่มจาก Backend Tier 1 (#1 Customer Real-time Events) → จากนั้น Rider App Step 1 (Login Screen UI)
