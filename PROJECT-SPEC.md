# 📋 Project Specification
## AI-Optimized Smart Delivery Routing System
### ระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์

> **Version:** 0.1.0 (Infrastructure Phase)  
> **Last Updated:** 2026-05-12  
> **Team Lead:** นนท์ธรัตน์ ทาลา

---

## 1. ภาพรวมโครงการ (Overview)

ระบบที่ใช้ **AI คำนวณเส้นทางขนส่ง** ให้สั้นที่สุดและประหยัดที่สุด สำหรับการจัดส่งสินค้า/อาหารที่มีจุดรับ-ส่งหลายจุด (Multi-drop) พัฒนาเป็น **Microservices Architecture** บน Docker Container เพื่อให้ทั้งทีมสามารถ setup dev environment ได้ในคำสั่งเดียว

### ปัญหาที่แก้
- คนขับรถได้เส้นทางที่ไม่ผ่านการ optimize → เสียเวลา + เชื้อเพลิง
- ระบบที่มีอยู่ต้องใช้ฮาร์ดแวร์ GPS แพง → ใช้สมาร์ทโฟนแทน (BYOD)

### Core Features
| Feature | Description |
|---------|-------------|
| 🗺️ AI Route Optimization | คำนวณเส้นทางที่สั้นที่สุดด้วย VRP Algorithm (Google OR-Tools) |
| 📡 Real-time GPS Tracking | ติดตามตำแหน่งคนขับ real-time ผ่าน WebSocket (SignalR) |
| 📊 Admin Dashboard | แดชบอร์ดดูสถานะคนขับ + ออเดอร์ บนแผนที่ |
| 📱 Rider Mobile App | แอปสำหรับคนขับ — ส่งพิกัด GPS + รับเส้นทาง |
| 🐳 One-Command Deploy | `docker-compose up` รันทั้งระบบ |

---

## 2. Tech Stack

### สรุปแบบรวดเร็ว

```
Frontend:  Angular 19        →  Admin Dashboard (Web)
Mobile:    Flutter            →  Rider App (iOS/Android)
Backend:   .NET 8 + SignalR   →  API Gateway + Real-time Hub
AI:        Python FastAPI     →  VRP Solver (OR-Tools)
Database:  PostgreSQL/PostGIS →  Spatial Data (GPS coordinates)
Infra:     Docker Compose     →  ทุก service รวมในที่เดียว
```

### รายละเอียด Version

| Component | Technology | Version | Package Manager |
|-----------|-----------|---------|-----------------|
| Backend API | ASP.NET Core | **.NET 8.0** | NuGet |
| Backend Packages | Swashbuckle (Swagger) | 6.6.2 | NuGet |
| Backend Packages | Microsoft.AspNetCore.OpenApi | 8.0.26 | NuGet |
| AI Engine | Python + FastAPI | 3.11 + ≥0.110.0 | pip |
| AI Engine | Google OR-Tools | ≥9.9 | pip |
| AI Engine | Uvicorn | ≥0.29.0 | pip |
| Frontend | Angular | **19.2.0** | npm |
| Frontend | TypeScript | 5.7.2 | npm |
| Frontend | RxJS | 7.8.x | npm |
| Database | PostgreSQL + PostGIS | **15-3.3** | Docker image |
| Mobile | Flutter (Dart) | TBD | pub |

### Packages ที่ยังต้องเพิ่ม (ยังไม่ได้ install)

| Service | Package | Purpose |
|---------|---------|---------|
| Backend | `Microsoft.AspNetCore.SignalR` | WebSocket real-time communication |
| Backend | `Npgsql.EntityFrameworkCore.PostgreSQL` | EF Core + PostgreSQL |
| Backend | `NetTopologySuite` | Map PostGIS GEOMETRY types ใน C# |
| Frontend | `@microsoft/signalr` | SignalR client สำหรับ Angular |
| Frontend | `leaflet` หรือ `@angular/google-maps` | แสดงแผนที่ |

---

## 3. System Architecture

### 3.1 Services Overview

```
┌──────────────────────────────────────────────────────────────┐
│                     Docker Compose Network                    │
│                                                              │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐  │
│  │    db           │  │   backend      │  │  ai-service    │  │
│  │  PostGIS 15     │  │  .NET 8        │  │  Python 3.11   │  │
│  │                 │  │  ASP.NET Core  │  │  FastAPI       │  │
│  │  Port: 5432     │  │  + SignalR     │  │  + OR-Tools    │  │
│  │                 │  │  Port: 5000    │  │  Port: 8000    │  │
│  └────────────────┘  └────────────────┘  └────────────────┘  │
│          ▲                   ▲ ▲                  ▲           │
│          │                   │ │                  │           │
│          └───────────────────┘ └──────────────────┘           │
│                                                              │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │                    frontend                               │ │
│  │              Angular 19 + Nginx                           │ │
│  │              Port: 80                                     │ │
│  └──────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
                              ▲
                              │ WebSocket (SignalR)
                    ┌─────────┴─────────┐
                    │   Flutter App      │
                    │   Rider Mobile     │
                    │   (ยังไม่ได้สร้าง)    │
                    └───────────────────┘
```

### 3.2 Docker Services

| Service | Container Name | Build Context | Port (Host:Container) | Depends On |
|---------|---------------|---------------|----------------------|------------|
| **db** | delivery-db | `postgis/postgis:15-3.3` (image) | `5432:5432` | — |
| **backend** | delivery-backend | `./BackendApi/Dockerfile` | `5000:80` | db |
| **ai-service** | delivery-ai | `./ai-engine/Dockerfile` | `8000:8000` | db |
| **frontend** | delivery-frontend | `./admin-dashboard/Dockerfile` | `80:80` | backend |

### 3.3 Data Flow

```
1. คนขับเปิดแอป Flutter → ส่ง GPS ผ่าน SignalR → .NET Backend
2. มีออเดอร์เข้า → Backend รวมข้อมูล (orders + พิกัดร้าน + พิกัดลูกค้า + คนขับว่าง)
3. Backend ส่ง REST request → AI Service (Python)
4. AI Service แก้สมการ VRP → ส่ง Waypoint Sequence กลับ
5. Backend บันทึก DB → Broadcast ผ่าน SignalR → แอปคนขับ + Dashboard อัปเดตพร้อมกัน
```

---

## 4. Project Structure

```
Delivery/
│
├── 📄 Delivery.sln              .NET Solution file
├── 📄 docker-compose.yml        Docker orchestration (4 services)
├── 📄 AI-BLUEPRINT.md           AI Context Ledger (สำหรับ AI assistant)
├── 📄 AI-CHANGELOG.md           AI Change Log
├── 📄 PROJECT-SPEC.md           ไฟล์นี้ — สเปคโปรเจค
├── 📄 .cursorrules              กฎสำหรับ AI coding assistant
├── 📄 README.md
│
├── 📁 BackendApi/               ── .NET 8 Web API ──
│   ├── Dockerfile               Multi-stage build (SDK → Runtime)
│   ├── .dockerignore
│   ├── BackendApi.csproj         Target: net8.0
│   ├── Program.cs               ⚠️ ยังเป็น template (WeatherForecast)
│   ├── appsettings.json
│   └── Properties/
│
├── 📁 ai-engine/                ── Python FastAPI ──
│   ├── Dockerfile               Python 3.11-slim + uvicorn
│   ├── .dockerignore
│   ├── main.py                  ✅ Minimal — /health + /api/solve-vrp (placeholder)
│   └── requirements.txt         fastapi, uvicorn, ortools
│
├── 📁 admin-dashboard/          ── Angular 19 ──
│   ├── Dockerfile               Multi-stage build (Node → Nginx)
│   ├── nginx.conf               SPA fallback + API proxy
│   ├── package.json             Angular ^19.2.0
│   ├── src/
│   │   ├── app/
│   │   │   ├── app.component.*  ⚠️ Default Angular template
│   │   │   ├── app.routes.ts
│   │   │   └── app.config.ts
│   │   ├── index.html
│   │   ├── main.ts
│   │   └── styles.css
│   └── tsconfig*.json
│
└── 📁 .github/workflows/       CI/CD (ว่าง — ยังไม่ได้ setup)
```

---

## 5. Environment Setup

### Prerequisites
| Tool | Version | Download |
|------|---------|----------|
| Docker Desktop | ≥ 4.x | https://www.docker.com/products/docker-desktop |
| Git | ≥ 2.x | https://git-scm.com |
| Node.js | ≥ 20 LTS | https://nodejs.org (สำหรับ dev Angular โดยตรง) |
| .NET SDK | 8.0 | https://dotnet.microsoft.com/download (สำหรับ dev Backend โดยตรง) |
| Python | 3.11+ | https://python.org (สำหรับ dev AI Engine โดยตรง) |

### Quick Start — รันทั้งระบบ

```bash
# 1. Clone repo
git clone <repo-url>
cd Delivery

# 2. รันทุก service ด้วย Docker Compose
docker-compose up --build

# 3. เปิดใช้งาน
#    Frontend:     http://localhost
#    Backend API:  http://localhost:5000/swagger
#    AI Engine:    http://localhost:8000/health
#    Database:     localhost:5432 (user: postgres / pass: your_password)
```

### Dev Mode — รันแต่ละ service แยก

```bash
# Database (จำเป็นต้องรันก่อน)
docker-compose up db

# Backend (.NET)
cd BackendApi
dotnet run

# AI Engine (Python)
cd ai-engine
pip install -r requirements.txt
uvicorn main:app --reload --port 8000

# Frontend (Angular)
cd admin-dashboard
npm install
npm start
# → http://localhost:4200
```

### Database Connection

| Parameter | Value |
|-----------|-------|
| Host | `localhost` (dev) / `db` (Docker internal) |
| Port | `5432` |
| Database | `delivery_db` |
| User | `postgres` |
| Password | `your_password` |
| Extensions | PostGIS (auto-enabled) |
| Coordinate System | SRID 4326 (WGS84 — มาตรฐาน GPS) |

---

## 6. API Specification (Planned)

### Backend API (.NET) — `http://localhost:5000`

| Method | Endpoint | Description | Status |
|--------|----------|-------------|--------|
| GET | `/swagger` | Swagger UI | ✅ Ready |
| GET | `/weatherforecast` | Template endpoint (จะลบ) | ⚠️ Template |
| POST | `/api/orders` | สร้างออเดอร์ใหม่ | 🔲 TODO |
| GET | `/api/orders` | ดึงรายการออเดอร์ | 🔲 TODO |
| GET | `/api/riders` | ดึงรายชื่อคนขับ | 🔲 TODO |
| GET | `/api/riders/available` | คนขับที่ว่าง + location filter | 🔲 TODO |
| PUT | `/api/riders/{id}/location` | อัปเดตตำแหน่งคนขับ | 🔲 TODO |
| POST | `/api/routes/optimize` | ส่ง batch orders ไปคำนวณ AI | 🔲 TODO |
| — | `/hubs/tracking` | SignalR Hub — GPS real-time | 🔲 TODO |

### AI Service (Python) — `http://localhost:8000`

| Method | Endpoint | Description | Status |
|--------|----------|-------------|--------|
| GET | `/health` | Health check | ✅ Ready |
| POST | `/api/solve-vrp` | รับพิกัด → คำนวณ VRP → return waypoints | ⚠️ Placeholder |
| GET | `/docs` | FastAPI auto-generated docs | ✅ Ready |

---

## 7. Database Schema (Planned)

### Core Entities

```
┌─────────────────┐     ┌─────────────────┐     ┌──────────────────┐
│     Riders       │     │     Orders       │     │    Customers      │
│─────────────────│     │─────────────────│     │──────────────────│
│ Id         (PK)  │     │ Id         (PK)  │     │ Id          (PK)  │
│ Name             │◄────│ RiderId    (FK)  │     │ Name              │
│ Phone            │     │ CustomerId (FK)──│────►│ Phone             │
│ CurrentLocation  │     │ ShopId     (FK)  │     │ Location (GEOM)   │
│  (GEOMETRY 4326) │     │ Status           │     │ Address           │
│ IsAvailable      │     │ CreatedAt        │     └──────────────────┘
│ TotalPoints      │     │ CompletedAt      │
└─────────────────┘     └────────┬────────┘     ┌──────────────────┐
                                 │               │     Shops         │
                                 └──────────────►│──────────────────│
                                                 │ Id          (PK)  │
┌─────────────────┐                              │ Name              │
│     Routes       │                              │ Location (GEOM)   │
│─────────────────│                              │ Address           │
│ Id         (PK)  │                              └──────────────────┘
│ RiderId    (FK)  │
│ WaypointSeq      │  ← JSON: ลำดับจุดที่ต้องไป
│ TotalDistance     │  ← เมตร
│ TotalTime         │  ← วินาที
│ Status            │
│ CreatedAt         │
└─────────────────┘
```

### Spatial Data Rules
- ทุก location field ใช้ `GEOMETRY(Point, 4326)` — มาตรฐาน WGS84
- ใช้ **GiST Index** สำหรับ geospatial queries
- ค้นหาคนขับในรัศมี → `ST_DWithin()` + GiST → ระดับ millisecond

---

## 8. Development Standards

### Code Patterns
| Layer | Pattern | Detail |
|-------|---------|--------|
| Backend | **Repository Pattern** | แยก data access ออกจาก business logic |
| Backend | **Dependency Injection** | ใช้ built-in DI ของ ASP.NET Core |
| Frontend | **Component-based** | Angular standalone components (ไม่ใช้ NgModules) |
| AI Engine | **Async-first** | FastAPI async endpoints |
| Database | **Spatial Index** | ทุก geospatial query ต้องมี GiST index |
| Communication | **SignalR** | GPS data ส่งผ่าน WebSockets เท่านั้น |

### Git Convention
```
feat: เพิ่ม feature ใหม่
fix: แก้ bug
docs: แก้เอกสาร
refactor: ปรับโค้ดไม่เปลี่ยน behavior
chore: งาน infra / dependency
```

### Branch Strategy
```
main          ← production-ready
└── develop   ← integration branch
    ├── feature/xxx
    └── fix/xxx
```

---

## 9. Current Status

### Phase: 🟡 Infrastructure Setup (Phase 1 of 4)

```
Phase 1: Infrastructure   ████████░░░░  70%  ← ตอนนี้อยู่ตรงนี้
Phase 2: Core Backend      ░░░░░░░░░░░░   0%
Phase 3: AI + Frontend     ░░░░░░░░░░░░   0%
Phase 4: Integration       ░░░░░░░░░░░░   0%
```

### Component Status

| # | Component | Status | Owner | Notes |
|---|-----------|--------|-------|-------|
| 1 | Docker Compose | ✅ Done | — | 4 services, volumes configured |
| 2 | PostGIS Database | ✅ Running | — | เชื่อมต่อผ่าน DBeaver แล้ว |
| 3 | Dockerfiles (x3) | ✅ Created | — | backend, ai-engine, frontend |
| 4 | AI Engine Scaffold | ✅ Basic | — | /health + /api/solve-vrp placeholder |
| 5 | Backend API | ⚠️ Template | — | ยังเป็น WeatherForecast template |
| 6 | Angular Dashboard | ⚠️ Template | — | ยังเป็น default Angular template |
| 7 | Database Schema | 🔲 TODO | — | ต้อง design entities + migrations |
| 8 | SignalR Hub | 🔲 TODO | — | ต้องเพิ่ม NuGet package |
| 9 | VRP Solver | 🔲 TODO | — | ต้อง implement OR-Tools logic |
| 10 | Flutter Rider App | 🔲 TODO | — | ยังไม่ได้ init project |
| 11 | CI/CD Pipeline | 🔲 TODO | — | GitHub Actions ว่าง |

---

## 10. Task Breakdown — สิ่งที่ต้องทำ

### 🔴 Phase 2: Core Backend (ทำต่อ)
- [ ] ออกแบบ + สร้าง EF Core Entities (Rider, Order, Customer, Shop, Route)
- [ ] สร้าง Database Migrations + seed data
- [ ] เพิ่ม SignalR Hub (`/hubs/tracking`)
- [ ] สร้าง REST API endpoints (Orders, Riders, Routes)
- [ ] เชื่อม Backend → AI Service (HTTP client)

### 🟡 Phase 3: AI + Frontend
- [ ] Implement VRP Solver ด้วย Google OR-Tools
- [ ] สร้าง Angular components (Map, OrderList, RiderStatus)
- [ ] เชื่อม Angular → SignalR (real-time updates)
- [ ] Init Flutter project + GPS tracking

### 🟢 Phase 4: Integration & Testing
- [ ] Integration test ทุก service ผ่าน Docker
- [ ] End-to-end test: สร้าง order → AI คำนวณ → แสดงบน map
- [ ] Setup CI/CD pipeline

---

## 11. URLs & Ports (Quick Reference)

| Service | Dev URL | Docker URL |
|---------|---------|------------|
| **Frontend** | http://localhost:4200 | http://localhost |
| **Backend Swagger** | http://localhost:5000/swagger | http://localhost:5000/swagger |
| **AI Docs** | http://localhost:8000/docs | http://localhost:8000/docs |
| **AI Health** | http://localhost:8000/health | http://localhost:8000/health |
| **Database** | localhost:5432 | db:5432 (internal) |

---

## 12. Environment Notes

- **Machine:** ASUS ROG — ระวังปัญหาความร้อน / GPU Driver (nvlddmkm)
- **Private Registry:** Azure Artifacts (BetimesShare) — **ต้องต่อ VPN** ก่อน `npm install`
- **DB Password:** ตอนนี้ใช้ `your_password` ใน docker-compose (**เปลี่ยนก่อน production**)
- **SRID 4326:** ใช้ทั้งระบบ — อย่าใช้ coordinate system อื่น

---

> 📌 **อ่านเพิ่มเติม:**  
> - [AI-BLUEPRINT.md](./AI-BLUEPRINT.md) — AI Context Ledger ฉบับเต็ม  
> - [AI-CHANGELOG.md](./AI-CHANGELOG.md) — ประวัติการเปลี่ยนแปลง  
> - [docker-compose.yml](./docker-compose.yml) — Docker configuration
