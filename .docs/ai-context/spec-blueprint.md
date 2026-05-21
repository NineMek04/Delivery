---
scope: System Architecture & Product Vision
source_of_truth:
  - PROJECT-SPEC.md (Section 1-3, Problems To Solve, Core Features, System Architecture)
  - AI-BLUEPRINT.md (Section 1-5, System Architecture, Data Flow, State Machine)
related_contexts:
  - .docs/ai-context/spec-backend.md
  - .docs/ai-context/contracts/state-machine.md
  - .docs/ai-context/contracts/signalr-contracts.md
forbidden_patterns:
  - เปลี่ยน OR-Tools เป็น solver อื่นโดยไม่ได้รับคำสั่ง
  - เปลี่ยน SRID จาก 4326
  - ให้ Redis เป็น source of truth
known_pitfalls:
  - Dispatch offer timeout 30s ต้องตรงกัน ทั้ง Backend และ Redis TTL
  - Rider reconnect race condition ระหว่าง OFFERED → ASSIGNED
  - OSRM ต้องสร้าง osrm_data ก่อนรัน container
---

# spec-blueprint.md — System Architecture & Product Vision

> **Source**: `PROJECT-SPEC.md` (Sections 1–3) + `AI-BLUEPRINT.md` (Sections 1–5)  
> **For quick route optimization spec** → read `spec-ai-engine.md`  
> **For state transition details** → read `contracts/state-machine.md`

---

## 1. Problem Statement

ระบบแพลตฟอร์มจัดส่งสินค้า/อาหาร ประสบปัญหา:
- เส้นทาง multi-drop / batched orders ที่ไม่ได้ optimize → สิ้นเปลือง **เวลา + เชื้อเพลิง**
- ต้องการ real-time GPS tracking ระหว่าง Rider mobile app ↔ Admin dashboard
- ต้องการ prototype ที่ใช้ smartphone เป็น GPS sensor (BYOD) แทน hardware เฉพาะทาง

**แนวทางแก้ไข:**
- AI (VRP) คำนวณเส้นทางด้วย Google OR-Tools
- SignalR/WebSocket สำหรับ real-time communication
- Microservices บน Docker — Zero-SDK setup (Docker Desktop เท่านั้น)

---

## 2. Core Features (สถานะปัจจุบัน)

| Feature | Status |
|---|---|
| AI Route Optimization (OR-Tools VRP) | ✅ Foundation Ready |
| Real-time GPS Tracking (SignalR + Redis) | ✅ Ready |
| Dispatch Orchestrator (30s Lifecycle) | ✅ Phase A Ready |
| Admin Dashboard (Angular 19) | 🟢 85% Ready |
| Rider Mobile App (Flutter) | 🟡 30% Ready |
| Dockerized Services (5+ services) | ✅ Ready |
| Backend Security (JWT + Refresh Token) | ✅ Enhanced |
| PostGIS Spatial (GiST, Partitioning) | ✅ Completed |
| OSRM Offline Routing | ✅ Completed |
| Simulation Sandbox (E2E Simulator) | ✅ Completed |

---

## 3. System Architecture (Microservices)

```
Flutter Rider App
    │
    │ SignalR / WebSocket
    ▼
.NET Backend API  ◄──── REST ────►  Python AI Service (Port 8000)
    │
    │ EF Core (Ledger)      │ Redis (Pulse)
    ▼                       ▼
PostgreSQL + PostGIS    GPS, Locks, Presence (Hot Data)
    ▲
    │ REST (Fluent API)
    ▼
Angular Admin Dashboard (Port 80)
```

**สำคัญ:**
- **PostgreSQL** = Persistent Truth (Orders, Riders, Transactions)
- **Redis** = Operational Realtime State เท่านั้น (GPS buffer, presence, locks)

---

## 4. Data Flow

1. Flutter/Simulator ส่ง GPS → Backend via SignalR (`UpdateLocation`)
2. Backend buffer GPS → `GpsSyncBuffer` (Memory) → Flush ลง PostGIS ทุก 30s (`GpsSyncWorker`)
3. เมื่อสร้าง Order → Backend เรียก AI Engine (`/api/optimize-route`) → VRP result
4. Backend ค้นหา Idle Riders → เรียก AI (`/api/v1/dispatch/rank`) → ได้ ranked candidates
5. Backend ส่ง Offer ผ่าน SignalR → Rider รับภายใน 30s
6. Rider ตอบรับ → Backend Assign → Broadcast ไปยัง Admin dashboard

---

## 5. Dispatch Lifecycle (CRITICAL CONTRACT)

```
Order States:
CREATED → MATCHING → OFFERING → ASSIGNED → PICKING_UP → DELIVERING → COMPLETED
                                                                     → CANCELLED
```

**สำคัญ:**
- `OFFERING` state: Offer ส่งแล้ว รอ Rider ตอบรับ (TTL **30s**)
- ถ้า Rider ไม่ตอบ: ระบบ re-dispatch ไปหา Rider คนถัดไป (ผ่าน `DispatchTimeoutWorker`)
- ถ้าไม่มี Rider ว่าง: Order ค้างอยู่ใน `MATCHING` รอ Rider ใหม่ online

```
Rider States:
OFFLINE → IDLE → OFFERED → ASSIGNED → PICKING_UP → DELIVERING → IDLE (auto-release)
```

> **รายละเอียดสมบูรณ์** → `contracts/state-machine.md`

---

## 6. OSRM Routing Fallback Chain

```
Request Routing
    │
    ├─► 1. Redis Cache (TTL 24h)
    │         │ Cache Hit → return
    │         │ Cache Miss
    │
    ├─► 2. Local OSRM (Port 5001) ← Offline Dijkstra MLD
    │         │ Success → Cache + return
    │         │ Fail / Timeout
    │
    ├─► 3. Public OSRM API (router.project-osrm.org)
    │         │ Success → Cache + return
    │         │ Fail
    │
    └─► 4. Haversine Straight-line (Emergency fallback)
```

**Polly Policy:** 2 Retries + 15s Circuit Breaker + 1.5s HTTP Timeout  
**Compression:** Google Polyline encoding (~99% size reduction)

---

## 7. Version & Project Info

- **Version:** 0.9.0 (Phase 5)
- **Current Phase:** Real-world Routing & Real-time Dispatch Simulation
- **Milestone:** Backend 100% | AI 95% | Admin Dashboard 85% | Rider App 30%
- **Next Immediate Actions:**
  1. Flutter Rider App UI (Login, Home, Active Delivery, Map)
  2. Backend Tier 1 features (Customer Real-time Events, CustomerAddress, OrderItems)
