# AI-CHANGELOG: Context Ledger & Sync

## [Project Status: In Development]

- **Current Milestone:** Phase 1 - Architecture & Database Setup
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

## [Log Date: 2026-05-15] | By: AI Agent

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
