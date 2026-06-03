# CRITICAL-CODE-PROTECTION.md — Critical Code Registry

> **Version:** 1.0.0 | **Last Updated:** 2026-06-03  
> **Purpose:** Living Document ระบุไฟล์/ฟังก์ชันที่ห้ามลบ ห้ามคอมเม้นต์ หรือเปลี่ยนแปลง contract โดยไม่มีเหตุผลที่ถูกต้อง  
> **Rules:** ดูกฎป้องกันใน [`AGENTS.md §7`](AGENTS.md)

---

## Tier System

| Tier | ระดับ | ผลลัพธ์ถ้าพัง | กฎ |
|------|-------|--------------|-----|
| 🔴 **TIER-1** | System-Critical | ระบบหยุดทำงานทันที | ❌ ห้ามลบ/คอมเม้นต์เด็ดขาด |
| 🟠 **TIER-2** | Feature-Critical | Feature หลักเสียหาย | ❌ ห้ามลบ ต้องได้รับ Review ก่อนแก้ไข |
| 🟡 **TIER-3** | Config-Critical | ประสิทธิภาพ/เสถียรภาพลดลง | ✅ แก้ได้ แต่ต้อง verify ก่อน merge |

---

## 🔴 TIER-1: System-Critical (14 ไฟล์)

> ⚠️ ถ้าไฟล์เหล่านี้ถูกลบหรือคอมเม้นต์ออก → **ระบบจะหยุดทำงานทันที**

### AI Engine (Python FastAPI)

| # | ไฟล์ | ฟังก์ชัน/คลาส Critical | ทำไมห้ามลบ |
|---|------|------------------------|-----------|
| 1 | `ai-engine/app/core/vrp_solver.py` | `solve_vrp()`, `compute_distance_matrix()` | หัวใจ OR-Tools VRP — ถ้าหาย ระบบจัดเส้นทางใช้ไม่ได้ |
| 2 | `ai-engine/app/core/scoring.py` | `rank_candidates()` | Dispatch ranking — ถ้าหาย ไม่สามารถจัดลำดับ Rider ได้ |
| 3 | `ai-engine/app/core/geo_utils.py` | `haversine_distance()`, `calculate_bearing()`, `is_same_direction()` | Foundation math — ทุก scoring/routing module อ้างอิง |
| 4 | `ai-engine/app/api/v1/endpoints/optimize.py` | `optimize_route()` | VRP HTTP endpoint (`POST /api/optimize-route`) |
| 5 | `ai-engine/app/api/v1/endpoints/dispatch.py` | `rank_dispatch_candidates()` | Dispatch HTTP endpoint (`POST /api/v1/dispatch/rank`) |
| 6 | `ai-engine/app/api/v1/api.py` | `v1_router` + router registrations | Router ที่ mount ทุก v1 endpoint |
| 7 | `ai-engine/main.py` | `app.include_router(v1_router, ...)`, `app.include_router(optimize.router, ...)` | ถ้าคอมเม้นต์บรรทัดนี้ = AI Engine ตายทั้งระบบ |

### Backend API (.NET 8)

| # | ไฟล์ | ฟังก์ชัน/คลาส Critical | ทำไมห้ามลบ |
|---|------|------------------------|-----------|
| 8 | `BackendApi/Features/AiRouting/IAiService.cs` | `IAiService` interface (3 methods) | Contract หลัก — DispatchService, AiController อ้างอิง |
| 9 | `BackendApi/Features/AiRouting/AiService.cs` | `RankDispatchCandidatesAsync()`, `OptimizeRouteAsync()`, `PredictEtaAsync()` + **Fallback methods ทั้ง 3** | Proxy + Fallback ไปหา AI Engine — ถ้าลบ fallback = AI ล่มแล้วทั้งระบบตาม |
| 10 | `BackendApi/Controllers/Business/AiController.cs` | `OptimizeRoute()` | REST entry point สำหรับ VRP (`POST /api/ai/optimize-route`) |
| 11 | `BackendApi/Features/AiRouting/OsrmRoutingService.cs` | `GetRouteDetailsAsync()`, `SnapToRoadAsync()`, `GetOptimizedTripSequenceAsync()` | Road-snap polyline — ถ้าลบ = แผนที่ไม่มีเส้นทางถนนจริง |
| 12 | `BackendApi/Features/DispatchManagement/DispatchService.cs` | `StartDispatchAsync()`, `FindAndOfferAsync()`, `TryOfferToRiderAsync()` | หัวใจ Dispatch Orchestrator — ถ้าลบ = ไม่มีงานส่งให้ Rider |
| 13 | `BackendApi/Hubs/TrackingHub.cs` + partial files (`*.Location.cs`, `*.RiderStatus.cs`, `*.Dispatch.cs`) | `OnConnectedAsync()`, `OnDisconnectedAsync()` | Realtime SignalR gateway — ถ้าลบ = ไม่มี real-time tracking |
| 14 | `BackendApi/Setup/ServiceSetup.cs` | AI HttpClient registration (L157-183), OSRM HttpClient registration (L186) | DI registration — ถ้าลบ block นี้ = IAiService/OsrmRoutingService inject ไม่ได้ |

---

## 🟠 TIER-2: Feature-Critical (12 ไฟล์)

> ⚠️ ถ้าไฟล์เหล่านี้ถูกลบหรือแก้ไขผิด → **Feature หลักจะเสียหาย**

### Backend State & Infrastructure

| # | ไฟล์ | ทำไมสำคัญ |
|---|------|-----------|
| 15 | `BackendApi/Core/StateMachines/OrderState.cs` | State transition rules — ลบ = Illegal State Transition ทำให้ Order ค้าง |
| 16 | `BackendApi/Core/StateMachines/RiderState.cs` | State transition rules — ลบ = Rider ค้างใน state ผิด |
| 17 | `BackendApi/Infrastructure/EventBus/RabbitMqEventBus.cs` | Message broker core — ลบ = Integration Events สูญหายหมด |
| 18 | `BackendApi/Infrastructure/Redis/RiderPresenceService.cs` | GEORADIUS + GPS presence — ลบ = หา Rider ใกล้ๆ ไม่ได้ |
| 19 | `BackendApi/Infrastructure/Redis/RedisLockService.cs` | Distributed lock — ลบ = Double dispatch (2 Rider รับงานเดียวกัน) |
| 20 | `BackendApi/Core/DataHandlers/DBHandlerCore.cs` | Data access layer foundation — ลบ = CRUD ทั้งหมดพัง |

### Background Workers

| # | ไฟล์ | ทำไมสำคัญ |
|---|------|-----------|
| 21 | `BackendApi/Services/BackgroundWorkers/DispatchTimeoutWorker.cs` | Offer timeout sweeper — ลบ = Rider ไม่ตอบรับแต่ offer ค้างตลอดกาล |
| 22 | `BackendApi/Services/BackgroundWorkers/HeartbeatMonitor.cs` | STALE→OFFLINE sweeper — ลบ = Rider หลุดแต่ยังเป็น IDLE |
| 23 | `BackendApi/Services/BackgroundWorkers/OsrmSnapWorker.cs` | GPS snap-to-road — ลบ = พิกัด raw อยู่กลางทุ่ง |
| 24 | `BackendApi/Services/Telemetry/TelemetryService.cs` | GPS ingestion pipeline — ลบ = GPS ไม่บันทึก |

### Frontend Apps

| # | ไฟล์ | ทำไมสำคัญ |
|---|------|-----------|
| 25 | `rider_app/lib/core/signalr/signalr_service.dart` | Rider realtime connection — ลบ = Rider ไม่เชื่อมต่อ SignalR |
| 26 | `rider_app/lib/core/location/gps_buffer_service.dart` | GPS queue + Head-of-Line blocking fix — ลบ = GPS ส่งไม่ได้/คิวค้าง |
| 27 | `rider_app/lib/core/location/location_service.dart` | Background GPS tracking — ลบ = ไม่มี GPS tracking |
| 28 | `admin-dashboard/src/app/features/map/map.component.ts` | OSRM polyline rendering + VRP waypoint sequence — ลบ = แผนที่ไม่แสดงเส้นทาง |

---

## 🟡 TIER-3: Config-Critical (5 ไฟล์)

> แก้ไขได้ แต่ต้อง verify ว่าไม่ทำให้ระบบพัง

| # | ไฟล์ | ทำไมสำคัญ |
|---|------|-----------|
| 29 | `docker-compose.yml` | Infrastructure topology — แก้ผิด = service เชื่อมต่อกันไม่ได้ |
| 30 | `BackendApi/Program.cs` | App bootstrap — แก้ผิด = app ไม่เริ่มทำงาน |
| 31 | `nginx-proxy/nginx.conf` | Reverse proxy routing — แก้ผิด = frontend/backend เข้าไม่ถึง |
| 32 | `ai-engine/app/models/` (ทุกไฟล์ใน directory) | Pydantic request/response schemas — แก้ field = API contract เปลี่ยน |
| 33 | `BackendApi/Core/Events/DispatchEvents.cs` | Domain event definitions — ลบ event class = handler compile ไม่ผ่าน |

---

## Quick Reference: Protected Endpoints

| Endpoint | ไฟล์ที่ register | ไฟล์ที่ handle |
|----------|-----------------|---------------|
| `POST /api/optimize-route` | `ai-engine/main.py` L17 | `ai-engine/app/api/v1/endpoints/optimize.py` |
| `POST /api/v1/dispatch/rank` | `ai-engine/app/api/v1/api.py` L7 | `ai-engine/app/api/v1/endpoints/dispatch.py` |
| `POST /api/v1/predict-eta` | `ai-engine/app/api/v1/api.py` L8 | `ai-engine/app/api/v1/endpoints/predict.py` |
| `POST /api/ai/optimize-route` | `ServiceSetup.cs` L157 | `BackendApi/Controllers/Business/AiController.cs` |
| `GET /health` (AI) | `ai-engine/main.py` L19 | inline |
| `WS /hubs/tracking` | `BackendApi/Setup/ApplicationSetup.cs` | `BackendApi/Hubs/TrackingHub.cs` |

---

## How to Update This Registry

1. เมื่อเพิ่มไฟล์ใหม่ที่เข้าข่าย Critical → เพิ่มรายการใน Tier ที่เหมาะสม
2. เมื่อ rename/move ไฟล์ Critical → **ต้องอัปเดตเอกสารนี้ทันที**
3. ห้ามลบรายการออกจาก Registry โดยไม่ได้รับอนุมัติจาก Project Owner
4. Version ของเอกสารนี้ต้องอัปเดตทุกครั้งที่มีการเปลี่ยนแปลง
