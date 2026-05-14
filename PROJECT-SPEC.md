# Project Specification
## AI-Optimized Smart Delivery Routing System
### ระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์

> **Version:** 0.4.1 (Auth System Enhanced with Refresh Token & Rotation)  
> **Last Updated:** 2026-05-14  
> **Team Lead:** นนท์ธรัตน์ ทาลา

---

## 1. Overview

ระบบนี้เป็นแพลตฟอร์มจำลองการจัดส่งสินค้า/อาหารที่ใช้ AI ช่วยคำนวณเส้นทางที่เหมาะสมสำหรับงานแบบ multi-drop หรือ batched orders โดยพัฒนาเป็น microservices บน Docker เพื่อให้ setup และทดสอบแต่ละ service ได้ง่าย
**เน้นเป็นพิเศษ:** ระบบจับตำแหน่งและติดตาม Rider แบบ Real-time ด้วย High-frequency GPS data.

### Problems To Solve

- เส้นทางจัดส่งที่ไม่ได้ optimize ทำให้เสียเวลาและเชื้อเพลิง
- งานจัดส่งหลายจุดต้องการการจัดลำดับ waypoint ที่เหมาะสม
- ต้องการ real-time GPS tracking ระหว่าง Rider mobile app และ Admin dashboard
- ต้องการ prototype ที่ใช้ smartphone เป็น GPS sensor แทน hardware เฉพาะทาง

### Core Features

| Feature | Description | Status |
|---|---|---|
| AI Route Optimization | คำนวณเส้นทางด้วย VRP algorithm ผ่าน Google OR-Tools | **Foundation Ready** |
| Real-time GPS Tracking | ระบบจับตำแหน่งและส่งพิกัด Rider ผ่าน SignalR/WebSocket | Foundation Ready |
| Admin Dashboard | Dashboard สำหรับดู order/rider/map แบบ real-time | **Core Architecture Ready** |
| Rider Mobile App | Flutter app สำหรับส่ง GPS และรับเส้นทาง | **Foundation Ready** |
| Dockerized Services | รันระบบด้วย Docker Compose ครบ 4 services | **Ready** |
| Backend Security | JWT, Refresh Token, Rotation, Role policy | **Enhanced** |

---

## 2. Tech Stack

| Layer | Technology | Version / Notes |
|---|---|---|
| Backend API | ASP.NET Core | .NET 8 |
| Backend ORM | EF Core + Npgsql + NetTopologySuite | 8.0.11 |
| Backend API Docs | Swagger / Swashbuckle | 6.6.2 (Enhanced with XML Comments) |
| Backend Security | JWT Bearer Authentication | 8.0.11 + Security Baseline |
| Real-time | SignalR | ASP.NET Core built-in |
| Database | PostgreSQL + PostGIS | `postgis/postgis:15-3.3` |
| AI Engine | Python FastAPI + OR-Tools | **Ready** (VRP Solver implemented) |
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
| `Microsoft.AspNetCore.Mvc.NewtonsoftJson` | JSON handling for Spatial types | Added |

---

## 3. System Architecture

```text
Flutter Rider App
    |
    | SignalR / WebSocket
    v
.NET Backend API  <---- REST ---->  Python AI Service (Port 8000)
    |
    | EF Core + NetTopologySuite
    v
PostgreSQL + PostGIS
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
│   │   │   └── RiderDto.cs
│   │   ├── Rider.cs
│   │   └── Order.cs
│   └── Setup/
│       ├── ServiceSetup.cs           ← DI, Filters, Mapster, Validation
│       └── ApplicationSetup.cs       ← Middleware pipeline
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
- [x] Add User/Auth domain model and migration.
- [x] Add AuthController (Login/Logout).
- [x] Implement `TrackingHub` logic for real-time broadcast.
- [ ] Add `OrdersController` (Business logic for multi-drop).

### Frontend / Mobile
- [ ] Run `npm run generate:api` to sync DTOs.
- [ ] Build Angular Dashboard Map View (Leaflet or Google Maps).
- [x] Initialize **Flutter Rider App** project.
- [ ] Implement Phase 2: GPS & Background Service in Rider App.

### AI Engine
- [x] Implement FastAPI service.
- [x] Add OR-Tools VRP solver.
- [ ] Integrate with BackendApi via `AiService` HttpClient.

### Integration
- [ ] End-to-end flow: create order → optimize route → assign rider → broadcast tracking.
- [ ] Docker Compose smoke test.
- [ ] Add CI build workflow.

---

## 11. URLs & Ports

| Service | Local URL | Docker URL |
|---|---|---|
| Frontend | `http://localhost:4200` | `http://localhost` |
| Backend Swagger | check `launchSettings.json` | `http://localhost:5000/swagger` |
| AI Docs | `http://localhost:8000/docs` | `http://localhost:8000/docs` |
| AI Health | `http://localhost:8000/health` | `http://localhost:8000/health` |
| Database | `localhost:5432` | `db:5432` |

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
