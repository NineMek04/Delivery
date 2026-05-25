# AI-INDEX.md — Master Context Router

> **⚠️ AI AGENT: อ่านไฟล์นี้ก่อนทุกครั้ง** เพื่อ route ไปยัง spec ที่ถูกต้อง ประหยัด token และป้องกัน context corruption

**Version:** 0.9.0 | **Last Updated:** 2026-05-21

---

## 1. Required Reading Order (ทุก Task ต้องทำ)

```
1. AI-INDEX.md        ← ไฟล์นี้ (อ่านก่อนเสมอ)
2. AI-BOOTSTRAP.md    ← Behavior rules & anti-hallucination constraints
3. AI-CHANGELOG.md    ← สถานะงานล่าสุด (อ่านเฉพาะส่วนที่เกี่ยวข้อง)
```

---

## 2. Context Routing Map

ระบุว่ากำลังทำงานเกี่ยวกับส่วนไหน แล้วอ่านเฉพาะไฟล์ที่ตรงกัน:

### 🏛️ System Architecture & Vision
→ [spec-blueprint.md](.docs/ai-context/spec-blueprint.md)
- Dispatch lifecycle, Rider lifecycle
- System overview, data flow, microservices topology

### ⚙️ Backend API (.NET 8)
→ [spec-backend.md](.docs/ai-context/spec-backend.md)
- TrackingHub, JWT/Auth, Redis presence
- DBHandlerCore, CrudControllerBase, RefNumbers
- PostGIS, GiST indexes, Partitioning

### 🎨 Frontend (Angular 19 Admin Dashboard)
→ [spec-frontend.md](.docs/ai-context/spec-frontend.md)
- SimMap, Leaflet, Polyline decoding
- BaseApiService, DeliveryHttpRequest, SignalR integration
- requestAnimationFrame, RxJS leak prevention

### 🤖 AI Engine (Python FastAPI + OR-Tools)
→ [spec-ai-engine.md](.docs/ai-context/spec-ai-engine.md)
- VRP Solver, PATH_CHEAPEST_ARC
- Haversine matrix, Scoring engine
- `/api/optimize-route`, `/api/v1/dispatch/rank`

### 🏗️ Infrastructure & DevOps
→ [spec-infra-devops.md](.docs/ai-context/spec-infra-devops.md)
- Docker Compose topology (11 services)
- OSRM offline setup, Polly retry/circuit breaker
- Redis config, Nginx proxy, Observability stack

### 📱 Mobile App (Flutter Rider App)
→ [spec-mobile-rider.md](.docs/ai-context/spec-mobile-rider.md)
- Riverpod, GoRouter, Dio
- Background GPS, SignalR connection
- Token refresh clocking, Rider state machine

### 📋 Runtime Rules & Coding Constraints
→ [runtime-rules.md](.docs/ai-context/runtime-rules.md)
- Forbidden patterns, Base class rules
- Environment constraints, Logging policy

---

## 3. Contracts (อ่านเมื่อต้องการ payload spec ที่แม่นยำ)

| Contract | ใช้เมื่อ |
|---|---|
| [signalr-contracts.md](.docs/ai-context/contracts/signalr-contracts.md) | ทำงานกับ SignalR Hub, events, payloads |
| [state-machine.md](.docs/ai-context/contracts/state-machine.md) | ต้องรู้สถานะ Order/Rider, transitions, timeout rules |
| [api-contracts.md](.docs/ai-context/contracts/api-contracts.md) | REST endpoints, DTOs, auth rules |
| [redis-keys.md](.docs/ai-context/contracts/redis-keys.md) | Redis key schemas, TTLs, data types |
| [geojson-contracts.md](.docs/ai-context/contracts/geojson-contracts.md) | Polyline encoding, SRID rules, RouteGeometry |

---

## 4. Historical Archives (Layer 1 — อย่าโหลดทั้งไฟล์ถ้าไม่จำเป็น)

| File | Purpose |
|---|---|
| `PROJECT-SPEC.md` | Full project spec, getting started guide |
| `AI-BLUEPRINT.md` | Full system architecture, Flutter feature lists |
| `AI-CHANGELOG.md` | Complete history of AI changes |
| `OSRM-SETUP.md` | Complete OSRM setup and compilation guide |
| `docker-compose.yml` | Live container configuration |

> ไฟล์เหล่านี้เป็น **source archive** ห้ามลบหรือแก้ไขเนื้อหาหลัก ใช้สำหรับ trace กลับและ recovery

---

## 5. Quick Decision Tree

```
ต้องการรู้ว่า Order ปัจจุบันอยู่ state ไหน?
  → contracts/state-machine.md

ต้องการเขียน SignalR event ใหม่?
  → contracts/signalr-contracts.md + spec-backend.md

ต้องการเพิ่ม Redis key?
  → contracts/redis-keys.md (ห้ามให้ Redis เป็น source of truth!)

ต้องการเพิ่ม REST endpoint?
  → contracts/api-contracts.md + spec-backend.md

ต้องการแก้ Angular component?
  → spec-frontend.md

ต้องการแก้ Flutter screen?
  → spec-mobile-rider.md

ต้องการแก้ AI routing logic?
  → spec-ai-engine.md

ต้องการแก้ Docker/infra?
  → spec-infra-devops.md
```
