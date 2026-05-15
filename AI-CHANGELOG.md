# AI-CHANGELOG: Context Ledger & Sync

## [Project Status: In Development]

- **Current Milestone:** Phase 2 - Real-time Dispatch Orchestration
- **Shared Registry:** Azure Artifacts (BetimesShare)

---

## [Log Date: 2026-05-12] | โดย: AI Agent

### Component: Environment Setup
- **Action:** แก้ไขปัญหาการเชื่อมต่อ Private Registry (E401) ผ่านไฟล์ `.npmrc` และ `vsts-npm-auth`
- **Status:** สำเร็จ สามารถ `npm install` ได้แล้ว

### Component: Database (PostGIS)
- **Action:** สร้างฐานข้อมูลและ Extension PostGIS พร้อมกำหนดมาตรฐานพิกัด SRID 4326
- **Status:** พร้อมใช้งาน เชื่อมต่อผ่าน DBeaver สำเร็จ

---

## [Log Date: 2026-05-13] | โดย: AI Agent

### Component: BackendApi Foundation
- **Action:** เพิ่ม `Core/DeliveryControllerBase.cs`, `Setup/ServiceSetup.cs`, `Setup/ApplicationSetup.cs` เพื่อวางโครงสร้าง DI, EF Core, Swagger, CORS, และ SignalR
- **Action:** เพิ่ม JWT Bearer authentication พร้อม role policies (`AdminOnly`, `Operations`, `Rider`) และ rate limiting

### Component: BackendApi Data Handler Core
- **Action:** เพิ่ม `DBHandlerCore.cs` และ `ConditionContext.cs` สำหรับการจัดการข้อมูลแบบ EF Core-based handler

### Component: AI Engine Setup
- **Action:** ยืนยันความพร้อมของ `ai-engine` (FastAPI + OR-Tools VRP Solver) พร้อม Dockerfile

### Component: Frontend Admin Dashboard
- **Action:** วางโครงสร้าง Fluent API (`DeliveryHttpRequest`), Base Services, และ Interceptors (Auth/Error) สำหรับ Angular 19

---

## [Log Date: 2026-05-14] | By: AI Agent

### Component: Documentation / Spec
- **Action:** อัปเดต `PROJECT-SPEC.md` และ `AI-BLUEPRINT.md` เป็นเวอร์ชัน 0.4.0
- **Action:** ซิงโครไนซ์สถานะงานจริง: AI Engine (Ready), Rider App (Initialized)

### Component: Flutter Rider App Foundation
- **Action:** สร้างโครงสร้างพื้นฐานสำหรับ `rider_app` (Dio, SignalR, Riverpod, Location Service, Models) อยู่ในสถานะ **Foundation Ready**

### Component: BackendApi — Authentication System
- **Action:** สร้าง `Models/User.cs`, `Security/PasswordHasher.cs`, และ `Controllers/AuthController.cs`
- **Action:** รองรับ Login (JWT + Cookie), Register (auto-create Rider), Logout, และ Session endpoints

### Component: BackendApi — TrackingHub (SignalR Real-time GPS)
- **Action:** สร้าง `Hubs/TrackingHub.cs` รองรับ `UpdateLocation` (พร้อม GPS Drift protection) และ `UpdateStatus`
- **Action:** Broadcast พิกัด Rider ไปยังกลุ่ม Admin/Dispatcher แบบ Real-time

### Component: Database & Migration
- **Action:** รัน `dotnet ef database update` สำเร็จทั้ง `InitialCreate` และ `AddUserEntity`
- **Fix:** แก้ไข Connection String ใน `appsettings.json` และรีเซ็ต password ใน Docker container ให้ตรงกัน (`postgres` / `Admin@Ts2x04_`)

### Verification
- **Backend Build:** `dotnet build BackendApi\BackendApi.csproj` → 0 errors
- **Database Status:** ตาราง `Orders`, `Riders`, `Users` ถูกสร้างและพร้อมใช้งานใน PostGIS

---

## [Log Date: 2026-05-14] | By: AI Agent

### Component: Architecture & AI Configuration
- **Action:** อัปเดตกฎในระบบ AI (Cursorrules & AGENTS) ให้ครอบคลุมการใช้ Base มาตรฐานในทุกส่วนของโปรเจกต์ (Backend, Frontend, Mobile, AI)
- **Action:** จัดระเบียบโครงสร้าง Service ย้ายออกจาก `Controllers/Services/` ไปยัง `BackendApi/Services/` ตามหลัก DI และ Separation of Concerns
- **Standard Enforcement:**
  - **Backend:** บังคับใช้ `CrudControllerBase`, `DeliveryControllerBase` และ `DBHandlerCore` สำหรับจัดการ Database
  - **Frontend:** บังคับใช้ `BaseApiService<T>` และ `DeliveryHttpRequest` (Angular)
  - **Mobile:** บังคับใช้โครงสร้าง Foundation ของ Flutter ที่วางไว้
- **Impact:** ช่วยให้ AI ทำงานได้ตรงตามโครงสร้างและรักษาความสะอาดของ Codebase ในระยะยาว

---

## [Log Date: 2026-05-14 (2)] | By: AI Agent

### Component: BackendApi — Refresh Token System
- **Action:** เพิ่ม `RefreshToken` (SHA-256 hash) และ `RefreshTokenExpiresAt` ใน `Models/User.cs`
- **Action:** เพิ่ม `RefreshTokenRequest` DTO และ `RefreshToken` field ใน `AuthResponse` (`Models/DTOs/AuthDtos.cs`)
- **Action:** เพิ่ม `RefreshTokenAsync()` ใน `IAuthService` + implement Token Rotation ใน `Services/Auth/AuthService.cs`
- **Action:** เพิ่ม `POST /api/v1/auth/refresh` endpoint ใน `Controllers/Business/AuthController.cs`
- **Security:** Refresh Token ถูก hash ด้วย SHA-256 ก่อนเก็บลง DB, Token Rotation ทุกครั้งที่ refresh (อันเก่าใช้ไม่ได้)
- **Config:** `Authentication:RefreshTokenLifetimeDays` (default: 7 วัน) ใน `appsettings.json`

### Component: Flutter Rider App — AuthService Overhaul
- **Action:** เพิ่ม `refreshAccessToken()` — เรียก `POST /auth/refresh` เพื่อขอ Access Token ใหม่อัตโนมัติ
- **Action:** เพิ่ม Token Clocking (`Timer.periodic` 30s) พร้อม proactive refresh เมื่อ token เหลือ < 2 นาที (เทียบ Angular `startTokenClocking()`)
- **Action:** เพิ่ม `setTokens()`, `setUserData()`, `getUserData()` สำหรับจัดการข้อมูลผู้ใช้ใน SecureStorage
- **Action:** เพิ่ม malformed token handling — `try-catch` รอบ `JwtDecoder.isExpired()` เพื่อจัดการ token ที่เสียหาย
- **Action:** เพิ่ม concurrent refresh protection (`_isRefreshing` flag) ป้องกัน race condition

### Component: Flutter Rider App — ErrorInterceptor Auto-Retry
- **Action:** อัปเดต `ErrorInterceptor` ใน `api_interceptors.dart` — auto refresh + retry original request เมื่อเจอ 401
- **Action:** เพิ่ม loop protection (ไม่ retry สำหรับ `/auth/refresh` และ `/auth/login`)
- **Action:** อัปเดต `delivery_api_client.dart` — ส่ง `Ref` ให้ `ErrorInterceptor`

### Verification
- **Backend Build:** `dotnet build` → 0 errors, 0 warnings
- **Flutter:** ⚠️ ต้อง analyze ใน IDE (Flutter SDK ไม่อยู่ใน PowerShell PATH)

### Pending
- ⚠️ **ต้องรัน EF Core migration:** `dotnet ef migrations add AddRefreshTokenFields` + `dotnet ef database update`
- ⚠️ **ต้องรัน build_runner:** `flutter pub run build_runner build` เพื่อ generate `.g.dart` files

---

## [Log Date: 2026-05-14 (3)] | By: AI Agent

### Component: Infrastructure — Redis Integration
- **Action:** เพิ่ม Redis 7 (Alpine) เข้าสู่ `docker-compose.yml` สำหรับเก็บ Hot Data (GPS, Presence, Locks)
- **Action:** ติดตั้ง `StackExchange.Redis` ใน Backend และสร้าง `IConnectionMultiplexer` ใน DI
- **Action:** พัฒนา `GpsSyncBuffer` และ `GpsSyncWorker` — ระบบรับ GPS ความถี่สูงลง Memory และ Batch Flush ลง PostGIS ทุก 30 วินาที

### Component: BackendApi — Dispatch Orchestrator (The Heart)
- **Action:** สร้าง `Services/Dispatch/DispatchService.cs` — ควบคุม Dispatch Lifecycle (30s timeout, Rider offering, offer versioning)
- **Action:** สร้าง `Services/Dispatch/StateMachineService.cs` — จัดการสถานะ `OrderState` และ `RiderState` แบบเข้มงวด
- **Action:** สร้าง `Infrastructure/Redis/RedisLockService.cs` — ระบบ Distributed Locking ด้วย Redis `SETNX` + Lua Script
- **Action:** สร้าง `Infrastructure/Redis/RiderPresenceService.cs` — ระบบ Geolocation บน Redis สำหรับค้นหา Nearby Idle Riders

### Component: AI Engine — Dispatch Scoring (The Brain)
- **Action:** สร้าง `app/core/geo_utils.py` — คำนวณ Haversine Distance และ Bearing (ทิศทางการเคลื่อนที่)
- **Action:** สร้าง `app/core/scoring.py` — ระบบ Ranking ไรเดอร์ (Heuristic Phase A) พิจารณาจาก ระยะทาง + ภาระงาน + ทิศทาง
- **Action:** อัปเดต `main.py` — เพิ่ม endpoint `POST /api/v1/dispatch/rank` เชื่อมต่อกับ .NET Backend

### Component: BackendApi — Pipeline & Background Workers
- **Action:** สร้าง `DispatchTimeoutWorker` — ระบบ Janitor ตรวจสอบ Offer หมดเวลาและสั่ง Re-dispatch อัตโนมัติ
- **Action:** สร้าง `HeartbeatMonitor` — ระบบตรวจสอบ Rider Offline (Detection of ghost riders)
- **Action:** ปรับปรุง `TrackingHub` — เชื่อมต่อกับ Redis Presence และ Dispatch logic พร้อมเพิ่ม GPS Sanity Check (Max Drift 5km)
- **Fix:** ย้าย `app.UseCors()` ไปไว้บนสุดของ Middleware Pipeline และเพิ่ม Default Ports (3000, 5173) เพื่อแก้ปัญหา CORS ในฝั่ง Dashboard

### Verification
- **Backend Build:** `dotnet build` → 0 errors, 5 warnings (Pending awaits in PresenceService)
- **AI Engine:** รันสำเร็จพร้อม endpoint ใหม่

### Pending
- ⚠️ **Database Migration:** ต้องรัน `dotnet ef migrations add Phase2DispatchStateAndLocationHistory` และ `dotnet ef database update`
- ⚠️ **Integration Test:** ทดสอบ End-to-end Dispatch flow เมื่อ Dashboard และ Rider App พร้อม

---

## [Log Date: 2026-05-14 (4)] | By: AI Agent

### Component: Infrastructure — Backend Stabilization & Docker Setup
- **Action:** สร้างไฟล์ `.env` สำหรับ Backend เพื่อกำหนดโหมด `Development` และจัดการ Connection String นอกโค้ด
- **Action:** แก้ไขปัญหาฐานข้อมูล — ปรับรหัสผ่านใน Docker ให้ตรงกับแอป (`Admin@Ts2x04_`) และรัน Migrations สำเร็จ
- **Action:** แก้ไข CORS Policy — เพิ่มการอนุญาต `http://localhost` ใน `docker-compose.yml` เพื่อรองรับการรัน Dashboard ผ่าน Docker (Port 80)
- **Action:** Re-enable Background Workers — เปิดการทำงานของ `DispatchTimeoutWorker`, `HeartbeatMonitor`, และ `GpsSyncWorker` หลังฐานข้อมูลพร้อมใช้งาน

### Component: Documentation & Team Readiness
- **Action:** อัปเดต `PROJECT-SPEC.md` เพิ่มส่วน **Setup Guide** และ **Port Mapping Table** สำหรับสมาชิกในทีม
- **Status:** ระบบพร้อมรัน 100% ทั้งแบบ Local (Visual Studio/npm) และแบบ Full Docker Stack

### Verification
- **Backend:** `http://localhost:5000/swagger` เข้าใช้งานได้ปกติทั้งในและนอก Docker
- **Frontend Integration:** ปัญหา CORS (ERR_FAILED) ถูกแก้ไขแล้ว หน้า Login สามารถสื่อสารกับ API ได้
- **Infrastructure:** `delivery-db` และ `delivery-redis` รันในสถานะ Healthy และรับการเชื่อมต่อได้

---

## [Log Date: 2026-05-15] | By: AI Agent

### Component: BackendApi — AI Integration & Dispatch Flow
- **Action:** สร้าง `AiService` (Typed HttpClient) สำหรับเชื่อมต่อ .NET Backend กับ Python AI Engine อย่างสมบูรณ์
- **Action:** สร้าง `OrdersController` (Business Logic) สำหรับรองรับการจัดการและตั้งเวลาหา Rider อัตโนมัติ 
- **Action:** สร้าง E2E Node.js Test Script เพื่อจำลองการทำงานจริง (Admin สร้างงาน -> AI จัดเรียงและ Dispatch -> SignalR ส่ง Offer ไปยัง Rider) และผ่านสำเร็จ 100%

### Component: BackendApi — Pricing & Spatial Fixes
- **Action:** แก้ไขปัญหา `PostgresException: Geometry has Z dimension` โดยปรับ `MappingConfig.cs` (Mapster) และแก้ไปใช้ Manual Mapping เมื่อจัดการข้อมูล `Point`
- **Action:** สร้าง Business Logic สำหรับคำนวณราคาจัดส่งผ่าน `DistanceKm` และ `DeliveryFee` โดยใช้ระยะทาง Haversine Equation พื้นฐาน
- **Action:** รัน `dotnet ef migrations add AddPricingToOrder` และอัปเดต Database

### Component: BackendApi — Order Lifecycle Management
- **Action:** เพิ่ม endpoint `PATCH /api/v1/orders/{id}/status` รองรับการทำงานของ Rider
- **Action:** เพิ่ม endpoint `POST /api/v1/orders/{id}/cancel` ให้ผู้ควบคุมระบบกดยกเลิก
- **Action:** เพิ่ม endpoint `POST /api/v1/orders/{id}/dispatch` บังคับรัน Dispatch ซ้ำเมื่อไม่มี Rider ว่างรับงาน
- **Status:** นำไป Deploy ลง Docker สำเร็จ ระบบพร้อมรองรับ Front-end เต็มตัว
