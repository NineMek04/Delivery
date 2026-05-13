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
