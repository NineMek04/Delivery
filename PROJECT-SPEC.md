# Project Specification
## AI-Optimized Smart Delivery Routing System
### ระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์

> **Version:** 0.2.0 (Backend Foundation + Security Baseline)  
> **Last Updated:** 2026-05-13  
> **Team Lead:** นนท์ธรัตน์ ทาลา

---

## 1. Overview

ระบบนี้เป็นแพลตฟอร์มจำลองการจัดส่งสินค้า/อาหารที่ใช้ AI ช่วยคำนวณเส้นทางที่เหมาะสมสำหรับงานแบบ multi-drop หรือ batched orders โดยพัฒนาเป็น microservices บน Docker เพื่อให้ setup และทดสอบแต่ละ service ได้ง่าย

### Problems To Solve

- เส้นทางจัดส่งที่ไม่ได้ optimize ทำให้เสียเวลาและเชื้อเพลิง
- งานจัดส่งหลายจุดต้องการการจัดลำดับ waypoint ที่เหมาะสม
- ต้องการ real-time GPS tracking ระหว่าง Rider mobile app และ Admin dashboard
- ต้องการ prototype ที่ใช้ smartphone เป็น GPS sensor แทน hardware เฉพาะทาง

### Core Features

| Feature | Description | Status |
|---|---|---|
| AI Route Optimization | คำนวณเส้นทางด้วย VRP algorithm ผ่าน Google OR-Tools | Planned |
| Real-time GPS Tracking | ส่งตำแหน่ง Rider ผ่าน SignalR/WebSocket | SignalR registered, Hub pending |
| Admin Dashboard | Dashboard สำหรับดู order/rider/map แบบ real-time | Angular template |
| Rider Mobile App | Flutter app สำหรับส่ง GPS และรับเส้นทาง | Not created |
| Dockerized Services | รันระบบด้วย Docker Compose | Partial |
| Backend Security | JWT, role policy, rate limit, security headers | Foundation ready |

---

## 2. Tech Stack

| Layer | Technology | Version / Notes |
|---|---|---|
| Backend API | ASP.NET Core | .NET 8 |
| Backend ORM | EF Core + Npgsql + NetTopologySuite | 8.0.11 |
| Backend API Docs | Swagger / Swashbuckle | 6.6.2 |
| Backend Security | JWT Bearer Authentication | Microsoft.AspNetCore.Authentication.JwtBearer 8.0.11 |
| Real-time | SignalR | ASP.NET Core built-in, registered in DI |
| Database | PostgreSQL + PostGIS | `postgis/postgis:15-3.3` |
| AI Engine | Python FastAPI + OR-Tools | Planned / scaffold pending verification |
| Frontend | Angular | 19.2.0, standalone components |
| Mobile | Flutter | Not created |
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

---

## 3. System Architecture

```text
Flutter Rider App
    |
    | SignalR / WebSocket
    v
.NET Backend API  <---- REST ---->  Python AI Service
    |
    | EF Core + NetTopologySuite
    v
PostgreSQL + PostGIS
    ^
    |
Angular Admin Dashboard
```

### Docker Services

| Service | Container | Build / Image | Port | Status |
|---|---|---|---|---|
| `db` | `delivery-db` | `postgis/postgis:15-3.3` | `5432:5432` | Ready |
| `backend` | `delivery-backend` | `./BackendApi/Dockerfile` | `5000:80` | Backend Dockerfile exists |
| `ai-service` | `delivery-ai` | `./ai-engine/Dockerfile` | `8000:8000` | Needs verification / implementation |
| `frontend` | `delivery-frontend` | `./admin-dashboard/Dockerfile` | `80:80` | Needs verification / implementation |

### Data Flow

1. Rider app sends GPS location to Backend via SignalR.
2. Backend stores GPS/order data in PostgreSQL/PostGIS.
3. Backend sends order and rider data to AI service for VRP optimization.
4. AI service returns waypoint sequence.
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
│   ├── BackendApi.csproj
│   ├── Program.cs
│   ├── Dockerfile
│   ├── .env.example
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── Core/
│   │   └── DeliveryControllerBase.cs
│   ├── Data/
│   │   └── ApplicationDbContext.cs
│   ├── Models/
│   │   ├── Rider.cs
│   │   └── Order.cs
│   ├── Migrations/
│   ├── Security/
│   │   ├── AuthConstants.cs
│   │   ├── ITokenService.cs
│   │   ├── JwtTokenService.cs
│   │   ├── LoginAttemptService.cs
│   │   └── TokenSubject.cs
│   └── Setup/
│       ├── ApplicationSetup.cs
│       ├── DotEnvLoader.cs
│       ├── SecurityConfiguration.cs
│       ├── SecurityHeadersMiddleware.cs
│       └── ServiceSetup.cs
├── ai-engine/
├── admin-dashboard/
└── .github/workflows/
```

---

## 5. Backend API Specification

### Current Backend Foundation

| Area | Current Implementation |
|---|---|
| Hosting | .NET 8 minimal hosting |
| Program setup | `Program.cs` delegates to setup extensions |
| DI setup | `Setup/ServiceSetup.cs` |
| Middleware pipeline | `Setup/ApplicationSetup.cs` |
| DbContext | `Data/ApplicationDbContext.cs` |
| Domain models | `Models/Rider.cs`, `Models/Order.cs` |
| Base controller | `Core/DeliveryControllerBase.cs` |
| Swagger | Enabled in development |
| SignalR | `AddSignalR()` registered, Hub not yet implemented |
| AI HTTP client | Named HttpClient: `AiService` |

### Backend Data Handler Core

`DBHandlerCore` is an EF Core-based data handler inspired by the `DBHandlerCore` pattern from the `online3-casemanagement-api` and `online3-ds-api` reference projects. It keeps the controller-facing workflow familiar while avoiding DevExpress XPO dependencies.

| Capability | Method / Property |
|---|---|
| Query entities | `DB.GetQuery<TEntity>()` |
| Query list | `DB.GetObjectListAsync<TEntity>()` |
| Find by key | `DB.GetObjectByKeyAsync<TEntity>(key)` |
| Create instance | `DB.CreateEntity<TEntity>()` |
| Insert | `DB.InsertObject(entity)` |
| Update | `DB.UpdateObject(entity)` |
| Soft/hard delete | `DB.DeleteObjectAsync<TEntity>(key, softDelete: true)` |
| Direct SQL delete by PK | `DB.DirectDeleteAsync<TEntity>(key)` |
| Commit unit of work | `DB.CommitChangesAsync()` |
| Clear tracked changes | `DB.ClearAllChanges()` |
| Begin transaction | `DB.BeginTransactionAsync()` |
| Execute SQL | `DB.ExecuteSqlAsync(sql, parameters)` |

`DeliveryControllerBase` exposes `protected DBHandlerCore DB`, so future controllers can use this pattern:

```csharp
var riders = await DB.GetObjectListAsync<Rider>();
DB.InsertObject(order);
await DB.CommitChangesAsync();
```

`ConditionContext` applies optional convention-based filters only when the target entity has matching properties:

- `RecordStatus` / `RECORD_STATUS` equals `A`
- `DelFlag` / `DEL_FLAG` equals `N`

### Planned API Endpoints

| Method | Endpoint | Description | Status |
|---|---|---|---|
| GET | `/swagger` | Swagger UI | Ready in development |
| POST | `/api/auth/login` | Login and issue JWT/session cookie | Planned |
| POST | `/api/auth/logout` | Clear auth cookie | Planned |
| GET | `/api/auth/session` | Read current session | Planned |
| POST | `/api/orders` | Create order | Planned |
| GET | `/api/orders` | List orders | Planned |
| GET | `/api/riders` | List riders | Planned |
| GET | `/api/riders/available` | List available riders, optionally by location | Planned |
| PUT | `/api/riders/{id}/location` | Update rider GPS location | Planned |
| POST | `/api/routes/optimize` | Send batched orders to AI service | Planned |
| WebSocket | `/hubs/tracking` | SignalR hub for real-time GPS tracking | Planned |

---

## 6. Backend Security Baseline

Security foundation is implemented before adding full AuthController/User domain logic.

### Implemented Components

| File | Purpose |
|---|---|
| `Setup/SecurityConfiguration.cs` | Registers JWT auth, authorization policies, rate limiting, token services |
| `Setup/SecurityHeadersMiddleware.cs` | Adds baseline security headers |
| `Setup/DotEnvLoader.cs` | Loads `.env` values and maps `__` to configuration paths |
| `Security/AuthConstants.cs` | Shared auth constants, roles, policy names, cookie name |
| `Security/JwtTokenService.cs` | Central JWT access token creation |
| `Security/LoginAttemptService.cs` | In-memory login failure tracking and lockout |
| `Security/TokenSubject.cs` | Token subject DTO |
| `Security/ITokenService.cs` | Token service abstraction |

### Security Rules

- `Jwt:Key` must be provided through user secrets, environment variables, or `.env`.
- `Jwt:Key` must be at least 32 characters.
- Placeholder values such as `__SET_VIA_USER_SECRETS_OR_ENV__` and `replace-with...` are rejected at startup.
- JWT is accepted from `Authorization: Bearer <token>`.
- JWT can also be read from an HttpOnly cookie named `access_token`.
- Kestrel server header is disabled.
- Security headers are enabled by default through `SecurityHeaders:Enabled`.
- CORS reads allowed origins from `Cors:AllowedOrigins`.
- Avoid allow-all CORS with credentials in production.
- Future auth endpoints should use the rate limit policy named `auth`.

### Role Policies

| Policy | Allowed Roles |
|---|---|
| `AdminOnly` | `Admin` |
| `Operations` | `Admin`, `Dispatcher` |
| `Rider` | `Admin`, `Rider` |

### Local JWT Configuration

Create `BackendApi/.env` from `BackendApi/.env.example`:

```env
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://localhost:5000
ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=delivery_db;Username=postgres;Password=replace-with-local-password"
AI_SERVICE_URL=http://localhost:8000
Jwt__Key="delivery-dev-jwt-secret-key-please-change-2026"
Jwt__Issuer=DeliveryBackendApi
Jwt__Audience=DeliveryClients
Cors__AllowedOrigins=http://localhost:4200,http://localhost:80
Authentication__SessionLifetimeHours=24
Authentication__RequireSecureCookie=false
Authentication__CookieSameSite=Lax
```

Alternative with user secrets:

```powershell
cd BackendApi
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "delivery-dev-jwt-secret-key-please-change-2026"
dotnet user-secrets set "Jwt:Issuer" "DeliveryBackendApi"
dotnet user-secrets set "Jwt:Audience" "DeliveryClients"
```

---

## 7. Database Specification

### Current EF Core Models

| Entity | Purpose | Spatial Fields |
|---|---|---|
| `Rider` | Delivery driver/rider profile and current status | `CurrentLocation` as `geometry(Point, 4326)` |
| `Order` | Delivery order with pickup/dropoff points | `PickupLocation`, `DropoffLocation` as `geometry(Point, 4326)` |

### Current DbContext

- `ApplicationDbContext`
- `DbSet<Rider> Riders`
- `DbSet<Order> Orders`
- PostGIS extension is enabled via EF model configuration

### Spatial Data Rules

- Use SRID 4326 / WGS84 for all GPS coordinates.
- Use `geometry(Point, 4326)` for location points.
- Add GiST indexes for production geospatial queries.
- Prefer EF Core + NetTopologySuite for geometry mapping instead of raw coordinate strings.

### Planned Schema Additions

- Users / roles / auth tables
- Customers
- Shops
- Routes
- Route waypoints
- Delivery assignment history
- Activity or audit logs

---

## 8. Environment Setup

### Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 8.x |
| Docker Desktop | 4.x or newer |
| Node.js | 20 LTS or compatible with Angular 19 |
| Python | 3.11+ |
| Git | 2.x |

### Run Backend Locally

```powershell
cd BackendApi
copy .env.example .env
# edit .env and set Jwt__Key + database password
dotnet run
```

Swagger:

```text
http://localhost:<port>/swagger
```

Check the actual development port in `BackendApi/Properties/launchSettings.json`.

### Run Backend Build Verification

```powershell
dotnet build BackendApi\BackendApi.csproj
```

Expected result:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

### Docker Compose

```powershell
docker-compose up --build
```

Important: replace placeholder values in `docker-compose.yml` before using Docker for realistic testing, especially:

- `ConnectionStrings__DefaultConnection`
- `Jwt__Key`

---

## 9. Development Standards

| Area | Standard |
|---|---|
| Backend | .NET 8, Repository Pattern, Dependency Injection |
| Real-time | SignalR for GPS updates |
| Database | PostgreSQL/PostGIS, SRID 4326, GiST indexes |
| AI Engine | FastAPI + Google OR-Tools |
| Frontend | Angular standalone components |
| Mobile | Flutter |
| Security | JWT Bearer, role policies, rate limiting, security headers |
| Secrets | No real secrets in git; use user secrets/env/.env ignored locally |
| CORS | Configure allowed origins explicitly |
| Containers | Keep services consistent with `docker-compose.yml` |

---

## 10. Current Status

### Phase Status

| Phase | Status | Notes |
|---|---|---|
| Phase 1: Infrastructure | In progress | Docker Compose exists; backend foundation improved |
| Phase 2: Core Backend | Starting | EF/PostGIS foundation and security baseline ready |
| Phase 3: AI + Frontend | Pending | AI/front dashboard work remains |
| Phase 4: Integration | Pending | End-to-end workflows not yet implemented |

### Component Status

| Component | Status | Notes |
|---|---|---|
| Docker Compose | Partial | 4 services defined |
| PostGIS Database | Ready | Uses PostGIS image and SRID 4326 standard |
| Backend Dockerfile | Ready | Multi-stage .NET 8 Dockerfile exists |
| Backend API | Foundation Ready | EF Core/PostGIS, setup extensions, JWT baseline |
| Data Handler Core | Ready | EF Core-based `DBHandlerCore` and `ConditionContext` registered in DI |
| Backend AuthController | Pending | Security services ready, endpoints not implemented |
| SignalR Hub | Pending | SignalR registered, hub not yet implemented |
| Repository Pattern | Pending | `DBHandlerCore` foundation exists; domain-specific repositories still pending |
| AI Engine | Pending / scaffold | Needs implementation and Dockerfile verification |
| Angular Dashboard | Template | Angular 19 template |
| Flutter App | Not Created | Rider app not initialized |
| CI/CD | Not Created | `.github/workflows` empty |

---

## 11. Next Tasks

### Backend

- [ ] Add User/Auth domain model and migration.
- [ ] Add AuthController for login/logout/session.
- [ ] Use `ITokenService` for issuing JWT access tokens.
- [ ] Apply `LoginAttemptService` and `auth` rate limit policy to login/register endpoints.
- [ ] Add repositories for Riders and Orders on top of `DBHandlerCore`.
- [ ] Add OrdersController and RidersController.
- [ ] Add TrackingHub at `/hubs/tracking`.
- [ ] Add GiST indexes for spatial fields in migrations.

### AI Engine

- [ ] Implement FastAPI service.
- [ ] Add OR-Tools VRP solver.
- [ ] Define request/response DTO contract with BackendApi.
- [ ] Verify `ai-engine/Dockerfile`.

### Frontend / Mobile

- [ ] Build Angular dashboard map view.
- [ ] Connect Angular to SignalR.
- [ ] Initialize Flutter Rider app.
- [ ] Send live GPS updates from Rider app.

### Integration

- [ ] End-to-end flow: create order → optimize route → assign rider → broadcast tracking.
- [ ] Docker Compose smoke test.
- [ ] Add CI build workflow.

---

## 12. URLs & Ports

| Service | Local URL | Docker URL |
|---|---|---|
| Frontend | `http://localhost:4200` | `http://localhost` |
| Backend Swagger | check `launchSettings.json` | `http://localhost:5000/swagger` |
| AI Docs | `http://localhost:8000/docs` | `http://localhost:8000/docs` |
| AI Health | `http://localhost:8000/health` | `http://localhost:8000/health` |
| Database | `localhost:5432` | `db:5432` |

---

## 13. Environment Notes

- Development machine may be ASUS ROG; avoid GPU-dependent implementation unless required.
- For npm work, check `.npmrc` and VPN/private registry status first.
- `Jwt__Key` must be set before running BackendApi.
- `.env` is for local development only and should not contain production secrets in git.
- Database password in sample config must be changed before non-local use.

---

## 14. Related Documents

- [AI-BLUEPRINT.md](./AI-BLUEPRINT.md)
- [AI-CHANGELOG.md](./AI-CHANGELOG.md)
- [.cursorrules](./.cursorrules)
- [docker-compose.yml](./docker-compose.yml)
