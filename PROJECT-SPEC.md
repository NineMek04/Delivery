# Project Specification

> **⚠️ AI AGENT NOTICE:** 
> This file is a **Historical Archive (Layer 1)**. Do not load this entire file into memory unless absolutely necessary. Start with `AI-INDEX.md` to route to specific partitioned specs in `.docs/ai-context/`.

## AI-Optimized Smart Delivery Routing System
### ระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์

> **Version:** 0.9.0 (Phase 5: Real-world Routing & Real-time Dispatch Simulation)  
> **Last Updated:** 2026-05-20  
> **Team Lead:** นนท์ธรัตน์ ทาลา

---

## 1. Overview

ระบบนี้เป็นแพลตฟอร์มจำลองการจัดส่งสินค้า/อาหารที่ใช้ AI ช่วยคำนวณเส้นทางที่เหมาะสมสำหรับงานแบบ multi-drop หรือ batched orders โดยพัฒนาเป็น microservices บน Docker เพื่อให้ setup และทดสอบแต่ละ service ได้ง่าย
**เน้นเป็นพิเศษ:** ระบบจับตำแหน่งและติดตาม Rider แบบ Real-time ด้วย High-frequency GPS data.
**Zero-SDK Setup:** สมาชิกในทีมไม่จำเป็นต้องติดตั้ง .NET, Python, หรือ Node.js ในเครื่อง ขอเพียงมี Docker Desktop ก็สามารถรันระบบทั้งหมดได้ทันที

### Problems To Solve

- เส้นทางจัดส่งที่ไม่ได้ optimize ทำให้เสียเวลาและเชื้อเพลิง
- งานจัดส่งหลายจุดต้องการการจัดลำดับ waypoint ที่เหมาะสม
- ต้องการ real-time GPS tracking ระหว่าง Rider mobile app และ Admin dashboard
- ต้องการ prototype ที่ใช้ smartphone เป็น GPS sensor แทน hardware เฉพาะทาง

### Core Features

| Feature | Description | Status |
|---|---|---|
| AI Route Optimization | คำนวณเส้นทางด้วย VRP algorithm ผ่าน Google OR-Tools | **Foundation Ready** |
| Real-time GPS Tracking | ระบบจับตำแหน่งและส่งพิกัด Rider ผ่าน SignalR/WebSocket | **Ready (Redis Speed Layer)** |
| Dispatch Orchestrator | ระบบจองและเสนอขายงานให้ Rider อัตโนมัติ (30s Lifecycle) | **Phase A Ready** |
| Admin Dashboard | Dashboard สำหรับดู order/rider/map แบบ real-time | **Core Architecture Ready** |
| Rider Mobile App | Flutter app สำหรับส่ง GPS และรับเส้นทาง | **Foundation Ready** |
| Dockerized Services | รันระบบด้วย Docker Compose ครบ 5 services | **Ready** |
| Backend Security | JWT, Refresh Token, Rotation, Role policy | **Enhanced** |
| PostGIS Spatial Tuning & Scaling | GiST Indexes, Range Partitioning รายเดือน, Database Clustering | **Completed** |
| Universal Tracking & OSRM | รหัส Tracking Code ที่อ่านง่าย (ORD-xxx), เส้นทางโค้งจริงแบบออฟไลน์ด้วย OSRM | **Completed** |
| Integration & Stress Test | รันสอบ E2E ผ่าน Testcontainers (xUnit) และ Node.js Load Scripts | **Completed** |
| Operational Intelligence | ระบบ Analytics API และ AI-ETA Prediction Engine | **Completed** |
| Simulation Sandbox | ระบบจำลองการยิง GPS, สมัครร้านค้า, การวิ่งตามถนนด้วย Polyline Decoder | **Completed** |

---

## 2. Tech Stack

| Layer | Technology | Version / Notes |
|---|---|---|
| Backend API | ASP.NET Core | .NET 8 |
| Backend ORM | EF Core + Npgsql + NetTopologySuite | 8.0.11 |
| Backend API Docs | Swagger / Swashbuckle | 6.6.2 (Enhanced with XML Comments) |
| Backend Security | JWT Bearer Authentication | 8.0.11 + Security Baseline |
| Backend Logging | Serilog | 10.0.0 (File Sink) |
| Real-time | SignalR | ASP.NET Core built-in |
| Database | PostgreSQL + PostGIS | `postgis/postgis:15-3.3` |
| Cache (Hot Data) | Redis | `redis:7-alpine` |
| AI Engine | Python FastAPI + OR-Tools | **Ready** (VRP + Scorer implemented) |
| Frontend | Angular | 19.2.0, standalone components |
| Mobile | Flutter | **Initialized** (rider_app) |
| Container | Docker Compose | v3.8 |

### Backend Packages

| Package | Purpose | Status |
|---|---|---|
| `Microsoft.AspNetCore.OpenApi` | OpenAPI support | Added |
| `Swashbuckle.AspNetCore` | Swagger UI | Added |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT validation | Added |
| `Microsoft.EntityFrameworkCore` | ORM | Added |
| `Microsoft.EntityFrameworkCore.Design` | EF migrations tooling | Added |
| `Microsoft.EntityFrameworkCore.Tools` | EF tools | Added |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL provider | Added |
| `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite` | PostGIS geometry mapping | Added |
| `Mapster` | Object Mapping (Entity <-> DTO) | Added |
| `FluentValidation` | Automatic Request Validation | Added |
| `StackExchange.Redis` | Redis Client for .NET | Added |
| `Microsoft.AspNetCore.Mvc.NewtonsoftJson` | JSON handling for Spatial types | Added |

---

## 3. System Architecture

```text
Flutter Rider App
    |
    | SignalR / WebSocket
    v
.NET Backend API  <──── REST ────>  Python AI Service (Port 8000)
    |
    | EF Core (The Ledger)           | Redis (The Pulse)
    v                                v
PostgreSQL + PostGIS           GPS, Locks, Presence (Hot Data)
    ^
    |
    | REST (Fluent API)
    v
Angular Admin Dashboard (Port 80)
```

### Docker Services

| Service | Container | Build / Image | Port | Status |
|---|---|---|---|---|
| `db` | `delivery-db` | `postgis/postgis:15-3.3` | `5432:5432` | Ready |
| `redis` | `delivery-redis` | `redis:7-alpine` | `6379:6379` | **New** |
| `backend` | `delivery-backend` | `./BackendApi/Dockerfile` | `5000:80` | Ready |
| `ai-service` | `delivery-ai` | `./ai-engine/Dockerfile` | `8000:8000` | **Ready** |
| `frontend` | `delivery-frontend` | `./admin-dashboard/Dockerfile` | `80:80` | **Ready** |

### Data Flow

1. Rider app sends GPS location to Backend via SignalR.
2. Backend stores GPS/order data in PostgreSQL/PostGIS.
3. Backend sends order and rider data to AI service (`/api/optimize-route`) for VRP optimization.
4. AI service returns optimized waypoint sequence.
5. Backend saves route result and broadcasts updates to Rider app and Admin dashboard.

---

## 4. Project Structure

```text
Delivery/
├── Delivery.sln
├── docker-compose.yml
├── AI-BLUEPRINT.md
├── AI-CHANGELOG.md
├── PROJECT-SPEC.md
├── .cursorrules
├── BackendApi/
│   ├── Dockerfile
│   ├── Core/
│   │   ├── Models/
│   │   │   ├── ApiResponse.cs        ← Standard JSON Wrapper
│   │   │   └── PaginatedResult.cs    ← Pagination Model
│   │   ├── Filters/
│   │   │   ├── GlobalResponseFilter.cs
│   │   │   ├── GlobalExceptionFilter.cs
│   │   │   └── ValidationFilter.cs
│   │   ├── Mappings/
│   │   │   └── MappingConfig.cs       ← Mapster (Point ↔ Lat/Lng)
│   │   ├── CrudControllerBase.cs     ← Generic CRUD Base
│   │   └── DeliveryControllerBase.cs
│   ├── Controllers/
│   │   ├── MasterData/
│   │   │   └── RidersController.cs   ← Example CRUD
│   │   └── Business/
│   ├── Models/
│   │   ├── DTOs/                     ← Type-safe API Contract
│   │   │   ├── OrderDto.cs
│   │   │   ├── Models/
│   │   ├── RiderLocationHistory.cs    ← PostGIS GPS Ledger
│   │   ├── Rider.cs                   ← Added RiderState
│   │   └── Order.cs                   ← Added OrderState & Offer Version
│   └── Setup/
│       ├── ServiceSetup.cs           ← DI, Redis, BackgroundWorkers
│       └── ApplicationSetup.cs       ← Middleware pipeline (CORS Top)
├── ai-engine/
│   ├── main.py                       ← FastAPI + OR-Tools VRP Solver
│   ├── requirements.txt
│   └── Dockerfile
├── admin-dashboard/
│   ├── Dockerfile
│   ├── src/app/core/
│   │   ├── http/
│   │   │   └── delivery-http-request.ts ← Fluent HTTP Client
│   │   └── interceptors/             ← Auth & Error Handlers
│   ├── src/app/api/generated/        ← Gen from OpenAPI (Planned)
│   └── openapitools.json             ← OpenAPI Generator Config
├── rider_app/                        ← Flutter Mobile App
│   ├── pubspec.yaml
│   ├── Rider_app.md                  ← Implementation Plan
│   └── lib/
└── .github/workflows/
```

---

## 5. Backend API Specification

### Current Backend Foundation

- **Global Response Wrapper:** ทุก API คืนค่าในรูปแบบ `ApiResponse<T>` เพื่อความสอดคล้องกับ Frontend
- **Automatic Validation:** ใช้ `FluentValidation` + `ValidationFilter` ตรวจสอบ Model อัตโนมัติ
- **Spatial Mapping:** ใช้ `Mapster` จัดการแปลงพิกัด `NetTopologySuite.Geometries.Point` เป็น `Lat/Lng` ใน DTO
- **Generic CRUD:** `CrudControllerBase` รองรับการทำ Master Data API อย่างรวดเร็ว พร้อม Pagination ในตัว
- **Enhanced Swagger:** รองรับ XML Comments และ Response Types ครบถ้วน เพื่อใช้กับ OpenAPI Generator
- **Enterprise Auditing:** ระบบบันทึกข้อมูลผู้สร้าง/แก้ไข/ลบ และ IP Address อัตโนมัติในระดับ DbContext

### Data Management & Auditing (Enterprise Standard)

ระบบมีการจัดการข้อมูลด้วย Layered Base Entities เพื่อให้รองรับการตรวจสอบย้อนหลัง (Audit Trail) และการทำงานแบบ Concurrency ในระบบ Real-time

#### 1. Layered Base Entities
| Class | Capabilities |
|---|---|
| `BaseEntity<T>` | มี **RowVersion** (Concurrency Token) ป้องกันการบันทึกทับกัน |
| `BaseAuditableEntity<T>` | เพิ่มฟิลด์ `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy` (Hybrid ID/Name) และ IP |
| `BaseSoftDeleteEntity<T>` | เพิ่มระบบ **Soft Delete** (`IsDeleted`, `DeletedAt`, `DeletedBy`, `DeletedFromIp`) |

#### 2. กรณีการดึงข้อมูล (Data Retrieval Cases)
เพื่อให้ทีมงานและ Admin เข้าใจพฤติกรรมของข้อมูลในระบบ:

| Case | Behavior (พฤติกรรม) | Note สำหรับ Admin |
|---|---|---|
| **การดึงข้อมูลปกติ (Standard Fetch)** | ระบบจะซ่อนข้อมูลที่ `IsDeleted = true` ให้อัตโนมัติ | ข้อมูลที่ลบไปแล้วจะไม่โผล่ในหน้า Dashboard ปกติ |
| **การลบข้อมูล (Deletion)** | ข้อมูลจะไม่ถูกลบออกจาก DB แต่จะถูก Mark `IsDeleted = true` | ข้อมูลยังอยู่ครบถ้วนเพื่อใช้ตรวจสอบย้อนหลังได้ |
| **การกู้คืนข้อมูล (Recovery)** | Admin สามารถเปลี่ยน `IsDeleted` กลับเป็น `false` เพื่อกู้คืนได้ | ต้องทำผ่าน DB หรือหน้า Admin พิเศษ |
| **การตรวจสอบคนทำ (Audit)** | ดูฟิลด์ `CreatedBy...` หรือ `UpdatedBy...` เพื่อหาตัวคนทำ | ระบบเก็บทั้ง User ID, ชื่อ, และ IP ของเครื่องที่ทำ |
| **การสมัครซ้ำ (Re-registration)** | สามารถสมัครด้วย Email เดิมที่เคยถูก Soft Delete ไปแล้วได้ | ระบบแก้ปัญหา Unique Index ให้เช็คเฉพาะที่ยังไม่ลบ |
| **ความขัดแย้งของข้อมูล (Concurrency)** | หากมีคนแก้ข้อมูลพร้อมกัน ระบบจะแจ้งเตือนผ่าน `RowVersion` | ป้องกันการกด Save ทับข้อมูลที่เพื่อนร่วมงานเพิ่งแก้ |

---

### Backend Data Handler Core

`DBHandlerCore` เป็นหัวใจสำคัญในการเข้าถึงข้อมูลผ่าน EF Core โดยเลียนแบบ Pattern ที่คุ้นเคย (GetObjectList, InsertObject, etc.)

| Capability | Method / Property |
|---|---|
| Query entities | `DB.GetQuery<TEntity>()` |
| Query list | `DB.GetObjectListAsync<TEntity>()` |
| Find by key | `DB.GetObjectByKeyAsync<TEntity>(key)` |
| Insert / Update | `DB.InsertObject(entity)` / `DB.UpdateObject(entity)` |
| Pagination | `DB.GetPaginatedListAsync<TEntity>(page, pageSize)` |
| Commit | `DB.CommitChangesAsync()` |

### API Endpoints (v1)

| Method | Endpoint | Description | Status |
|---|---|---|---|
| GET | `/api/v1/riders` | ดึงข้อมูล Rider (แบ่งหน้า) | **Ready** |
| GET | `/api/v1/riders/{id}` | ดึงข้อมูล Rider ตาม ID | **Ready** |
| POST | `/api/auth/login` | Login และออก JWT | Planned |
| POST | `/api/optimize-route` (AI) | รัน VRP Optimization | **Ready (AI Engine)** |
| WebSocket | `/hubs/tracking` | SignalR Hub สำหรับ GPS | Registered |

---

## 6. Backend Security Baseline

- **JWT Auth:** รองรับทั้ง `Authorization: Bearer` และ HttpOnly Cookie `access_token`
- **Refresh Token:** ระบบ Token Rotation (Access + Refresh) เพื่อความปลอดภัยสูงสุด
- **Role Policies:** `AdminOnly`, `Operations`, `Rider`
- **Rate Limiting:** มี `auth` policy สำหรับป้องกัน Brute-force
- **Security Headers:** ตั้งค่า `Referrer-Policy`, `X-Frame-Options`, ฯลฯ ผ่าน Middleware

---

## 7. Database Specification

- **PostGIS:** ใช้ SRID 4326 เสมอ
- **Indexing:** แผนการเพิ่ม GiST Index สำหรับ `CurrentLocation` และ `Pickup/DropoffLocation`
- **EF Core:** ใช้ `NetTopologySuite` สำหรับการคำนวณเชิงพื้นที่ในระดับ Code

---

## 8. Frontend Architecture (Angular)

- **Fluent HTTP Request:** ใช้ `req<T>(path).body(data).post()` เพื่อการเขียน Code ที่อ่านง่าย
- **Interceptors:** 
    - `AuthInterceptor`: แนบ Token อัตโนมัติ
    - `ErrorInterceptor`: จัดการ 401, 403, 500 พร้อม SweetAlert2
- **OpenAPI Integration:** เตรียมพร้อมสำหรับรัน `npm run generate:api` เพื่อสร้าง Models จาก Backend Swagger

---

## 9. AI Engine Specification (Python)

- **FastAPI:** High-performance web framework
- **OR-Tools Solver:** 
    - `RoutingIndexManager` & `RoutingModel`
    - `PATH_CHEAPEST_ARC` strategy
    - Linear Distance Matrix (Haversine/Euclidean approximation)
- **Endpoint:** `POST /api/optimize-route`

---

## 10. Next Tasks (Updated)

### Backend
- [x] Add User/Auth domain model, JWT, and Serilog logging.
- [x] Add AuthController (Login/Logout/Refresh Token).
- [x] Implement `TrackingHub` logic, Dispatch Orchestrator, and Redis integration.
- [x] **Run Database Migrations** (`dotnet ef database update`).
- [x] Add `OrdersController` (Business logic for multi-drop).
- [x] Add `AiService` HttpClient to communicate with AI Engine.

### Frontend / Mobile
- [x] Run `npm run generate:api` in Angular to sync DTOs.
- [x] Build Angular Dashboard Map View (Leaflet) + real-time SignalR UI (Sim Map).
- [x] Initialize **Flutter Rider App** project and AuthService.
- [ ] Build real UI for Rider App and run `build_runner`.
- [ ] Implement Phase 2: Background GPS Service and end-to-end dispatch receiving in Rider App.

### AI Engine
- [x] Implement FastAPI service.
- [x] Add OR-Tools VRP solver and Phase A Heuristic Scorer.
- [x] Wait for BackendApi to integrate and call its endpoints (`AiService` added).

### Integration
- [x] End-to-end flow: create order → VRP optimize → OSRM routing → assign rider → broadcast tracking.
- [x] Docker Compose full dispatch smoke test (ผ่าน `simulate-e2e.js`).
- [x] Add CI build workflow (GitHub Actions).
- [x] Integration Tests (xUnit + Testcontainers) 18/18 Passed.
- [x] Stress / Load testing (SignalR, API, Dispatch).
- [x] Analytics API & ETA Prediction Engine.

---

## 11. Getting Started (For Team Members)

### 11.1 Run with Docker (Recommended)
1. **Prerequisites:** ติดตั้ง Docker Desktop และเปิดใช้งาน **WSL 2 Backend** (แนะนำสำหรับ Windows)
2. **Configuration Check:** ตรวจสอบว่าในเครื่องมีพอร์ตชนหรือไม่ (80, 5000, 5432, 6379, 8000) หากมีโปรแกรมเดิมรันอยู่ให้ปิดก่อน
3. **Start Project:** เปิด Terminal ในโฟลเดอร์ Root ของโปรเจกต์ แล้วรันคำสั่งเดียวเพื่อสร้างและเริ่มทำงานทุก Service:
   ```bash
   docker-compose up -d --build
   ```
4. **Database Migration:** สำหรับเครื่องที่ไม่มี .NET SDK ให้รันคำสั่งนี้เพื่ออัปเดต Schema ของฐานข้อมูลภายใน Container:
   ```bash
   docker-compose exec backend dotnet ef database update
   ```
5. **Verification:** ตรวจสอบว่าทุก Container รันอยู่ด้วยคำสั่ง `docker-compose ps` หรือดูผ่าน Docker Desktop Dashboard (ต้องขึ้นสีเขียวครบทั้ง 5 services)

### 11.2 Docker Desktop Optimization
เพื่อให้ระบบประมวลผลเส้นทาง (AI) และการจัดเก็บพิกัด (PostGIS) ทำงานได้เสถียร ควรตั้งค่าทรัพยากรดังนี้:
- **Settings > Resources**: ปรับ RAM อย่างน้อย **4GB** (แนะนำ 8GB หาก RAM เครื่องเพียงพอ)
- **Settings > General**: มั่นใจว่าติ๊กเลือก **"Use the WSL 2 based engine"**

### 11.3 Troubleshooting
- **CORS Issue:** หาก Browser แจ้งเตือน CORS ให้ตรวจสอบค่า `Cors__AllowedOrigins` ใน `docker-compose.yml`
- **DB Connection Error:** ตรวจสอบว่า `delivery-db` พร้อมใช้งานก่อนที่ `delivery-backend` จะเริ่มทำงาน (หากพลาดให้สั่ง `docker-compose restart backend`)
- **Logs:** ดู Error เพิ่มเติมได้ด้วยคำสั่ง `docker-compose logs -f [service_name]`

### 11.4 Running the E2E Dispatch & Delivery Simulator (Node.js)
ระบบมีชุด Script สำหรับทดลองวิ่งจำลองทราฟฟิกจริง (End-to-End Simulation) เพื่อจำลอง flow การจองคิวและการเดินทางเชิงพื้นที่ของ Rider ใน จ.อุดรธานี โดยไม่ต้องเปิดหน้าต่างแอปพลิเคชันจริง:

1. **เตรียมความพร้อม:**
   - ติดตั้ง [Node.js](https://nodejs.org) (v18 ขึ้นไป) บนเครื่องโฮสต์
   - มั่นใจว่าคอนเทนเนอร์ระบบหลัก (`delivery-db`, `delivery-redis`, `delivery-backend`, `delivery-ai`) ทำงานอยู่อย่างครบถ้วนและ Healthy
2. **ติดตั้ง Dependencies:**
   เปิด Terminal และเข้าไปยังโฟลเดอร์ของ Simulator แล้วติดตั้ง library ที่ใช้รัน:
   ```bash
   cd scripts/e2e-simulator
   npm install
   ```
3. **เริ่มรันการจำลอง (Simulation):**
   รันคำสั่งด้านล่างเพื่อเริ่มกระบวนการจัดส่งแบบ real-time:
   ```bash
   node simulate-e2e.js
   ```
4. **กระบวนการที่ Simulator ทำการจำลองอัตโนมัติ:**
   - **Step 0-1:** ตรวจสอบความแข็งแรงของระบบ (Health Check) และเข้าสู่ระบบในชื่อ Admin และ Rider 1
   - **Step 2:** สร้างร้านค้าจำลอง (Shop) ใน จ.อุดรธานี
   - **Step 3:** เชื่อมต่อ Rider เข้าสู่ WebSocket (`TrackingHub` SignalR) และยิงพิกัด GPS จำลองเข้าสู่ Redis
   - **Step 4:** สร้างออเดอร์ใหม่ -> ส่งพิกัดเข้า AI Engine -> รับ Offer ข้อเสนอบน Rider Hub -> ตอบตกลงรับงาน
   - **Step 5:** จำลองการเดินทางของ Rider ทีละพิกัด (12 จุดแรกเข้าหาพิกัดร้านค้าอุดร, อัปเดตสถานะเป็น `PICKING_UP`, และเดินทางอีก 15 จุดมุ่งหน้าหาที่อยู่ผู้รับปลายทาง, อัปเดตสถานะเป็น `DELIVERING` และ `COMPLETED` เพื่อล้างสถานะ Rider ให้กลับมาว่างอีกครั้ง)

### 11.5 URLs & Ports

| Service | Local URL | Docker URL |
|---|---|---|
| Frontend | `http://localhost:4200` | `http://localhost` |
| Backend Swagger | `http://localhost:5000/swagger` | `http://localhost:5000/swagger` |
| AI Docs | `http://localhost:8000/docs` | `http://localhost:8000/docs` |
| AI Health | `http://localhost:8000/health` | `http://localhost:8000/health` |
| Database | `localhost:5432` | `db:5432` (Internal) |
| Redis | `localhost:6379` | `redis:6379` (Internal) |

---

## 12. Environment Notes

- Development machine may be ASUS ROG; avoid GPU-dependent implementation unless required.
- For npm work, check `.npmrc` and VPN/private registry status first.
- `Jwt__Key` must be set before running BackendApi.
- `.env` is for local development only and should not contain production secrets in git.
- Database password in sample config must be changed before non-local use.

---

## 13. Related Documents

- [AI-BLUEPRINT.md](./AI-BLUEPRINT.md)
- [AI-CHANGELOG.md](./AI-CHANGELOG.md)
- [.cursorrules](./.cursorrules)
- [docker-compose.yml](./docker-compose.yml)
