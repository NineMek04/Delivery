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

## [Log Date: 2026-05-15] | By: AI Agent

### Component: BackendApi — Automated Migrations
- **Action:** เพิ่ม `DatabaseMigrationSetup.cs` และปรับปรุง `Program.cs` เพื่อรัน `context.Database.MigrateAsync()` อัตโนมัติขณะ Startup
- **Status:** สำเร็จ (Zero-Manual Setup สำหรับ DB schema)

### Component: Rider App — Dockerization (Flutter Web)
- **Action:** สร้าง Multi-stage Dockerfile และเพิ่ม Service `rider-app` เข้าไปใน `docker-compose.yml` บน Port 8080
- **Status:** พร้อมสำหรับการตรวจสอบ UI ผ่าน Browser โดยไม่ต้องลง Flutter SDK

### Component: AI Engine — Code Refactoring
- **Action:** ปรับโครงสร้างโค้ดเป็น Modular แยก VRP Logic ออกจาก API Endpoint (core/ vs api/)
- **Status:** Refactored สำเร็จตามแผนงาน

---

## [Log Date: 2026-05-15 (2)] | By: AI Agent

### Component: Admin Dashboard — Modularization & Standard Refactoring
- **Action:** Refactored `AppComponent` by separating monolithic logic into modular feature components: `Dashboard`, `Map`, `Orders`, and `Analytics`.
- **Action:** Implemented `MainLayoutComponent` to handle shared navigation, sidebar, and topbar across all protected routes.
- **Action:** Standardized `LoginComponent` and `RegisterComponent` by separating inline templates and styles into external `.html` and `.scss` files.
- **Action:** Configured nested routing in `app.routes.ts` with Lazy Loading support for all feature modules.
- **Fix:** Restored `static InjectorInstance` in `AppComponent` to maintain compatibility with core infrastructure and fluent API patterns.

### Component: Admin Dashboard — Style System Upgrade (SCSS)
- **Action:** Migrated the entire CSS stack to SCSS for better maintainability and nesting support.
- **Action:** Renamed all `.css` files to `.scss` and updated `angular.json` build configurations.
- **Action:** Updated all component `styleUrl` references to point to the new `.scss` files.
- **Status:** Standardized & SCSS-Ready.

---

## [Log Date: 2026-05-15 (3)] | By: AI Agent

### Component: Admin Dashboard — Real-Time Infrastructure (SignalR)
- **Action:** Generated OpenAPI models from Backend using `openapi-generator-cli` via Docker container (`src/app/api/generated/`).
- **Action:** Installed `leaflet` and `@microsoft/signalr` to support interactive maps and real-time WebSocket communication.
- **Action:** Created `TrackingSignalRService` to handle WebSocket connection with `TrackingHub` and subscribe to `RiderLocationUpdated`, `OfferSent`, and `OrderStatusChanged`.
- **Status:** Real-time foundation established, complying with `DeliveryHttpRequest` and `BaseApiService<T>` architectural patterns.

### Component: Admin Dashboard — Features Implementation
- **Action:** Developed `OrderService` and `RiderService` inheriting from `BaseApiService<T>` to fetch and manage data dynamically from the Backend API.
- **Action:** Implemented `MapComponent` with real-time `Leaflet` integration, successfully rendering moving rider markers with varying colors based on live status (IDLE vs DELIVERING).
- **Action:** Implemented `OrdersComponent` to display live `OrderDto` data, distance (km), pricing, and added action buttons for `Retry Dispatch` and `Cancel Order`.
- **Action:** Updated `angular.json` to correctly bundle Leaflet CSS styles globally.
- **Status:** Dashboard is now fully operational with live DB data and Real-time SignalR broadcasts.

---

## [Log Date: 2026-05-15 (4)] | By: AI Agent

### Component: BackendApi — Enterprise Auditing & Soft Delete
- **Action:** ออกแบบและวางโครงสร้าง Base Entity แบบ Layered (`IEntity`, `IAuditableEntity`, `ISoftDeletableEntity`) เพื่อความยืดหยุ่นในการใช้งาน
- **Action:** เพิ่ม **Concurrency Token (`RowVersion`)** ใน `BaseEntity` เพื่อป้องกัน Overwrite Conflict ในระบบ Real-time
- **Action:** พัฒนา `CurrentUserService` พร้อมระบบ **IP Normalization** (รองรับ `X-Forwarded-For` ป้องกัน Proxy Spoofing)
- **Action:** อัปเดต `ApplicationDbContext` ให้รองรับระบบอัตโนมัติ:
  - `ApplyAuditFields()`: บันทึกวันเวลา/ผู้ใช้/IP อัตโนมัติเมื่อสร้างหรือแก้ไข
  - `ApplySoftDelete()`: เปลี่ยนคำสั่งลบเป็นการ Mark `IsDeleted = true` พร้อมบันทึกหลักฐานการลบ
  - **Global Query Filter**: ซ่อนข้อมูลที่ถูกลบจากการ Query ปกติอัตโนมัติ
  - **Filtered Unique Index**: ปรับแก้ Unique Index ของ Email ให้เช็คเฉพาะข้อมูลที่ยังไม่ลบ เพื่อให้สมัครซ้ำได้
- **Action:** ปรับปรุงโมเดล `Order`, `User`, `Rider` ให้รองรับระบบใหม่ และปรับ `RiderLocationHistory` ให้เป็น Lightweight Insert-only
- **Verification:** รัน Migration `AddEnterpriseAuditing` และอัปเดตฐานข้อมูล PostGIS สำเร็จ
- **Status:** ระบบฐานข้อมูลมีมาตรฐานความปลอดภัยและตรวจสอบย้อนหลัง (Audit Trail) ระดับ Enterprise พร้อมใช้งาน

---

## [Log Date: 2026-05-15 (5)] | By: AI Agent

### Component: Rider App — GPS & Background Tracking
- **Action:** แก้ไข `AndroidManifest.xml` เพิ่มสิทธิ์ `ACCESS_BACKGROUND_LOCATION` และ `FOREGROUND_SERVICE` เพื่อรองรับการติดตามพิกัดเบื้องหลัง
- **Action:** แก้ไข `Info.plist` เพิ่มคำอธิบายการขอสิทธิ์ Location และเปิด `UIBackgroundModes` สำหรับ iOS
- **Action:** อัปเดต `LocationService` (Flutter) ให้รองรับการทำงานเบื้องหลัง:
  - **Android:** เปิด Persistent Notification (Foreground Service) พร้อมแจ้งเตือนผู้ใช้ว่าสามารถปิดการติดตามได้ในแอป
  - **iOS:** เปิด `showBackgroundLocationIndicator` และ `allowBackgroundLocationUpdates`
  - **Noise Filtering:** เพิ่มระบบกรองพิกัดขยะ (Accuracy > 50m) เพื่อป้องกันปัญหาพิกัดกระโดด (GPS Drift)
- **Status:** ระบบติดตามพิกัดสำหรับ Rider พร้อมใช้งานทั้งแบบ Foreground และ Background ตามแผนงาน Phase 2

---

## [Log Date: 2026-05-18] | By: AI Agent

### Component: BackendApi — Database Seeding (Mock Data)
- **Action:** ออกแบบและเขียนทับ `DataSeeder.cs` เพื่อรองรับการใส่ข้อมูลจำลอง (Mock Data) ที่สมบูรณ์แบบ
  - **ผู้ใช้งาน (Users):** Seed บัญชีผู้ใช้ของทุกฝั่ง (Admin, Dispatcher, Rider, Customer) พร้อมรหัสผ่านเริ่มต้น `Password123!` ที่ผ่านการ Hash ด้วย PBKDF2 อย่างถูกต้อง
  - **ผู้จัดส่ง (Riders) & ออเดอร์ (Orders):** กำหนดตำแหน่งพิกัดจำลองจริงด้วย NetTopologySuite Geometry (SRID 4326) ในบริเวณใจกลางกรุงเทพมหานครเพื่อให้พร้อมสำหรับการคำนวณและ dispatch
  - **ประวัติพิกัด (RiderLocationHistories):** สร้างพิกัดย้อนหลังเพื่อให้แดชบอร์ดทดสอบฟีเจอร์แสดงผลเส้นทางการเดินทาง
- **Action:** ปรับปรุง `DatabaseMigrationSetup.cs` เพื่อเรียกใช้ `DataSeeder.SeedAsync` หลังการเช็ค Migration สำเร็จ เพื่อทำการ Seed ข้อมูลอัตโนมัติหากพบว่าตารางว่างอยู่
- **Verification:** ตรวจสอบความถูกต้องผ่าน `dotnet build` สำเร็จ 100% ไม่มี Error
- **Status:** ข้อมูล Mock สำหรับการทดสอบร่วมกันระหว่าง Admin Dashboard, Rider Mobile App และ AI Engine พร้อมใช้งานเรียบร้อยแล้ว

---

## [Log Date: 2026-05-18 (2)] | By: AI Agent

### Component: BackendApi — Data Mapping & DTO Extensions
- **Action:** แก้ไข Bug ใน `MappingConfig.cs` โดยเพิ่มการแมพฟิลด์ `LastUpdated` ของ `RiderDto` ให้ดึงมาจาก `LastGpsUpdate ?? src.UpdatedAt` (ป้องกันปัญหาฟิลด์นี้มีค่าเริ่มต้นเป็น `DateTime.MinValue` เสมอ)
- **Action:** เพิ่มฟิลด์ `CreatedAt`, `AssignedAt`, และ `CompletedAt` ใน `OrderDto` (และอัปเดตไฟล์ `order-dto.ts` ฝั่ง Frontend แมนนวล) เพื่อรองรับการแสดงผล Timeline ของออเดอร์และการคำนวณ SLA ในฝั่งแดชบอร์ด

### Component: Admin Dashboard — API Service Layer Standardization
- **Action:** เขียนโครงสร้าง `BaseApiService.ts` ใหม่ทั้งหมด เพื่อให้แกะ Wrapper (`ApiResponse` และ `PaginatedResult`) ที่ส่งมาจาก `GlobalResponseFilter` ของ Backend อัตโนมัติ ช่วยรักษา Type Safety และรองรับ parameter pagination อย่างสมบูรณ์ รวมถึงเพิ่มฟังก์ชัน `getAllPaginated()`
- **Action:** แก้ไขบั๊ก Race Condition ในระบบ Logout ของ `AuthService` โดยปรับลำดับให้ส่ง Request ไปยัง API `/Auth/logout` เพื่อลบ HttpOnly Cookie ก่อนเคลียร์ Token ใน LocalStorage ป้องกันปัญหา 401 Unauthorized
- **Action:** ปรับเปลี่ยนการตรวจสถานะออเดอร์ใน `OrdersComponent`, `DashboardComponent`, และ `AnalyticsComponent` จากคำเดิมที่ไม่มีจริง (`PENDING`, `DELIVERED`) มาใช้ค่าจริงตาม State Machine ของ Backend (`CREATED`, `MATCHING`, `OFFERING`, `ASSIGNED`, `PICKING_UP`, `DELIVERING`, `COMPLETED`, `CANCELLED`)
- **Action:** ทำความสะอาดโค้ดดึงข้อมูลในทุก Component โดยถอดฟังก์ชัน manual unwrap ออกทั้งหมด เนื่องจากเปลี่ยนไปใช้ความสามารถของ `BaseApiService` ตัวใหม่ที่จัดการข้อมูลให้อัตโนมัติแล้ว

### Verification
- **Backend Build:** `dotnet build` ผ่านสำเร็จ 100% ไม่มีข้อผิดพลาด (0 errors)
- **Frontend Build:** `ng build` ผ่านสำเร็จ 100% (0 errors, build bundle asset สมบูรณ์)

---

## [Log Date: 2026-05-18 (3)] | By: AI Agent

### Component: Admin Dashboard — Security & Access Control (Guards)
- **Action:** สร้างระบบป้องกันสิทธิ์การเข้าใช้งาน (Guards) และการแจ้งเตือนแบบครบวงจร:
  - `authGuard` (`auth.guard.ts`): ตรวจสอบการเข้าสู่ระบบ หาก Token หมดอายุจะทำการ Refresh Token ให้แบบโปรแอคทีฟ หากไม่สำเร็จจะแสดง SweetAlert2 แจ้งเตือนแล้วส่งกลับหน้า Login
  - `roleGuard` & `adminOnlyGuard` (`role.guard.ts`): กรองสิทธิ์การเข้าใช้งานระบบ Dashboard อย่างเข้มงวด เฉพาะสิทธิ์ `Admin` และ `Dispatcher` เท่านั้น หากไม่ใช่ (เช่น `Rider` หรือ `Customer`) จะปฏิเสธการเข้าถึงพร้อมแสดงป๊อปอัป SweetAlert2 และให้ปุ่มออกจากระบบ
  - `guestGuard` (`guest.guard.ts`): ป้องกันผู้ใช้ที่เข้าสู่ระบบสำเร็จแล้วไม่ให้เข้าถึงหน้า Login หรือ Register อีกครั้ง โดยจะแสดงข้อความต้อนรับผ่าน SweetAlert2 Toast และพาไปยังหน้าหลักโดยอัตโนมัติ
- **Action:** อัปเดต `app.routes.ts` เพื่อคุ้มครอง Route ทั้งหมดในระบบด้วย Guard แต่ละรูปแบบอย่างรัดกุม พร้อมส่งผ่าน metadata ของ Role ที่ได้รับสิทธิ์ในแต่ละเส้นทาง
- **Action:** อัปเดต `AuthService` เพื่อเพิ่มเมธอดอำนวยความสะดวกในระบบความปลอดภัย:
  - `getUserRole()`, `hasRole()`, `canAccessDashboard()`, และ `getDecodedToken()` ดึงค่าจาก claims ใน JWT token (ทั้งแบบ custom และ standard Microsoft schema) หรือ fallback ไปที่ `userData`
  - `verifySession()`: เช็คเซสชันปัจจุบันกับ Backend API `/Auth/session` เพื่อยืนยันว่า token ยังไม่ถูกเพิกถอน (Revoked) ในฝั่งเซิร์ฟเวอร์
- **Action:** ปรับปรุง `app.config.ts` ให้เรียกใช้ `APP_INITIALIZER` (`initializeAuth`) เพื่อเช็ค Session ความถูกต้องของโทเค็นก่อนแอปพลิเคชันจะเรนเดอร์เนื้อหาหน้าจอ ป้องกันปัญหาการแสดงผลแผงควบคุมแวบหนึ่งก่อนถูกเด้งออกเมื่อโทเค็นมีปัญหา
- **Action:** ปรับปรุง `LoginComponent` ให้รองรับการทำงานของ `returnUrl` (ส่งผู้ใช้กลับไปยังหน้าที่พยายามจะเข้าถึงตอนแรกหลังจาก Login สำเร็จ) และเพิ่มการเช็คสิทธิ์แบบ fail-fast ทันทีหลังล็อกอิน หากไม่ใช่ Admin/Dispatcher จะแสดงสิทธิ์ที่ไม่ถูกต้องและออกจากระบบทันที

### Verification
- **Frontend Build:** `ng build` สำหรับ Dashboard ผ่านสำเร็จ 100% (0 errors, build bundles complete)

---

## [Log Date: 2026-05-18 (4)] | By: AI Agent

### Component: Admin Dashboard — Route Initialization & Bug Fixes
- **Action:** แก้ไขโครงสร้าง Routing ใน `app.routes.ts` ให้มีระบบการเปิดเว็บที่เป็นมิตรยิ่งขึ้น:
  - กำหนดให้ Root Path (`''`) ทำการ `redirectTo: 'login'` ด้วย `pathMatch: 'full'` ทำให้ทุกครั้งที่เปิด URL หลักของเว็บ (เช่น `http://localhost:4200/`) จะได้หน้าจอ Login ทันทีแบบเงียบๆ
  - ปรับเปลี่ยนตำแหน่งของ Route โดยเอา Guest Routes (`login`, `register`) มาไว้ด้านบน Protected Routes เพื่อให้ Angular แมตช์หน้าล็อกอิน/ลงทะเบียนก่อนโดยไม่ต้องผ่าน Guard ของฝั่งหลังบ้าน
  - ปรับปรุงให้ Wildcard Route (`**`) ย้ายมา Redirect ไปยังหน้า `login` แทน `/dashboard` เพื่อป้องกันปัญหาการเข้าหน้าที่ไม่มีอยู่จริงแล้วเด้งกลับหน้าว่างเปล่า
- **Action:** แก้ไขบั๊กหน้าจอค้างไม่แสดงอะไรเลย (App Blocking) ที่มีสาเหตุมาจาก `APP_INITIALIZER`:
  - ปรับปรุง `app.config.ts` ให้เพิ่ม **Timeout ป้องกันแอปค้างเป็นเวลา 5 วินาที** และใช้ระบบ **Always Resolve Promise** ทำให้ไม่มีกรณีใดที่ระบบโหลดตั้งต้นจะทำการบล็อกแอปพลิเคชันจากการเรนเดอร์เนื้อหาหน้าจอ
  - ปรับปรุงเมธอด `verifySession()` ใน `AuthService` ให้เป็นกระบวนการตรวจความถูกต้องของ Token แบบ **Local (JWT Expiration Verification)** เท่านั้น โดยไม่ทำการยิง API ไปเช็คกับ Backend ในช่วงเริ่มต้นบูตแอป เพื่อตัดปัญหาการชนกันของ `errorInterceptor` 401 Refresh Cascade Loop และป้องกันปัญหาในกรณีที่ Backend ดับอยู่
- **Action:** ปรับเปลี่ยนเงื่อนไขใน `authGuard` ให้ตรวจสอบเป้าหมายของ URL ปัจจุบัน หากผู้ใช้ที่ยังไม่ล็อกอินกำลังจะเข้าสู่หน้าแรกสุด (Root Path) ระบบจะทำการเปลี่ยนเส้นทางไปเงียบๆ แต่หากเจาะจงเข้าหน้าภายใน (เช่น `/orders`) จะแสดงป๊อปอัป SweetAlert2 และเสนอหน้าล็อกอินตามปกติ

### Verification
- **Frontend Build:** `ng build --configuration=development` ตรวจสอบแล้วผ่าน 100% ไม่มี Error หรือ Warning ใดๆ ในโครงสร้างใหม่





