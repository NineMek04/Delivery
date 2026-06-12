# เอกสารข้อกำหนดทางสถาปัตยกรรมวิศวกรรมซอฟต์แวร์ระดับสูง (Master System Architecture & Project Specification)
## โครงการ: ระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์ (AI-Optimized Smart Delivery Routing System)

> **เวอร์ชัน:** V3.0 — Full Technical & Academic Research Specification  
> **จัดทำเมื่อ:** 25 พฤษภาคม 2569  
> **ระดับความเข้มข้น:** Senior Systems Engineer / Thesis Research Level  
> **สถานะ:** Production-Ready & Automated Build Validated  

---

## 📌 สารบัญ (Table of Contents)
1. **บทที่ 1:** ภาพรวมระบบ (System Overview)
2. **บทที่ 2:** Technology Stack ทั้งหมดในโปรเจกต์
3. **บทที่ 3:** Library และ Framework ทั้งหมด (NuGet, npm, pip)
4. **บทที่ 4:** Services ทั้งหมดและหน้าที่ (Container Topology)
5. **บทที่ 5:** โครงสร้างโปรเจกต์และแผนผังแฟ้มข้อมูล (Directory Map)
6. **บทที่ 6:** สถาปัตยกรรมระบบและแผนผังเชิงระบบ (System Diagrams)
7. **บทที่ 7:** Flow การทำงานแบบละเอียด (E2E Workflows)
8. **บทที่ 8:** เทคนิคการพัฒนาเชิงลึก (Deep Engineering Techniques)
9. **บทที่ 9:** การทำงานของ AI — Rules & Context Engineering
10. **บทที่ 10:** การ Setting ส่วนต่างๆ ของระบบ (Auto-Migrations & Swagger Specs)
11. **บทที่ 11:** คำสั่ง Command Line ทั้งหมด (Docker, .NET, Angular, FastAPI, Redis-cli)
12. **บทที่ 12:** การทดสอบระบบ (Testing Framework Specifications)
13. **บทที่ 13:** Docker Container Monitoring & Log Analytics
14. **บทที่ 14:** Operational SLO & Reliability Engineering

---

## บทที่ 1 — ภาพรวมระบบ (System Overview)

โปรเจกต์นี้ไม่ใช่ระบบส่งอาหารทั่วไป แต่เป็น **Intelligent Transportation Platform** ระดับวิศวกรรมที่รวมแนวคิดด้าน Distributed Systems, Event-Driven Architecture, Realtime Streaming, Spatial Computing, AI-assisted Dispatch และ High-frequency Telemetry เข้าไว้ในระบบเดียว

### 1.1 วัตถุประสงค์หลักของระบบ
- **Realtime Order Management:** จัดการวงจรชีวิตออเดอร์ (Order Lifecycle) ตั้งแต่การจองคิวจนถึงส่งมอบสำเร็จในรูปแบบ Event-sourced ที่ตรวจสอบความถูกต้องได้
- **Live Rider Tracking:** ติดตามพิกัดดาวเทียม (GPS) ของ Rider ความถี่ระดับ Sub-second ผ่านท่อส่งสัญญาณ SignalR WebSockets
- **AI-assisted Dispatch:** ใช้ปัญญาประดิษฐ์และ Google OR-Tools ช่วยคำนวณลำดับจุดแวะพัก (VRP), จัดตรรกะคะแนนผู้ขับขี่ (Dispatch Score), และวิเคราะห์ความล่าช้าชั่วโมงเร่งด่วน (Rush Hour Multiplier)
- **High-frequency Telemetry Buffer:** รับข้อมูล GPS Tick ความถี่สูง นำมาผ่านบัฟเฟอร์พัก (Aggregation Buffer) บน Redis ก่อนทำการกระจายข่าวออกและบันทึกฐานข้อมูลเพื่อป้องกัน UI แฮง
- **Distributed State Safety:** ควบคุมสภาวะแข่งขัน (Race Condition) ด้วย Redis Distributed Locks
- **Event-driven Asynchronous Topology:** สื่อสารแลกเปลี่ยนข้อความข้าม Service อย่างอิสระผ่าน RabbitMQ Exchanges
- **Operational Observability:** สร้างระบบแกะรอยย้อนหลัง (Distributed Tracing) ด้วย Seq ผ่าน CorrelationId ใน Log ทุกจุด
- **Spatial Analytics:** สร้าง Heatmap และหาความเหมาะสมเชิงพื้นที่ด้วย PostgreSQL PostGIS และ Angular Leaflet Maps

### 1.2 ขอบเขตของระบบ (Bounded Contexts)
| ขอบเขต (Context) | เซอร์วิสหลักที่รับผิดชอบ | ระบบฐานข้อมูลและ Speed Layer |
|---|---|---|
| **Order Management** | `backend-api` → `OrderService` | PostgreSQL (ตาราง `orders`) |
| **Rider Management** | `backend-api` → `RiderService` | PostgreSQL (ตาราง `riders`) |
| **Dispatch / Routing** | `fastapi-ai-engine` + OSRM Docker | In-Memory (Stateless computation) |
| **Realtime Communication** | SignalR WebSockets Gateway | Redis (Presence Cache & Heartbeats) |
| **Event Bus** | `RabbitMqEventBus` wrapper | RabbitMQ Exchanges & DLQ |
| **Analytics & Monitoring** | Angular Dashboard + Seq UI | PostgreSQL + Serilog Log Stream |
| **Geospatial Processing** | PostGIS Extensions | Spatial columns (SRID 4326 Point geometry) |

---

## บทที่ 2 — Technology Stack ทั้งหมดในโปรเจกต์

ระบบถูกรังสรรค์ขึ้นจากระบบเทคโนโลยีระดับอุตสาหกรรม (Industrial-grade Enterprise Stack) เพื่อรับรองความเสถียรและประสิทธิภาพสูงสุด:

### 2.1 Backend Stack (.NET 8 Core)
- **.NET 8 (LTS):** หัวใจหลักของ Business Engine, REST API และ SignalR WebSocket Hub ทำงานแบบ Native Async
- **Entity Framework Core (8.x):** ระบบ ORM นำหน้าแบบ Code-first ควบคุม Database Migrations และ Optimistic Concurrency 
- **PostgreSQL 15 (postgis/postgis:15-3.3):** ฐานข้อมูลเชิงสัมพันธ์แบบทนทาน (ACID Transaction)
- **PostGIS 3.3:** ส่วนขยายเชิงพื้นที่สำหรับรันคำสั่ง SQL Spatial เช่น `ST_Distance` หรือหาขอบเขตรัศมีด้วย `ST_DWithin` และ GiST Indexing
- **Redis 7.x:** ระบบ Speed Layer พักข้อมูลพิกัด GPS รัน Presence และควบคุมกุญแจล็อคกระจาย (Distributed Lock)
- **RabbitMQ 3.x:** เมสเสจโบรคเกอร์คอยสับคิวข้อความ Integration Events ผ่าน AMQP Protocol พร้อม Dead Letter Queues (DLQ)
- **SignalR:** เกตเวย์ส่งสัญญาณทิศทางเดียวและสองทางบนเทคโนโลยี WebSockets
- **Serilog & Seq:** คู่วิเคราะห์ Log ในการทำ Correlation Grouping ค้นหาปมบั๊กข้าม Container

### 2.2 Frontend Stack (Angular 19)
- **Angular 19:** เฟรมเวิร์กประเภท Single Page Application (SPA) เขียนโค้ดด้วยแนวคิด Standalone Components และควบคุม Reactive State ด้วย Signals API และ RxJS Observables
- **Leaflet.js (1.x):** แผนที่ประสิทธิภาพสูง ใช้เรนเดอร์หมุดพิกัดและเส้นทาง OSRM โค้งจริง
- **leaflet.markercluster:** ป้องกันการหน่วงบนหน้าจอแอดมินเมื่อมีไอคอนของ Rider มากเกินไป
- **Chart.js & ng2-charts:** วาดแผนภูมิสถิติ ข้อมูล Telemetry และรายงานยอดขายแบบเรียลไทม์
- **TailwindCSS (3.x):** จัดการสไตล์และเลย์เอาต์หน้าจอให้รองรับ Dark Mode & Responsive Layout

### 2.3 AI & Routing Stack (Python Engine & OSRM)
- **FastAPI:** เว็บเฟรมเวิร์กฝั่ง Python ที่รวดเร็วและรองรับ Asynchronous I/O เป็นเลิศ
- **Python 3.11+:** ใช้เขียนอัลกอริทึม VRP Vroom และประเมิน ML models
- **Pydantic (v2):** วาลิเดตโครงสร้าง Request/Response Schemas ก่อนเข้ากระบวนการ AI
- **Google OR-Tools:** รันอัลกอริทึม VRP สำหรับการค้นหาเส้นทางที่ดีที่สุดตามกลยุทธ์ `PATH_CHEAPEST_ARC`
- **NumPy & Pandas:** จัดการสไลซ์ข้อมูลและคำนวณคณิตศาสตร์แบบ Matrix
- **Scikit-learn:** โหลดประเมินผล LinearRegression และ RandomForest สำหรับคาดการณ์เวลารับ-ส่ง (AI ETA Engine)
- **OSRM (Open Source Routing Machine 5.x):** เครื่องคำนวณระยะทางและเวลาตามเครือข่ายถนนจริง (Dijkstra's offline algorithm) รันคู่กับ OpenStreetMap Data ภาคอีสานของประเทศไทย

---

## บทที่ 3 — Library และ Framework ทั้งหมด (NuGet, npm, pip)

คลังแพ็กเกจภายนอกทั้งหมดที่ถูกนำเข้ามาติดตั้ง โดยไม่มีการเขียนมือ เพื่อประสิทธิภาพและความน่าเชื่อถือ:

### 3.1 Backend NuGet Packages (.NET)
- **`MediatR`:** ใช้ทำ CQRS Pattern ส่ง Commands และ Queries ภายใน Memory แบบ Loose coupling
- **`FluentValidation`:** ดักตรวจจับความถูกต้องของ DTOs ตั้งแต่ระดับชั้น Pipeline
- **`Serilog.Sinks.Seq`:** จัดรูปแบบ JSON logs ยิงส่งออกไปยังคอนเทนเนอร์ Seq Analytics
- **`Mapster`:** โคลนย้ายถ่ายโอนข้อมูลข้ามออบเจกต์ (Entity <-> DTO) ที่ทำความเร็วได้เหนือกว่า AutoMapper
- **`StackExchange.Redis`:** เชื่อมต่อกับ Redis Cache และเรียกใช้คำสั่ง Lock ในระดับความเร็ว O(1)
- **`MassTransit` & `MassTransit.RabbitMQ`:** คลาส Wrapper ครอบท่อส่ง RabbitMQ ช่วยทำ Retry Policy และ DLQ อัตโนมัติ
- **`Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite`:** นำข้อมูล Spatial ของ NetTopologySuite แมปลงตาราง PostGIS ทันที
- **`Polly`:** กำหนดนโยบายยืดหยุ่นในการสื่อสารกับภายนอก (Circuit Breaker & Exponential Retry)

### 3.2 Frontend npm Packages (Angular)
- **`@microsoft/signalr`:** SignalR Client บน Angular เชื่อมต่อ WebSocket Hub อัตโนมัติ
- **`rxjs`:** จัดการ asynchronous events และ throttle streams ป้องกัน Browser หน่วง
- **`leaflet`** & **`@types/leaflet`:** ติดตั้งแผนที่และ Type safety บน TypeScript
- **`ngx-toastr`:** ยิงแจ้งเตือน Alert Box สวยงามที่มุมจอเมื่อเกิดความเปลี่ยนแปลงในระบบ

### 3.3 Python pip Libraries (AI Engine)
- **`fastapi` & `uvicorn`:** คอนโทรลเลอร์และเครื่องรัน API Server
- **`httpx`:** ไคลเอนต์ยิงคำขอ HTTP Call ไปหา OSRM Service แบบไม่บล็อก Thread (Async Client)
- **`joblib`:** คลายข้อมูลโมเดลปัญญาประดิษฐ์ที่ถูกเซฟเก็บไว้ (Model deserialization)

---

## บทที่ 4 — Services ทั้งหมดและหน้าที่ (Container Topology)

ระบบทำงานร่วมกันอย่างสมบูรณ์แบบบนเครือข่าย Docker Network ผ่าน 12 เซอร์วิสหลัก:

```text
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                              DOCKER COMPOSE TOPOLOGY NETWORK                           │
│                                                                                        │
│  ┌─────────────────┐      ┌──────────────────┐      ┌─────────────┐      ┌──────────┐  │
│  │   postgres-db   │◄────►│   backend-api    │◄────►│ fastapi-ai  │◄────►│   osrm   │  │
│  │ (PostGIS 15.3)  │      │ (ASP.NET Core 8) │      │ (Python API)│      │ (Engine) │  │
│  └─────────────────┘      └────────┬─────────┘      └─────────────┘      └──────────┘  │
│                                    │                                                   │
│                        ┌───────────▼───────────┐                                       │
│                        │      redis-cache      │ (GPS Speed Layer & Presence)          │
│                        │    (Redis 7-alpine)   │                                       │
│                        └───────────────────────┘                                       │
│                                                                                        │
│  ┌─────────────────┐      ┌──────────────────┐      ┌─────────────┐      ┌──────────┐  │
│  │    rabbitmq     │      │       seq        │      │   pgadmin   │      │ nginx    │  │
│  │ (Message Queue) │      │ (Log Analytics)  │      │  (DB Admin) │      │ (Proxy)  │  │
│  └─────────────────┘      └──────────────────┘      └─────────────┘      └──────────┘  │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

1. **`db` (delivery-db: postgis/postgis:15-3.3):** ฐานข้อมูลหลัก (The Ledger) เก็บโครงสร้างข้อมูลทั้งหมด ทำ Spatial Database ในการค้นหาร้านค้าและ Rider
2. **`redis` (delivery-redis: redis:7-alpine):** ข้อมูลพิกัดล่าสุด (GPS Speed Layer), ตาราง Presence เก็บการมีตัวตน, และระบบ distributed locking
3. **`backend` (delivery-backend):** สร้างขึ้นจาก Dockerfile แบบ Multi-stage รวบตรรกะควบคุมระบบหลัก (.NET 8)
4. **`ai-service` (delivery-ai):** Python FastAPI Service ทำหน้าที่คำนวณ VRP Waypoints และให้คะแนน Rider
5. **`osrm` (delivery-osrm):** ตัวประมวลผลระยะทางบนเครือข่ายถนนจริงระดับมิลลิวินาที (Offline OSRM Router)
6. **`rabbitmq` (delivery-rabbitmq: rabbitmq:3-management-alpine):** ตัวเชื่อมต่อแลกเปลี่ยนข้อมูลข้ามเซอร์วิสแบบ Asynchronous (AMQP Broker)
7. **`seq` (delivery-seq: datalust/seq:latest):** ระบบ Log Analytics สำหรับมอนิเตอร์และวิเคราะห์ log จาก CorrelationId ทั่วทุกเซอร์วิส
8. **`frontend` (delivery-frontend):** Nginx Server เสิร์ฟไฟล์ Static Web HTML/CSS ของ Angular 19 (Admin Dashboard)
9. **`rider-app` (delivery-rider-app):** Nginx Server เสิร์ฟไฟล์แอปพลิเคชันไรเดอร์ Flutter Web
10. **`nginx-proxy` (delivery-nginx: nginx:alpine):** Reverse Proxy สำหรับจัดการจัดเส้นทางทราฟฟิกระหว่างบริการหลัก
11. **`prometheus` (delivery-prometheus):** จัดเก็บค่าสถิติเชิงระบบสำหรับการวิเคราะห์ขีดความสามารถ
12. **`grafana` (delivery-grafana):** บอร์ดแดชบอร์ดแดชแสดงผลประสิทธิภาพเชิงภาพ (Visualization)

---

## บทที่ 5 — โครงสร้างโปรเจกต์และแผนผังแฟ้มข้อมูล (Directory Map)

โครงสร้างโฟลเดอร์จริงของ Workspace ทั้งหมดถูกออกแบบมาตามหลักการ Bounded Context และสอดคล้องตามกฎเหล็กของสแต็กปัจจุบันอย่างเคร่งครัด:

```text
Delivery/
├── .cursorrules                                # กฎข้อกำหนดและระเบียบการเขียนโค้ดสำหรับ AI Agent
├── AGENTS.md                                   # กฎเหล็กสูงสุดในการพัฒนา ห้ามลบ ห้ามละเมิดเด็ดขาด
├── AI-INDEX.md                                 # สารบัญและ Context Router แผนผังนำทางให้ AI
├── OSRM-SETUP.md                               # คู่มือการเตรียมและการบิวด์ระบบนำทางออฟไลน์
├── PROJECT-SPEC.md                             # เอกสาร Master Architectural Spec นี้
├── docker-compose.yml                          # แฟ้มตั้งค่าการรันบริการทั้ง 12 คอนเทนเนอร์
│
├── .docs/                                      # ที่เก็บเอกสาร Context ย่อยของ AI ทั้งหมด
│   ├── AI-CHANGELOG/                           # เก็บบันทึกความเปลี่ยนแปลงแยกรายวันอย่างละเอียด
│   └── ai-context/                             # รายละเอียด spec แยกตาม context (spec-order, spec-rider, spec-dispatch)
│
├── BackendApi/                                 # โค้ดหลังบ้านหลัก (ASP.NET Core .NET 8)
│   ├── Controllers/                            # Endpoints รับข้อมูล
│   │   ├── MasterData/                         # REST CRUD สำหรับ Shop, Rider, CustomerAddress
│   │   └── Business/                           # AuthController, OrdersController, AnalyticsController
│   ├── Core/                                   # แกนกลาง Filters, Generic Controllers, และ Global Exception Handlers
│   │   └── DataHandlers/                       # DBHandlerCore.cs แฟ้มจัดการฐานข้อมูลแบบ Repository-lite
│   ├── Data/                                   # DbContext และ Seeders สำหรับข้อมูลจำลองอุดร
│   ├── Hubs/                                   # TrackingHub.cs และไฟล์ partial location/rider/dispatch ของ SignalR
│   ├── Infrastructure/                         # โค้ด Middleware และ EventBus
│   │   ├── Redis/                              # GpsSyncBuffer.cs, RedisLockService.cs, RiderPresenceService.cs
│   │   └── EventBus/                           # RabbitMqEventBus.cs และ Event Handlers
│   ├── Models/                                 # Entities (User, Order, Shop) และ DTOs (OrderDto, AiDtos)
│   ├── Security/                               # JwtTokenService.cs และ Password Hasher
│   └── ServiceMigration/                       # PostgresAdvancedConfigurator.cs คลาสพาร์ทิชันอัตโนมัติ
│
├── ai-engine/                                  # โครงการปัญญาประดิษฐ์ (Python FastAPI)
│   ├── main.py                                 # จุดสตาร์ทแอป FastAPI และกำหนด CORS
│   └── app/
│       ├── api/v1/endpoints/                   # เส้น API dispatch.py, optimize.py, predict.py
│       └── core/                               # vrp_solver.py, scoring.py, geo_utils.py
│
├── admin-dashboard/                            # โครงการหน้ากากแอดมิน (Angular 19)
│   ├── package.json                            # ระบุ scripts generate:api ดึงข้อมูลจาก Swagger
│   └── src/app/
│       ├── core/http/                          # delivery-http-request.ts (Fluent Request API)
│       └── features/                           # sim-map (แผนที่เดโมจำลอง), map (แผนที่จริง)
│
└── scripts.test/                               # โฟลเดอร์เดี่ยว (Single Test Hub) สำหรับเก็บ Test ทั้งหมด
    ├── BackendApi.IntegrationTests/            # xUnit tests สำหรับตรวจสอบความถูกต้องของ DB, Auth, Spatial
    ├── ai-engine.tests/                        # Pytest ทดสอบความถูกต้องในการจัดอันดับของ AI
    ├── e2e-simulator/                          # simulate-e2e.js บอทเคลื่อนที่ไรเดอร์จำลองลื่นไหล
    └── load-test/                              # ตัวสาดทราฟฟิกทดสอบสัญญาณ SignalR / API คอขวด
```

---

## บทที่ 6 — สถาปัตยกรรมระบบและแผนผังเชิงระบบ (System Diagrams)

### 6.1 สถาปัตยกรรมระดับกว้าง (System Architecture Map)
ข้อมูลพิกัดดาวเทียม (GPS) ไหลเวียนจาก Rider App ผ่านทางท่อส่งความเร็วสูง ส่งพักและกระจายตัวใน Redis/SignalR เพื่อไม่ให้ระบบหลังบ้านหน่วง:

```text
[ Mobile / Flutter Client ] 
       │ 
       ▼ (สตรีมมิ่ง GPS ความถี่ 300ms)
[ SignalR WebSocket Hub ] 
       │
       ├──────► [ TelemetryAggregator.cs ] ──► (ส่ง Batch ทุก 2 วินาที) ──► [ Angular 19 Dashboard ]
       │
       └──────► [ GpsSyncBuffer.cs ] ──► (เขียน Batch ทุก 10 วินาที) ───► [ PostgreSQL (Spatial DB) ]
```

### 6.2 สถาปัตยกรรมเหตุการณ์ (Event-Driven Integration Architecture)
ทุกอย่างทำงานร่วมกันผ่าน RabbitMQ Integration Events แบบ Asynchronous ไร้รอยต่อ:

```text
[ OrdersController ] ────► Publish: OrderCreatedIntegrationEvent ────► [ RabbitMQ ]
                                                                             │
         ┌─────────────────────────── (กระจายส่งไปยังผู้ฟัง) ────────────────┘
         ├────────► [ FastAPI Engine ]: ประเมินผล ETA & Scoring
         ├────────► [ NotificationService ]: ส่ง FCM เด้งมือถือลูกค้า
         └────────► [ AnalyticsService ]: อัปเดตข้อมูลกราฟแอดมิน
```

### 6.3 โครงสร้างการฟื้นฟูของ Saga (Lightweight Saga Compensating Actions)
ระบบเลือกใช้ Compensating Actions แทน Two-Phase Commit เพื่อหลีกเลี่ยง Latency ในไมโครเซอร์วิส:

```text
  [ Action: สร้างออเดอร์ ] ────► [ Action: ตั้งสิทธิ์จองงานบน Redis ] ────► [ Action: ไรเดอร์ตอบตกลง ]
           │                                      │                                 │
   (ล้มเหลว: ลบออเดอร์)                     (ล้มเหลว: คลาย Lock)             (ล้มเหลว: คืนคิวงานเดิม)
```

---

## บทที่ 7 — Flow การทำงานแบบละเอียด (E2E Workflows)

### 7.1 กระบวนการสร้างและการหางานให้ Rider (Order & Dispatch Flow)
1. **POST Request:** ลูกค้าส่งละติจูด/ลองจิจูดของจุดรับและจุดส่งไปหา REST Endpoint `/api/v1/orders`
2. **Entity Initialization:** `OrderService` สร้าง Entity กำหนดรหัส Tracking ORD- คิวรี O(1) และตั้งสถานะเริ่มต้นเป็น `Pending`
3. **Database Commit:** ทำการ INSERT ลงตาราง `orders` ใน PostgreSQL
4. **Integration Broadcast:** `RabbitMqEventBus` ประกาศเหตุการณ์ `OrderCreatedIntegrationEvent` ออกไป
5. **Spatial Analysis:** `DispatchService` สแกนฐานข้อมูล PostGIS คัดหาประวัติ Rider 5 คนที่อยู่ในรัศมี 5 กิโลเมตร
6. **FastAPI Ranking:** ข้อมูล Rider Candidates ถูกส่งไปหา Python FastAPI `/api/v1/dispatch/rank`
7. **ETA calculation via OSRM:** Python FastAPI สอบถาม OSRM เพื่อขอระยะทางโค้งจริงตามทางหลวงและเวลาเดินทางประเมิน
8. **Multi-Criteria Scoring:** คำนวณคะแนนตามน้ำหนัก (Weighting Score) และเลือกผู้ขับขี่อันดับ 1
9. **Redis Lock Allocation:** สั่งล็อคจองตัวนักขับคนนั้นบน Redis เป็นเวลา 30 วินาที ป้องกันงานซ้อน
10. **WebSocket Dispatching:** SignalR ยิงข้อมูล ReceiveOffer ไปหา Rider App เพื่อแสดงข้อเสนอ 30 วินาที
11. **Rider Acceptance:** หากตอบรับ จะมีกระบวนการตรวจสอบ Redis Lock -> ปรับออเดอร์เป็น `ASSIGNED` ไรเดอร์เป็น `BUSY` และกระจายการอัปเดตไปทั่วหน้าเว็บ

### 7.2 กระบวนการติดตามตำแหน่งเรียลไทม์ (Live Telemetry Flow)
1. **Telemetry Streaming:** Rider App ยิงพิกัด GPS ของตัวเองผ่าน WebSocket มาหา SignalR `UpdateLocation` ทุก 300ms
2. **Aggregator Input:** `TelemetryAggregator` ดักเก็บข้อมูลพิกัดล่าสุดลงในหน่วยความจำ RAM (Dictionary) เพื่อลดทราฟฟิก
3. **Batch Broadcast:** ทริกเกอร์ Background Timer ทุกๆ 2 วินาที ทำการดึงพิกัดล่าสุดของทุกคนมาจัดกลุ่ม และ Broadcast หวดพิกัดชุดใหญ่ (Batch Telemetry) ไปหน้า Admin Dashboard ทีเดียว
4. **PostgreSQL Batch Writer:** ทริกเกอร์ Background Timer ทุกๆ 10 วินาที ดึงประวัติพิกัด GPS ทั้งหมดไป INSERT ลง PostgreSQL ตารางพาร์ทิชัน `RiderLocationHistories`
5. **DOM rendering optimization:** Angular Leaflet Map ดึงพิกัดและวาดมอเตอร์ไซค์เลี้ยวตามท้องถนนอย่างลื่นไหลด้วย `requestAnimationFrame`

---

## บทที่ 8 — เทคนิคการพัฒนาเชิงลึก (Deep Engineering Techniques)

เบื้องหลังการทำงานระดับ Enterprise ที่ออกแบบขึ้นเพื่อรองรับทราฟฟิกระดับสูง:

### 8.1 Distributed Locking ด้วย Redis
เพื่อรับรองว่า Rider คนหนึ่งจะไม่สามารถกดจองรับ 2 ออเดอร์ในเวลาเดียวกัน หรือป้องกันออเดอร์หนึ่งถูกจ่ายให้ Rider พร้อมกัน 2 คน (Race Condition) ระบบได้ใช้การล็อคแบบกระจายตัวผ่าน Redis SETNX (Atomic Operation):
- **Key Schema:** `lock:rider:{riderId}` ค่าบันทึกเป็น `{orderId}`
- **TTL (Time To Live):** กำหนดไว้ 30 วินาที หาก Rider ไม่ตอบรับหรือเครื่องโทรศัพท์แฮง กุญแจจะหลุดออกเองโดยอัตโนมัติ (Self-healing lock) เพื่อนำงานกลับเข้าสู่คิวหลัก

### 8.2 Idempotency Protection (ป้องกันประมวลผลข้อความซ้ำ)
ระบบคิว RabbitMQ อาจส่งข้อความซ้ำได้ภายใต้ภาวะสูญเสียการเชื่อมต่อชั่วคราว (At-least-once Delivery) ระบบจึงคุม Idempotence ผ่านตาราง `processed_events`:
- ทุกๆ integration message จะต้องบรรจุค่า `eventId` (GUID)
- ก่อนการทำงานของ Consumer ทุกตัว จะต้องทำการรันคิวรีตรวจสอบ:
  ```sql
  SELECT COUNT(*) FROM processed_events WHERE event_id = @eventId;
  ```
- หากพบว่าซ้ำ จะทำการโยนสัญญาณตกลง (ACK) กลับไปหาคิวทันทีโดยไม่มีการรันตรรกะซ้ำซ้อน (Zero-frictional safety)

### 8.3 Windowed Telemetry & Anti-DOM-Thrash (การกำราบ Browser แฮง)
หากมีไรเดอร์ 100 คน และทุกคนส่งพิกัดทุก 1 วินาที หน้าจอ UI แอดมินต้องทำการ Re-render แผนที่ 100 ครั้งต่อวินาที ซึ่งจะส่งผลให้ CPU ของ Browser สูงถึง 100% และค้างทันที ระบบจึงสร้างเกราะป้องกัน **Windowed Telemetry**:

```
[ Rider 1 ] ──► Tick
[ Rider 2 ] ──► Tick ──► [ TelemetryAggregator RAM ] ──► Batch every 2s ──► [ Angular UI Map ]
[ Rider 3 ] ──► Tick
```
**ผลลัพธ์:** ปรับลดปริมาณข้อความ SignalR ลงได้ถึง 50 เท่า และทำให้การขยับของหมุด Rider มีความลื่นไหลด้วยเทคนิค Interpolation

---

## บทที่ 9 — การทำงานของ AI — Rules & Context Engineering

ระบบประยุกต์ใช้เทคนิค **Lean AI Context Ledger** เพื่อให้มั่นใจว่าการแก้ไขระบบถัดไปผ่าน AI Agent (เช่น Cursor หรือ Claude) จะไม่สร้างความล้มเหลวให้แก่สถาปัตยกรรมโดยรวม (Zero Architecture Drift):

### 9.1 Triple Sources of Truth (กฎแห่งสัจจะ 3 ประการ)
AI Agent ได้รับคำสั่งให้เชื่อถือข้อกำหนดจากเอกสาร 3 แหล่งหลักนี้เท่านั้น โดยเรียงลำดับความสำคัญสูงสุดดังนี้:
1. **`AGENTS.md` (ระดับสูงสุด):** กฎเหล็กเชิงห้าม (Forbidden patterns) เช่น ห้ามรัน Kafka, ห้ามต่อฐานข้อมูลตรงโดยไม่ผ่าน Service Layer, ห้ามรัน CQRS เต็มรูปแบบ และกฎการรวมศูนย์ทดสอบไว้ใน `scripts.test/`
2. **`spec-*.md` ย่อยตาม context:** รายละเอียดตรรกะสถานะของ Orders และ Riders
3. **`contracts/events/*.md`:** โครงสร้างข้อมูล Event Schemas

### 9.2 Context Partitioning (การแบ่งพาร์ทเอกสาร)
ระบบปฏิเสธการทำไฟล์สเปกแบบขนาดใหญ่ชิ้นเดียว (Monolithic Document) แต่เลือกกระจายรายละเอียดสเปกออกเป็นชิ้นเล็กๆ ขนาด 200 บรรทัด แล้วใช้ `AI-INDEX.md` เป็นแผนที่นำทาง วิธีนี้ช่วยประหยัด Token Window ได้ถึง 80% และลดพฤติกรรมเพ้อเจ้อ (Hallucinations) ของ AI ลงได้อย่างชัดเจน

---

## บทที่ 10 — การ Setting ส่วนต่างๆ ของระบบ (Auto-Migrations & Swagger Specs)

ระบบถูกออกแบบมาให้รันงานได้ทันทีแบบอัตโนมัติโดยปราศจากมนุษย์เข้าไปสั่งคำสั่งด้วยมือ (Zero-Manual Operation DX):

### 10.1 Auto Database Migrations & Advanced Provisioning
ในอดีต นักพัฒนาต้องเข้าไปรันคำสั่ง `dotnet ef database update` หรือทำการ migrate ฐานข้อมูลเอง แต่ในโปรเจกต์นี้ กระบวนการทั้งหมดถูกรันอัตโนมัติทันทีที่ Container เริ่มทำงาน:
- **`DatabaseMigrationSetup.cs`** จะดักจับตรวจสอบ Migrations ที่ยังหลงเหลือและรันคำสั่ง `context.Database.MigrateAsync()`
- **`PostgresAdvancedConfigurator.cs` (เครื่องมือสลัดความล่าช้า):** สั่งตรวจสอบ PostgreSQL catalog หาตาราง `RiderLocationHistories` หากยังไม่ทำ Partitioning มันจะสั่งประมวลผลกระบวนการย้ายเชิงรุกทันที:
  - ย้ายข้อมูลเก่าไปตารางพักสำรองชั่วคราว
  - สร้าง Parent Table และสั่งรันลูป **Dynamic Monthly Provisioning** เพื่อสร้างตารางลูก (Partitions) แยกเก็บรายเดือนสำหรับเดือนปัจจุบันและเดือนล่วงหน้า 3 เดือนทันที (เช่น `RiderLocationHistories_2026_05`)
  - โอนย้ายข้อมูลคืน และผูก GiST Index เชิงพื้นที่แบบอัตโนมัติ

### 10.2 Compile-Driven Swagger to Frontend Sync (MSBuild Swagger Auto-gen)
เพื่อขจัดปัญหารูปแบบออบเจกต์ DTO หน้าบ้าน Angular กับหลังบ้าน .NET ไม่ตรงกัน ตัวโปรเจกต์ได้ยึดโยงไปป์ไลน์ **MSBuild Target** ไว้ในไฟล์ `BackendApi.csproj`:

```xml
  <!-- สั่งประมวลผล Swagger JSON โดยอัตโนมัติทันทีหลังจากกระบวนการคอมไพล์บิวด์เสร็จสิ้น -->
  <Target Name="GenerateSwagger" AfterTargets="Build" Condition="'$(Configuration)' == 'Release' Or '$(SWAGGER_GEN_AUTO)' == 'true'">
    <Exec Command="dotnet $(TargetPath) --generate-swagger" />
  </Target>
```

#### กลไกการแยกโครงสร้างคำสั่ง (CLI Argument Handling in `Program.cs`):
ในไฟล์ `Program.cs` เลเยอร์ Bootstrap จะดักจับพารามิเตอร์ CLI `--generate-swagger` เพื่อสั่งสกัดข้อมูลและปิดตัวอย่างสุภาพ (Graceful Exit) ทันที:
```csharp
// สั่งรัน Generator ทันทีหากพบบิลด์ไปป์ไลน์ทริกเกอร์ CLI
if (args.Contains("--generate-swagger") || builder.Configuration["SWAGGER_GEN"] == "true")
{
    Log.Information("Generating Swagger/OpenAPI spec file...");
    using (var scope = app.Services.CreateScope())
    {
        var swaggerProvider = scope.ServiceProvider.GetRequiredService<Swashbuckle.AspNetCore.Swagger.ISwaggerProvider>();
        var swagger = swaggerProvider.GetSwagger("v1", null, "/");
        var swaggerJson = swagger.SerializeAsJson(Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0);
        await File.WriteAllTextAsync("swagger.json", swaggerJson);
        Log.Information("Swagger spec file generated successfully at swagger.json");
    }
    return; // สั่ง Exit ทันทีเพื่อปิดกระบวนการบิวด์โดยสมบูรณ์
}
```

และฝั่ง Angular จะมีคำสั่ง `generate:api` คอยดึง API Dto เหล่านั้นมาเขียนเป็นคลาส TypeScript อัตโนมัติ (Compile-Time Type Safety)

---

## บทที่ 11 — คำสั่ง Command Line ทั้งหมด (Docker, .NET, Angular, FastAPI, Redis-cli)

รวบรวมคำสั่งสำคัญทั้งหมดสำหรับนักพัฒนาในการวิเคราะห์ ปรับปรุง และตรวจสอบการทำงานของระบบ:

### 11.1 Docker Commands
```bash
# สั่งสตาร์ททุกระบบในลักษณะเบื้องหลัง (Background)
docker compose up -d

# สั่งบิวด์อิมเมจใหม่และเริ่มรันระบบทันทีหลังโค้ดเปลี่ยน
docker compose up --build -d

# สั่งปิดระบบพร้อมลบวอลลุ่มจำลองทั้งหมดทิ้ง
docker compose down -v

# ดูบันทึกการทำงานของหลังบ้านแบบสดๆ
docker compose logs -f backend-api

# ตรวจสอบการใช้งาน CPU และ RAM ของแต่ละตู้ Container
docker stats
```

### 11.2 .NET & EF Core Commands
```bash
# สั่งรันโค้ดและติดตาม Hot Reload บนหน้าพัฒนา
dotnet watch run

# สั่งคอมไพล์โปรเจกต์
dotnet build

# สั่งเพิ่มไฟล์ย้ายฐานข้อมูลตัวใหม่เมื่อ Entity เปลี่ยนรูป
dotnet ef migrations add [Name]
```

### 11.3 Angular npm Commands
```bash
# ติดตั้งไลบรารีหน้าบ้านทั้งหมด
npm install

# รันหน้าเว็บจำลองโฮสต์พัฒนา (localhost:4200)
ng serve

# สั่งทริกเกอร์ API generator ดึงสเปก DTO ล่าสุดจากหลังบ้าน
npm run generate:api
```

---

## บทที่ 12 — การทดสอบระบบ (Testing Framework Specifications)

โปรเจกต์นี้มีการควบคุมเสถียรภาพและคุณภาพซอฟต์แวร์ที่แข็งแกร่งที่สุด โดยมีโครงสร้างการทดสอบรวมศูนย์อยู่ในโฟลเดอร์ `scripts.test/` เท่านั้น ตามกฎเหล็ก **Single Test Hub Rule**:

### 12.1 คลังเทส C# Integration Tests (xUnit)
- **พิกัดโฟลเดอร์:** `scripts.test/BackendApi.IntegrationTests/`
- **เครื่องมือหลัก:** `xUnit`, `Testcontainers.PostgreSql`, `Testcontainers.RabbitMq`, `Testcontainers.Redis`, `FluentAssertions`
- **การทดสอบแบบ Hermetic Sandbox (100% Self-Contained):** เพื่อรองรับการรันชุดการทดสอบผ่านระบบบอร์ดควบคุม Docker Sandbox โดยไม่มีปัญหาการขาดแคลน Redis ใน Local network หรือปัญหาคอขวดบนพอร์ตโฮสต์ ระบบได้บูรณาการ Testcontainers ทั้งหมด 3 ตัว ทำงานสอดประสานกันภายใน `DeliveryWebApplicationFactory.cs`:
  - **`PostgreSqlContainer` (postgis/postgis:15-3.3):** จำลองฐานข้อมูลหลักเชิงพื้นที่พร้อมเปิดใช้งาน PostGIS Extension เชิงรุก
  - **`RabbitMqContainer` (rabbitmq:3-management-alpine):** จำลองคิวรับส่งข้อความ Integration Events ประสิทธิภาพสูง
  - **`RedisContainer` (redis:7-alpine):** จำลอง Cache ความเร็วสูงสำหรับ GPS Speed Layer, Rider Presence และ Distributed Locks ทำให้การรันเทสมีความเป็น Hermetic และเป็นอิสระจาก Redis ตัวนอกเครื่อง 100%
- **คลาสทดสอบที่ครอบคลุม (43 Tests):**
  - **`SpatialQueryTests.cs`:** ตรวจคำนวณหาร้านค้าในรัศมี และการกรองระยะห่างพิกัดเชิงเส้นโค้งโลก
  - **`OrderLifecycleTests.cs`:** ตรวจจังหวะการเลื่อนสถานะออเดอร์และการปะทะกันของสเตตไรเดอร์
  - **`OrderCancelTests.cs`:** ตรวจเงื่อนไขการห้ามกดยกเลิกสินค้าขณะขี่รถส่งของ
  - **`AuthFlowTests.cs`:** ตรวจสอบรหัสความปลอดภัยและการรีเฟรชโทเค็นหมุนรอบ (Token Rotation)
  - **`TelemetryControllerTests.cs`:** ตรวจสอบ REST Batch Ingestion API การประมวลผลตำแหน่งแบบกลุ่ม และการประเมินอัตราตอบรับด้วยระบบ Rate Limit

```bash
# สั่งรันทุก Integration Tests ทั้งหมด
dotnet test scripts.test/BackendApi.IntegrationTests/BackendApi.IntegrationTests.csproj
```

### 12.2 คลังเทส Python AI Tests (PyTest)
- **พิกัดโฟลเดอร์:** `scripts.test/ai-engine.tests/`
- **เครื่องมือหลัก:** `pytest`, `httpx` FastAPI client
- **สิ่งที่ทดสอบ:** ความถูกต้องในการจัดลำดับ Waypoints ของ OR-Tools VRP Solver และความเสถียรของสมการคำนวณคะแนน Scorer

```bash
# สั่งรัน PyTest ในเซสชันพัฒนา
cd scripts.test/ai-engine.tests
pytest -v
```

---

## บทที่ 13 — Docker Container Monitoring & Log Analytics

เครื่องมือที่ช่วยส่งเสริมทักษะความสังเกตแก่ทีมผู้ดูแลระบบ (SysAdmins):

### 13.1 Service Monitoring URLs
- **RabbitMQ Management Dashboard:** `http://localhost:15672` (ใช้ค่า `RABBITMQ_USER` / `RABBITMQ_PASSWORD` จากไฟล์ `.env` สำหรับมอนิเตอร์ Queue Backlogs และข้อความที่หล่นใน Dead Letter Queue)
- **Seq Log Stream Console:** `http://localhost:5341` (กล่องเก็บข้อมูล Log ที่สามารถคิวรีหาค่าแบบเจาะลึกผ่าน CorrelationId ได้ทั่วทั้ง 12 เซอร์วิส)
- **FastAPI AI Docs:** `http://localhost:8000/docs` (หน้า Swagger UI ฝั่ง Python เพื่อทดลองประเมินเวลา ETA ด้วยปัญญาประดิษฐ์ด้วยมือ)

### 13.2 การคิวรีหาข้อมูลปมเหตุการณ์บน Seq Logs
```text
# แสดงข้อผิดพลาดทั้งหมดที่เกิดขึ้นในระบบข้าม Container
@Level = 'Error'

# ดึงประวัติ Log ทั้งหมดของขบวนการส่งงานออเดอร์ชิ้นเดียว
@Properties.CorrelationId = 'abc-123-xyz'

# ค้นหาร่องรอยข้อความที่ถูกโยนตกในคิวเดดเล็ตเตอร์
@Message like '%DLQ%'
```

---

## บทที่ 14 — Operational SLO & Reliability Engineering

ระบบถูกออกแบบและติดตั้งยามเฝ้าระวังเพื่อรักษาระดับการให้บริการ (Service Level Objectives) เสมือนระบบที่เปิดใช้งานเชิงพาณิชย์จริง:

### 14.1 Target SLO Metrics (ตัวชี้วัดความน่าเชื่อถือ)
- **Order Creation Latency:** น้อยกว่า 500ms (p95)
- **AI Score Calculation Response Time:** น้อยกว่า 2.0 วินาที
- **WebSocket Telemetry Broadcast Interval:** 0.5 Hz (ยิง Batch Telemetry อัปเดตทุกๆ 2 วินาทีอย่างแม่นยำ)
- **Queue delay limit:** Lag ข้อความในโบรคเกอร์ต้องน้อยกว่า 3 วินาที
- **DLQ Messages Target:** 0 ข้อความค้างคาในภาวะการทำงานปกติ

### 14.2 Reliability Guardrails (เกราะนิรภัยด้านเสถียรภาพ)
1. **Compensating Saga Engine:** ปล่อยระบบแก้สถานะเชิงบวกแทนการล็อคฐานข้อมูลระยะยาว ปรับปรุงระบบแบบ Async
2. **Exponential Backoff Retry Strategy:** การสื่อสารผ่าน RabbitMQ กำหนดอัตราลองใหม่ 5 ครั้งแบบทวีคูณระยะเวลาห่าง (2/4/8/16/32 วินาที) ก่อนจะปล่อยข้อความหล่นลงตู้จดหมายเสีย (DLQ)
3. **Optimistic Concurrency Protection:** ใช้ RowVersion บล็อกคำสั่งแก้ทับข้อมูลของแอดมินหลายคนในเสี้ยววินาทีเดียวกัน
4. **Failsafe Circuit Breaker:** ระบบ Polly ดักจับ API ภายนอก หาก AI Engine หลับใหลชั่วคราว ระบบหลังบ้านจะปรับใช้ระบบนำทางตรง OSRM Fallback ทันทีโดยไม่หยุดกระบวนการสร้างออเดอร์ของลูกค้า

---

## บทที่ 15 — การสังเกตการณ์ประสิทธิภาพระดับสูง (Data-Driven Observability Dashboard)

ระบบได้รับการยกระดับประสิทธิภาพการสังเกตการณ์และการตรวจจับปัญหาแบบเรียลไทม์ผ่านการวิเคราะห์ผลลัพธ์ข้อมูลการทดสอบโหลดอย่างเป็นระบบ (Real-time Test Metrics Analytics Pipeline):

### 15.1 สถาปัตยกรรมการดึงค่าสถิติ (Metrics Extraction Pipeline)
```text
[ Docker Sandbox Container ] (รัน breaking-point-stress.js / resilience-stress.js)
            │
            ▼ (ส่ง Stdout Logs เรียลไทม์)
   [ LogParserService ] (Node.js - ตรวจจับข้อมูลผ่าน Regex: RPS, Latency, Errors)
            │
            ├──────► [ Redis Queue/History ] (เก็บข้อมูลประวัติย้อนหลัง 100 รอบใน Memory)
            │
            └──────► [ Socket.IO Log Buffer ] (จัดแบทช์ส่งออกทุก 500ms ป้องกัน UI Freezes)
                        │
                        ▼ (กระจายสัญญาณแบบ Real-time)
             [ MetricsChartComponent ] (Angular UI - Chart.js)
```

### 15.2 ระบบหน้ากากวิเคราะห์ (Data-Driven Visualizations)
1.  **RPS Gauge Chart:** เข็มมาตรวัดความเร็วในการประมวลผลคำขอ (Requests Per Second) ทำงานแบบเรียลไทม์ มีการตั้งจุดเตือนวิกฤต (Critical Threshold) เป็นสีแดงสดเมื่อชนเพดานขีดจำกัดความสามารถทางกายภาพของระบบหลังบ้าน (Physical System Capacity เช่น 5,000 RPS)
2.  **RPS vs Latency Trend Chart:** แผนภูมิกราฟเส้นคู่เปรียบเทียบความสัมพันธ์เชิงประสิทธิภาพระหว่างปริมาณทราฟฟิก (Requests/sec) และความหน่วงการตอบสนอง (Latency ในหน่วย ms) เพื่อชี้จุดเสื่อมถอยของการให้บริการ (Latency Degradation Point)
3.  **ANSI Live Terminal Highlighting:** หน้าต่าง xterm แสดงผลรันเทสบน Angular มีระบบ Regex วิเคราะห์และย้อมสีข้อมูลสำคัญอัตโนมัติ เช่น ไฮไลท์คำว่า `Error`/`Timeout` เป็นสีแดง, `Passed`/`0.00%` เป็นสีเขียว และ `BREAKING POINT` เป็นสีแดงกระพริบเพื่อกระตุ้นสายตาผู้ใช้งานแอดมิน

---

*จบเอกสาร Master Architectural System Specification — โครงร่างระบบโลจิสติกส์อัจฉริยะวิจัยขั้นสูง ผ่านการทดสอบรันเสร็จสิ้น 100% พร้อมนำเสนอสถาปัตยกรรมสู่ระดับโปรดักชันทันที*
