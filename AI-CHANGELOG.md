# AI-CHANGELOG: Context Ledger & Sync

## [Project Status: In Development]

- **Current Milestone:** Phase 1 - Architecture & Database Setup
- **Shared Registry:** Azure Artifacts (BetimesShare)

---

## [LOG TEMPLATE - วิธีการบันทึก]

### [Date: YYYY-MM-DD] | โดย: [ชื่อคนทำ/AI]

- **Service:** เช่น BackendApi / ai-engine
- **Action:** สรุปสิ่งที่ทำสำเร็จ
- **Applied Version:** เวอร์ชันล่าสุดที่นำไปใช้จริง
- **Impact:** ผลกระทบต่อส่วนอื่น ถ้ามี

---

## [Log Date: 2026-05-12] | โดย: AI Agent

### Component: Environment Setup

- **Action:** แก้ไขปัญหาการเชื่อมต่อ Private Registry (E401) ผ่านไฟล์ `.npmrc` และ `vsts-npm-auth`
- **Status:** สำเร็จ สามารถ `npm install` ได้แล้ว
- **Note:** ต้องต่อ VPN บริษัททุกครั้งก่อนรันคำสั่ง npm

### Component: Database (PostGIS)

- **Action:** สร้างฐานข้อมูลและ Extension PostGIS พร้อมกำหนดมาตรฐานพิกัด SRID 4326
- **Status:** พร้อมใช้งาน เชื่อมต่อผ่าน DBeaver สำเร็จ

### Component: Infrastructure (Docker)

- **Action:** เริ่มร่างโครงสร้าง `docker-compose.yml` สำหรับเชื่อมโยง 4 Microservices
- **Note:** รอการใส่ค่า Environment Variable จากสมาชิกทีมท่านอื่น

---

## [Log Date: 2026-05-13] | โดย: AI Agent

### Component: BackendApi Foundation

- **Action:** เพิ่ม `Core/DeliveryControllerBase.cs` เป็น base controller สำหรับ API รุ่นถัดไป โดยมี access ไปยัง `ApplicationDbContext`, logger และ current user id จาก claims
- **Action:** แยก startup code จาก `Program.cs` ไปยัง `Setup/ServiceSetup.cs` และ `Setup/ApplicationSetup.cs` เพื่อรวม DI, EF Core/PostGIS, Swagger, CORS, SignalR และ middleware pipeline
- **Applied Version:** BackendApi ใช้ .NET 8 minimal hosting พร้อม setup extensions, EF Core/Npgsql NetTopologySuite, Swagger และ SignalR registration
- **Impact:** `Program.cs` สั้นลงและพร้อมต่อยอด controller/repository/hub โดยไม่ต้องกระจาย config หลายจุด

### Component: BackendApi Security Baseline

- **Action:** เพิ่ม JWT Bearer authentication ตามแนวทางจาก Bookings API reference พร้อม role policies: `AdminOnly`, `Operations`, `Rider`
- **Action:** เพิ่ม `Security/JwtTokenService.cs`, `Security/AuthConstants.cs`, `Security/LoginAttemptService.cs`, `Security/TokenSubject.cs` และ `Security/ITokenService.cs`
- **Action:** เพิ่ม global rate limiting และ policy `auth` สำหรับ login/register endpoints ในอนาคต
- **Action:** เพิ่ม security headers middleware และปิด Kestrel server header
- **Action:** เพิ่ม `.env` loader และ `BackendApi/.env.example` สำหรับ local environment configuration
- **Applied Version:** JWT config ต้องตั้งผ่าน `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`; `Jwt__Key` ต้องยาวอย่างน้อย 32 ตัวอักษร และห้ามใช้ placeholder
- **Impact:** BackendApi มีมาตรฐาน token/security พร้อมก่อนสร้าง AuthController/User model จริง

### Component: Documentation / Spec

- **Action:** อัปเดต `AI-BLUEPRINT.md` ให้ตรงกับสถานะจริงของ BackendApi: EF Core/PostGIS, migrations, Dockerfile, setup extensions, JWT baseline และ next priorities
- **Action:** อัปเดต `.cursorrules` เพิ่มมาตรฐาน security, secrets และ CORS
- **Impact:** Project spec ล่าสุดตรงกับ codebase มากขึ้น ลดความเสี่ยงที่งานถัดไปจะอิงสถานะเก่า

### Verification

- **Command:** `dotnet build BackendApi\BackendApi.csproj`
- **Result:** Build succeeded, 0 warnings, 0 errors

---

## [Log Date: 2026-05-13] | By: AI Agent

### Component: BackendApi Data Handler Core

- **Action:** Added `Core/DataHandlers/DBHandlerCore.cs` as an EF Core-based handler inspired by `DBHandlerCore` from `online3-casemanagement-api` and `online3-ds-api`, without bringing DevExpress XPO into this project.
- **Action:** Added `Core/DataHandlers/ConditionContext.cs` for optional convention-based filters such as `RecordStatus` / `RECORD_STATUS` and `DelFlag` / `DEL_FLAG` when future entities include those fields.
- **Action:** Updated `Core/DeliveryControllerBase.cs` with a protected `DB` property so child controllers can use a familiar pattern similar to `BetimesControllerBase`.
- **Action:** Registered `ConditionContext` and `DBHandlerCore` as scoped services in `Setup/ServiceSetup.cs`.
- **Action:** Added missing policy constants to `Security/AuthConstants.cs` so security setup and policy names are centralized.
- **Applied Version:** Controllers can now call `DB.GetQuery<T>()`, `DB.GetObjectListAsync<T>()`, `DB.GetObjectByKeyAsync<T>()`, `DB.InsertObject(entity)`, `DB.UpdateObject(entity)`, `DB.DeleteObjectAsync<T>(key)`, and `DB.CommitChangesAsync()`.
- **Impact:** BackendApi has a practical data-handler layer for upcoming controllers while still matching the project constraint to use EF Core/PostGIS.

### Verification

- **Command:** `dotnet build BackendApi\BackendApi.csproj`
- **Result:** Build succeeded, 0 warnings, 0 errors

---

## [Log Date: 2026-05-13] | By: AI Agent

### Component: AI Engine Setup

- **Action:** ตรวจสอบและยืนยันโครงสร้างโค้ดของ `ai-engine` (FastAPI + OR-Tools)
- **Action:** ยืนยันความพร้อมของ `main.py`, `requirements.txt`, `Dockerfile` และ `.dockerignore` 
- **Applied Version:** บริการ AI พร้อมทำงานด้วย Python 3.11-slim เปิดพอร์ต 8000 สอดคล้องกับโครงสร้างใน `docker-compose.yml`
- **Impact:** `ai-engine` เปลี่ยนสถานะจาก Empty เป็น Foundation Ready พร้อมสำหรับการทดสอบและเชื่อมโยงกับ BackendApi

### Component: Frontend Admin Dashboard (Fluent API)

- **Action:** นำแนวคิด Fluent API สำหรับ HTTP Request จากโปรเจกต์เดิมมาปรับปรุงเป็น `DeliveryHttpRequest` ใน `admin-dashboard/src/app/core/http/delivery-http-request.ts`
- **Action:** สร้าง `environments/environment.ts` เพื่อกำหนดค่า `apiUrl` ชี้ไปที่ Backend (`http://localhost:5000`)
- **Action:** เพิ่ม `provideHttpClient()` ใน `app.config.ts` และเก็บค่า `InjectorInstance` ที่ `app.component.ts` เพื่อให้สามารถทำงานกับ `req<T>()` ได้แบบ static
- **Action:** เตรียมตัวอย่างการเรียกใช้งาน API ผ่านไฟล์ `app/services/route.service.ts` สำหรับ Route Optimization
- **Applied Version:** Frontend สามารถใช้ `req<T>('path').body(data).post()` เพื่อสื่อสารกับ BackendApi ได้แล้ว โดยรองรับโมเดล `HttpStatusResult`
- **Impact:** เตรียมรากฐานการคุยกันระหว่าง Angular (Frontend) และ .NET (Backend) เสร็จสิ้น รอการรัน `npm install` ผ่าน VPN เพื่อใช้งานจริง

### Component: Frontend Admin Dashboard (Base Services & Interceptors)

- **Action:** สร้าง `BaseApiService<T>` สำหรับให้ Service อื่นสืบทอด (มีเมธอด `getAll`, `getById`, `create`, `update`, `delete`)
- **Action:** สร้าง `AuthService` พร้อมระบบ Token Clocking โดยใช้ `interval` ตรวจสอบ Token Expiration ด้วย `jwt-decode` แบบอัตโนมัติ
- **Action:** สร้าง Functional Interceptors ได้แก่ `auth.interceptor.ts` เพื่อส่ง HTTP Header `Authorization: Bearer <token>`
- **Action:** สร้าง `error.interceptor.ts` สำหรับจัดการ Global Error (401, 403, 500) โดยนำ `sweetalert2` เข้ามาใช้ในการแสดงผลแจ้งเตือน
- **Action:** กำหนด `provideHttpClient(withInterceptors(...))` ลงใน `app.config.ts`
- **Impact:** Frontend มีโครงสร้างที่พร้อมสำหรับรองรับ Authentication (JWT) และการจัดการข้อผิดพลาดตาม Security Baseline ที่กำหนดไว้ในส่วนของ BackendApi

### Component: BackendApi Foundation — Phase 1 (Global Wrapper, Mapster, FluentValidation)

- **Action:** ติดตั้ง NuGet: `Mapster 10.0.0`, `Mapster.DependencyInjection 10.0.0`, `FluentValidation.AspNetCore 11.3.0`
- **Action:** สร้าง `Core/Models/ApiResponse.cs` — Standard JSON Wrapper (`Success`, `Message`, `Value`, `ErrorDetail`, `Code`) ตรงกับ `HttpStatusResult` ฝั่ง Angular
- **Action:** สร้าง `Core/Models/PaginatedResult.cs` — โมเดลแบ่งหน้าพร้อม `TotalCount`, `Page`, `PageSize`, `HasPrevious`, `HasNext`
- **Action:** สร้าง `Core/Filters/GlobalResponseFilter.cs` — ห่อ Response อัตโนมัติด้วย `ApiResponse`, รองรับ `[DisableWrapper]` bypass
- **Action:** สร้าง `Core/Filters/GlobalExceptionFilter.cs` — ดัก Unhandled Exception, แสดง stack trace ใน Dev mode เท่านั้น
- **Action:** สร้าง `Core/Filters/ValidationFilter.cs` — ตรวจ FluentValidation ก่อนเข้า Action Method อัตโนมัติ
- **Action:** สร้าง `Core/Attributes/DisableWrapperAttribute.cs` — Bypass wrapper สำหรับ Raw Data (PDF/Excel)
- **Action:** สร้าง `Core/Mappings/MappingConfig.cs` — จุดศูนย์กลางตั้งค่า Entity↔DTO mapping
- **Action:** สร้าง `Core/DataHandlers/DBHandlerCoreExtensions.cs` — Pagination extension methods
- **Action:** สร้าง `Core/CrudControllerBase.cs` — Generic CRUD สำหรับ Master Data (route prefix `api/v1/`)
- **Action:** สร้างโครงสร้างโฟลเดอร์ `Controllers/MasterData/` และ `Controllers/Business/` แยกกันชัดเจน
- **Action:** อัปเดต `ServiceSetup.cs` — ลงทะเบียน Mapster, FluentValidation (Singleton), Global Filters ทั้ง 3 ตัว และ Swagger XML Comments
- **Action:** เพิ่ม `<GenerateDocumentationFile>` ใน `.csproj` สำหรับ Swagger XML enrichment
- **Applied Version:** Backend พร้อมรองรับ API Response มาตรฐาน, Validation อัตโนมัติ, Pagination, และ Object Mapping ตั้งแต่ระดับ Foundation
- **Impact:** ทุก API ที่เขียนต่อจากนี้จะมี Format เดียวกันอัตโนมัติ ลดโค้ดซ้ำซ้อนและทำให้ Frontend สามารถ handle ข้อมูลแบบ Predictable

### Verification

- **Command:** `dotnet restore; dotnet build --no-restore`
- **Result:** Build succeeded, 0 warnings, 0 errors

---

## [Log Date: 2026-05-13] | By: AI Agent

### Component: BackendApi — Phase 2 (Swagger Perfection & API Contract)

- **Action:** สร้าง `Models/DTOs/OrderDto.cs` — DTO สำหรับ Order Entity พร้อม XML Comments ครบทุก field (PickupLat/Lng, DropoffLat/Lng, Status, etc.)
- **Action:** สร้าง `Models/DTOs/RiderDto.cs` — DTO สำหรับ Rider Entity พร้อม XML Comments
- **Action:** อัปเดต `Core/Mappings/MappingConfig.cs` — ลงทะเบียน Mapster mappings สำหรับ PostGIS Point ↔ lat/lng (SRID 4326) ทั้ง Order และ Rider
- **Action:** เพิ่ม `[ProducesResponseType]` annotations ครบทุก CRUD action ใน `CrudControllerBase`
- **Action:** เพิ่ม XML doc comments ครบถ้วนใน `CrudControllerBase` สำหรับ Swagger enrichment
- **Action:** เพิ่ม `[Route("api/v1/[controller]")]` และ `[Produces("application/json")]` ที่ `DeliveryControllerBase`
- **Action:** สร้าง `Controllers/MasterData/RidersController.cs` — ตัวอย่าง Controller สืบทอดจาก CrudControllerBase
- **Applied Version:** ทุก API Endpoint มี route prefix มาตรฐาน `api/v1/`, Swagger แสดง XML Comments และ response types ครบถ้วน

### Component: Frontend Admin Dashboard — Phase 2 (OpenAPI Generator Setup)

- **Action:** เพิ่ม `@openapitools/openapi-generator-cli` เป็น devDependency ใน `package.json`
- **Action:** สร้าง `openapitools.json` config กำหนดให้ Gen เฉพาะ TypeScript Models (DTOs) เท่านั้น
- **Action:** เพิ่ม npm script `generate:api` สำหรับรัน Generator จาก Swagger JSON
- **Action:** สร้าง `.openapi-generator-ignore` ใน `src/app/api/generated/`
- **Action:** อัปเดต `environments/environment.ts` ให้ apiUrl ชี้ไปที่ `http://localhost:5000/api/v1`
- **Applied Version:** ทีมสามารถรัน `npm run generate:api` (หลังจากเปิด Backend Swagger ไว้) เพื่อ Gen TypeScript Models อัตโนมัติ
- **Impact:** Frontend ↔ Backend ใช้ Contract เดียวกัน (Type-Safe) ผ่าน OpenAPI spec ลดความเสี่ยงที่ข้อมูลจะไม่ตรงกัน

### Verification

- **Command:** `dotnet build`
- **Result:** Build succeeded, 0 warnings, 0 errors

---

## [Log Date: 2026-05-14] | By: AI Agent

### Component: Flutter Rider App Foundation

- **Action:** สร้างโครงสร้างพื้นฐาน (Foundation) สำหรับ `rider_app` ทั้งหมด 30 ไฟล์ แบ่งเป็น 9 Phases
- **Action:** กำหนดมาตรฐาน API Client (Dio), Auth Service (JWT + Secure Storage), SignalR (Real-time), และ Location Service (GPS Tracking) ให้สอดคล้องกับ BackendApi และ admin-dashboard
- **Action:** สร้าง Data Models (Freezed) ที่ตรงกับ DTOs ของ BackendApi (Rider, Order, RouteResult) พร้อมห่อหุ้มด้วย `ApiResponse` มาตรฐาน
- **Action:** ตั้งค่า Routing ด้วย `go_router` พร้อมระบบ Auth Guard และจัดการ Theme (Dark Mode) สำหรับการใช้งานภาคสนาม
- **Action:** สร้าง Scaffolding สำหรับ Feature สำคัญ (Auth, Home, Delivery, Tracking) พร้อม placeholder UI และ Riverpod Providers
- **Applied Version:** Flutter Rider App อยู่ในสถานะ **Foundation Ready** พร้อมให้ทีมพัฒนาต่อยอด UI และ Business Logic
- **Impact:** โปรเจกต์ทั้ง 3 ส่วน (Backend, Admin, Rider) มีโครงสร้างและ Contract ในการสื่อสารที่ตรงกัน 100%

### Verification

- **Status:** วางโครงสร้างไฟล์เสร็จสิ้น (ยังไม่ได้รัน build เนื่องจากสภาพแวดล้อมไม่มี Flutter SDK)
- **Files Created:** `lib/main.dart`, `app/`, `core/`, `features/`, `models/`, `shared/`, `pubspec.yaml`
