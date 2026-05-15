# AI-BLUEPRINT: Smart Delivery Routing System

> **ชื่อโครงการ:** ระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์  
> **English:** AI-Optimized Smart Delivery Routing System  
> **ผู้พัฒนา:** นายนนท์ธรัตน์ ทาลา  
> **Version:** 0.5.0 (Phase 2: Real-time Dispatch Orchestration — Heart vs. Brain)  
> **Last Updated:** 2026-05-14

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

### สถานะรวม: 🔵 **Phase 2 — Real-time Dispatch Orchestration**

| Component            | Status            | รายละเอียด                                                    |
|----------------------|-------------------|--------------------------------------------------------------|
| **docker-compose**   | ✅ Created         | 5 services: db, backend, ai-service, frontend, **redis**     |
| **PostGIS DB**       | ✅ Running         | SRID 4326 standard, PostGIS extension ready                  |
| **Redis Cache**      | ✅ Running         | Used for GPS speed layer, presence, and distributed locking  |
| **BackendApi**       | 🟢 90% Ready       | Auth, Serilog, TrackingHub, Dispatch Orchestrator, `AiService`, `OrdersController` created. |
| **ai-engine**        | 🟢 95% Ready       | FastAPI + OR-Tools VRP solver & Phase A Scorer. Waiting for Backend to call. |
| **admin-dashboard**  | 🟡 40% Ready       | Architecture ready. Missing Map UI and OpenAPI generation.   |
| **rider_app**        | 🟡 30% Ready       | Foundation ready. Needs real UI, build_runner, and Background GPS logic. |
| **SignalR Hub**      | ✅ Ready           | `TrackingHub` refactored to use Redis presence & GPS buffer  |
| **Database Migration**| ✅ Applied         | Run `dotnet ef database update` successfully for new dispatch & auth fields. |
| **Backend Security** | ✅ Enhanced        | JWT, Refresh Token, Rotation, Role policy, Serilog logging added |

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
