# AI-BLUEPRINT: Smart Delivery Routing System

> **ชื่อโครงการ:** ระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์  
> **English:** AI-Optimized Smart Delivery Routing System  
> **ผู้พัฒนา:** นายนนท์ธรัตน์ ทาลา  
> **สถาบัน:** มหาวิทยาลัยราชภัฏอุดรธานี — วิศวกรรมคอมพิวเตอร์และการสื่อสาร  
> **Last Updated:** 2026-05-12

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
| **Backend API**    | .NET (ASP.NET Core)         | **.NET 8**        | `net8.0` in csproj + Swagger/OpenAPI           |
| **Real-time**      | SignalR                     | —                 | ยังไม่ได้เพิ่ม — ต้อง install NuGet package      |
| **AI Engine**      | Python + FastAPI            | —                 | ยังว่าง — `main.py` + `requirements.txt` ว่าง   |
| **AI Algorithm**   | Google OR-Tools             | —                 | ยังไม่ได้ install                                |
| **Database**       | PostgreSQL + PostGIS        | 15-3.3 (Docker)   | GEOMETRY(Point, 4326) / SRID 4326              |
| **ORM**            | EF Core + NetTopologySuite  | —                 | ยังไม่ได้เพิ่ม NuGet packages                    |
| **Frontend**       | Angular                     | **v19.2.0**       | Standalone components (ไม่มี NgModules)         |
| **Mobile App**     | Flutter (Dart)              | —                 | **❌ ยังไม่ได้สร้างใน repo**                      |
| **Container**      | Docker + Docker Compose     | v3.8              | 4 services defined                             |
| **Web Server**     | Nginx                       | —                 | สำหรับ serve Angular ใน container               |

---

## 3. System Architecture

### 3.1 Microservices Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Docker Network                           │
│                                                             │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │  delivery-db  │  │delivery-     │  │  delivery-ai     │  │
│  │   PostGIS     │  │  backend     │  │  Python FastAPI   │  │
│  │  15-3.3       │  │  .NET 8      │  │  + OR-Tools       │  │
│  │  Port: 5432   │  │  Port: 5000  │  │  Port: 8000       │  │
│  └──────────────┘  └──────────────┘  └──────────────────┘  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │        delivery-frontend — Angular via Nginx          │   │
│  │                    Port: 80                           │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                             │
                    ┌────────┴────────┐
                    │  Flutter App    │
                    │  (Rider Mobile) │
                    │  ❌ ยังไม่สร้าง    │
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

### 3.3 ลำดับการทำงาน
1. **Rider App (Flutter)** ส่งพิกัด GPS → **.NET** ผ่าน SignalR
2. **.NET** รวม orders + พิกัดร้าน + พิกัดลูกค้า + คนขับที่ว่าง → ส่ง **AI Service**
3. **AI Service** แก้ VRP → หาเส้นทางที่สั้น/เวลาน้อยที่สุด
4. **AI Service** ส่ง Waypoint Sequence กลับ → **.NET**
5. **.NET** บันทึก DB → Broadcast ผ่าน SignalR → App + Dashboard อัปเดตพร้อมกัน

---

## 4. Repository Structure (สถานะจริง)

```
Delivery/
├── .cursorrules             ← กฎสำหรับ AI assistant (Cursor)
├── .github/workflows/       ← [ว่าง] CI/CD workflows
├── AI-BLUEPRINT.md          ← ไฟล์นี้ — AI Context Ledger
├── AI-CHANGELOG.md          ← Log การเปลี่ยนแปลงโดย AI
├── Delivery.sln             ← .NET Solution (1 project: BackendApi)
├── docker-compose.yml       ← ✅ 4 services defined (db, backend, ai-service, frontend)
├── README.md
│
├── BackendApi/              ← .NET 8 Web API
│   ├── BackendApi.csproj    ← net8.0, Swagger, OpenAPI
│   ├── Program.cs           ← ⚠️ ยังเป็น WeatherForecast template
│   ├── appsettings.json
│   └── Properties/
│
├── ai-engine/               ← Python FastAPI
│   ├── main.py              ← ❌ ว่าง
│   └── requirements.txt     ← ❌ ว่าง
│
└── admin-dashboard/         ← Angular 19
    ├── package.json          ← Angular ^19.2.0
    ├── src/app/
    │   ├── app.component.*   ← ⚠️ Default Angular template
    │   ├── app.routes.ts
    │   └── app.config.ts
    └── ...configs
```

---

## 5. Current State of Development

### สถานะรวม: 🟡 **Phase 1 — Infrastructure Scaffolded**

| Component            | Status            | รายละเอียด                                                    |
|----------------------|-------------------|--------------------------------------------------------------|
| **docker-compose**   | ✅ Created         | 4 services: db, backend, ai-service, frontend                |
| **PostGIS DB**       | ✅ Running         | เชื่อมต่อผ่าน DBeaver ได้แล้ว (ตาม changelog)                  |
| **BackendApi**       | ⚠️ Template Only  | WeatherForecast template + Swagger — ไม่มี domain models      |
| **ai-engine**        | ❌ Empty           | `main.py` + `requirements.txt` ว่างเปล่า                      |
| **admin-dashboard**  | ⚠️ Template Only  | Angular 19 default — ไม่มี custom components                  |
| **Flutter App**      | ❌ Not Created     | ไม่มีโฟลเดอร์ใน repo                                          |
| **SignalR Hub**      | ❌ Not Added       | ยังไม่ได้ install NuGet package                                |
| **EF Core + PostGIS**| ❌ Not Added       | ยังไม่มี entities / migrations / NetTopologySuite              |
| **Dockerfiles**      | ❌ Missing         | docker-compose อ้าง Dockerfile แต่ยังไม่ได้สร้างในแต่ละ service |
| **CI/CD**            | ❌ Empty           | `.github/workflows/` ว่าง                                     |

### Git History (4 commits)
```
79a421f feat: initialize backend API project with Docker configuration
2833bb0 feat: add initial project structure and autogenerated files for BackendApi
2bdf6ac feat: initialize admin dashboard with Angular setup
e0f3089 Initial commit
```

---

## 6. Docker Compose Config (ปัจจุบัน)

| Service        | Image / Build          | Port  | Container Name       | Status              |
|----------------|------------------------|-------|----------------------|---------------------|
| **db**         | `postgis/postgis:15-3.3`| 5432  | delivery-db          | ✅ ใช้งานได้          |
| **backend**    | `./BackendApi/Dockerfile`| 5000 | delivery-backend     | ❌ ไม่มี Dockerfile   |
| **ai-service** | `./ai-engine/Dockerfile` | 8000 | delivery-ai          | ❌ ไม่มี Dockerfile   |
| **frontend**   | `./admin-dashboard/Dockerfile`| 80| delivery-frontend    | ❌ ไม่มี Dockerfile   |

> ⚠️ **Critical:** `docker-compose.yml` อ้าง Dockerfile ใน 3 services แต่ยังไม่ได้สร้างไฟล์ `Dockerfile` ใน folder ใดเลย

---

## 7. Domain Concepts

| Term (EN)                          | Term (TH)                    | Description                                    |
|------------------------------------|------------------------------|------------------------------------------------|
| **VRP** (Vehicle Routing Problem)  | ปัญหาการจัดเส้นทางยานพาหนะ     | หาเส้นทางสั้นที่สุดสำหรับยานพาหนะหลายคัน         |
| **Batched Orders**                 | ออเดอร์ซ้อน                    | รวมหลายออเดอร์ส่งพร้อมกัน                        |
| **Multi-drop**                     | จุดส่งหลายจุด                  | เส้นทางที่มีจุดรับ-ส่งหลายจุด                      |
| **Waypoint Sequence**              | ลำดับจุดแวะพัก                 | ผลลัพธ์จาก AI — ลำดับจุดที่คนขับต้องไป             |
| **Rider / Driver**                 | พนักงานขับรถ                   | คนส่งสินค้า ใช้ Flutter App                       |
| **GEOMETRY(Point, 4326)**          | —                            | PostGIS data type — พิกัด GPS (WGS84)           |
| **SignalR Broadcast**              | บรอดแคสต์                     | ส่งข้อมูลไปทุก client พร้อมกัน                     |
| **Geospatial Query**               | การค้นหาเชิงพื้นที่             | SQL query ค้นหาจากพิกัด/รัศมี (GiST Index)       |

---

## 8. Development Standards (จาก .cursorrules)

- **Backend:** Repository Pattern + Dependency Injection
- **Frontend:** Component-based architecture (Angular standalone)
- **Database:** ทุก Geospatial Query ต้องใช้ GiST Index
- **Communication:** GPS data ส่งผ่าน SignalR/WebSockets เท่านั้น
- **Container:** ทุก service ต้องนิยามใน `docker-compose.yml`
- **Logging:** ก่อนบันทึกลง `AI-CHANGELOG.md` ต้องถามผู้ใช้ยืนยันก่อน

---

## 9. Environment Notes

- **Hardware:** ASUS ROG — อาจมีปัญหาความร้อน / GPU Driver (nvlddmkm)
- **GPU:** หากใช้ Lossless Scaling → จำกัด FPS ที่ 60
- **Private Registry:** ใช้ Azure Artifacts (BetimesShare) — ต้องต่อ VPN + ตรวจ `.npmrc`
- **Database:** PostGIS เชื่อมต่อผ่าน DBeaver สำเร็จแล้ว

---

## 10. What Needs to Be Done Next (Priority Order)

### 🔴 Critical — ขาดและ block การทำงาน
1. **สร้าง Dockerfiles** — backend, ai-engine, frontend (docker-compose อ้างอยู่แต่ไม่มีไฟล์)
2. **พัฒนา BackendApi** — เปลี่ยนจาก WeatherForecast → Domain models + EF Core + SignalR Hub
3. **พัฒนา ai-engine** — FastAPI + OR-Tools VRP solver

### 🟡 Important — ต้องทำเร็วๆ นี้
4. **พัฒนา Angular Dashboard** — Map view + real-time tracking UI
5. **สร้าง Flutter project** — Rider App + GPS tracking
6. **ออกแบบ Database Schema** — EF Core entities + migrations

### 🟢 Nice-to-have
7. **CI/CD Workflows** — GitHub Actions
8. **Integration Tests** — ทดสอบการเชื่อมต่อระหว่าง services

---

## 11. AI-Specific Notes (สำหรับ AI Assistant)

### ⚠️ สิ่งที่ต้องระวัง
- **Version จริง:** Backend ใช้ `.NET 8` (ไม่ใช่ .NET 9 — ตรวจแล้วจาก csproj ล่าสุด)
- **PostGIS image:** ต้องใช้ `postgis/postgis` ไม่ใช่ `postgres` ธรรมดา
- **Dockerfiles ยังไม่มี:** docker-compose อ้าง `./BackendApi/Dockerfile` etc. แต่ไม่มีไฟล์จริง
- **Angular 19** ใช้ standalone components — ไม่มี NgModules
- **SignalR** ต้อง config CORS ให้ Flutter + Angular เชื่อมต่อได้
- **NetTopologySuite** ต้องเพิ่มเป็น NuGet package เพื่อ map GEOMETRY types
- **OR-Tools** ติดตั้งผ่าน `pip install ortools`
- **SRID 4326** (WGS84) ใช้ทั้งระบบ

### 📋 Files ที่ต้องอ่านก่อนเริ่มงาน
1. `AI-BLUEPRINT.md` — ไฟล์นี้ (Context Ledger)
2. `AI-CHANGELOG.md` — สถานะงานล่าสุด
3. `.cursorrules` — กฎและ workflow ของ AI
4. `docker-compose.yml` — โครงสร้าง infrastructure
