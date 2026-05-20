# Delivery Platform — Phase 6: Production Readiness Roadmap

## เป้าหมาย

เปลี่ยนระบบจาก **"Prototype ที่ทำงานได้"** → **"Production-grade Thesis Platform"** ที่พร้อม: Present / Defense / Demo / Stress Test / Benchmark / Reliability Validation

## Core Strategy

> หยุดเพิ่ม feature ใหญ่ฝั่ง business/mobile ก่อน → เข้าสู่โหมด **"Hardening + Observability + Reliability + Operational Quality"**

---

## แยกขอบเขตชัดเจน: Production vs Presentation

| ด้าน | Production Readiness | Presentation Readiness |
|---|---|---|
| **เป้าหมาย** | ระบบเสถียร ปลอดภัย วัดผลได้ | นำเสนอได้สวย มีหลักฐาน |
| **เนื้อหา** | CI/CD, Security, Monitoring, Health Checks, Load Testing, Integration Tests | Grafana Dashboard สวย, Benchmark Reports, Analytics Charts, Demo Scenarios |
| **ทำเมื่อ** | Sprint 1-3 (ทำก่อน) | Sprint 4 (ต่อยอดจาก Production) |

---

## Tech Stack Final (Phase 6)

| Layer | Technology |
|---|---|
| Backend API | .NET 8 |
| Realtime | SignalR |
| Spatial DB | PostgreSQL + PostGIS |
| Hot Data | Redis |
| AI Engine | FastAPI + OR-Tools |
| Routing | OSRM |
| Reverse Proxy | Nginx |
| Logging | Serilog + Seq |
| Metrics | Prometheus |
| Visualization | Grafana |
| CI/CD | GitHub Actions |
| Testing | xUnit + Testcontainers + Node.js |
| Stress Test | Node.js Simulator |
| Containerization | Docker Compose |

---

## PHASE EXECUTION PLAN

---

### Sprint 1 — Foundation Hardening (Production Readiness)

> เป้าหมาย: **"ระบบต้องดู Production-ready"** — ระยะสำคัญที่สุด

---

#### 1. CI/CD Pipeline

> ทุก push/PR → build → test → validate → dockerize **อัตโนมัติทั้งหมด**

##### [NEW] [ci.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/.github/workflows/ci.yml)

| Job | Steps |
|---|---|
| `backend` | `dotnet restore` → `dotnet build` → `dotnet test` → `docker build` |
| `frontend` | `npm ci` → `npm run build` |
| `ai-engine` | `pip install -r requirements.txt` → `pytest` → `flake8` |
| `docker-compose` | `docker compose build` → `docker compose up -d` → health checks |

**Triggers:** `push` to `main`, `pull_request`

---

#### 2. Secrets Management

> เอา secrets ออกจาก source code → ย้ายจาก `docker-compose.yml` ไป `.env`

##### [NEW] [.env.example](file:///c:/Users/ASUS/Desktop/Project/Delivery/.env.example)
- `POSTGRES_PASSWORD=`, `JWT_SECRET=`, `REDIS_PASSWORD=`, `SEQ_API_KEY=`

##### [MODIFY] [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml)
- แทน hardcoded secrets ด้วย `${VARIABLE}` references
- เพิ่ม `env_file: .env` ให้ทุก service

---

#### 3. HTTPS Reverse Proxy

> จำลอง production environment จริง

##### [NEW] [nginx-proxy/nginx.conf](file:///c:/Users/ASUS/Desktop/Project/Delivery/nginx-proxy/nginx.conf)

| Route | Destination | Note |
|---|---|---|
| `/` | Angular frontend | static files |
| `/api` | Backend :80 | REST API |
| `/hubs` | Backend :80 | WebSocket Upgrade |
| `/metrics` | Prometheus | scrape endpoint |

- รองรับ HTTPS (self-signed cert สำหรับ dev)
- WebSocket Upgrade headers
- CORS isolation

##### [MODIFY] [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml)
- เพิ่ม service `nginx-proxy`

---

#### 4. Security Hardening

##### [MODIFY] [SecurityConfiguration.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Setup/SecurityConfiguration.cs)

**Security Checklist:**
- [x] JWT + Refresh Token Rotation — มีแล้ว
- [x] Rate Limiting — มีแล้ว (`AuthRateLimitPolicy`)
- [x] SQL injection — ใช้ EF Core (parametrized) ปลอดภัย ✅
- [ ] CORS review — ตรวจว่า production ไม่มี `AllowAnyOrigin()`
- [ ] JWT expiration validation — ตรวจ revoke flow
- [ ] Brute force test — ตรวจว่า rate limit ทำงานจริง
- [ ] Secrets ย้ายออกจาก source code (ข้อ 2)

---

#### 5. Enhanced Health Checks

##### [NEW] [SignalRHealthCheck.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/HealthChecks/SignalRHealthCheck.cs)
- ตรวจ active SignalR connection count

##### [NEW] [DispatchQueueHealthCheck.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/HealthChecks/DispatchQueueHealthCheck.cs)
- ตรวจ pending dispatch queue size

##### [MODIFY] [ServiceSetup.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Setup/ServiceSetup.cs)
- Register health checks ใหม่

##### [MODIFY] [ApplicationSetup.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Setup/ApplicationSetup.cs)
- เพิ่ม endpoint `/health/detail` → JSON response:

| Metric | Source |
|---|---|
| DB latency | PostgreSQL ping |
| Redis latency | Redis ping |
| Active SignalR connections | TrackingHub |
| Dispatch queue size | DispatchService |
| GPS updates/sec | TrackingHub counter |
| Active riders | Redis Presence |

---

### Sprint 2 — Observability Layer (Production Readiness)

> เป้าหมาย: **"มองเห็นระบบทั้งระบบ"**

---

#### 6. Structured Logging — Serilog → Seq

##### [MODIFY] [BackendApi.csproj](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/BackendApi.csproj)
- เพิ่ม NuGet: `Serilog.Sinks.Seq`

##### [MODIFY] [Program.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Program.cs)
- เพิ่ม `.WriteTo.Seq("http://seq:5341")`

##### [MODIFY] [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml)
- เพิ่ม service `seq` (image: `datalust/seq:latest`, UI port `8081:80`, ingest `5341:5341`)

**Log Categories:**

| Category | Example |
|---|---|
| Dispatch | rider matching, offer sent, assign |
| Security | failed login, token revoke |
| GPS | invalid coordinates, buffer flush |
| AI | optimization latency, scoring |
| Database | slow query, migration |

**Structured Fields:** `requestId`, `userId`, `orderId`, `riderId`, `latency`, `error stack`

---

#### 7. Metrics — Prometheus

##### [MODIFY] [BackendApi.csproj](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/BackendApi.csproj)
- เพิ่ม NuGet: `prometheus-net.AspNetCore`

##### [MODIFY] [ApplicationSetup.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Setup/ApplicationSetup.cs)
- เพิ่ม `app.UseHttpMetrics()` + `app.MapMetrics()` → endpoint `/metrics`

##### [NEW] [prometheus.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/prometheus.yml)
- Scrape target: `backend:80/metrics`

##### [MODIFY] [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml)
- เพิ่ม service `prometheus`

**Metrics สำคัญ:**

| Metric | Type |
|---|---|
| HTTP request duration | histogram |
| Dispatch latency | histogram |
| GPS throughput | counter |
| Active riders | gauge |
| Redis ops/sec | gauge |
| SignalR connections | gauge |

---

#### 8. Grafana Dashboard

##### [MODIFY] [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml)
- เพิ่ม service `grafana` (port `3000`)

##### [NEW] [grafana/provisioning/](file:///c:/Users/ASUS/Desktop/Project/Delivery/grafana/provisioning/)
- Auto-provision Prometheus datasource + dashboard JSON

**Dashboard Panels:**

| Section | Panels |
|---|---|
| System | CPU, RAM, container health |
| Backend | API latency, request/sec, error rate |
| Dispatch | dispatch latency, active offers, failed dispatch % |
| GPS | GPS/sec, active riders, SignalR connections |

---

### Sprint 3 — Reliability & Validation (Production Readiness)

> เป้าหมาย: **"พิสูจน์ว่าระบบเสถียร"**

---

#### 9. Integration Test Suite

##### [NEW] [BackendApi.IntegrationTests/](file:///c:/Users/ASUS/Desktop/Project/Delivery/scripts/BackendApi.IntegrationTests/)

| Test Class | ครอบคลุม |
|---|---|
| `AuthFlowTests` | login → refresh → revoke → verify expired |
| `OrderLifecycleTests` | create → dispatch → assign → pickup → complete |
| `OrderCancelTests` | create → cancel |
| `RealtimeTests` | GPS update → rider offline → reconnect |
| `AiIntegrationTests` | optimize route → rider ranking |

Stack: `xUnit` + `WebApplicationFactory<Program>` + `Testcontainers`

---

#### 10. AI Engine Tests

##### [NEW] [ai-engine/tests/](file:///c:/Users/ASUS/Desktop/Project/Delivery/scripts/ai-engine-tests/)

| File | Purpose |
|---|---|
| `test_vrp_solver.py` | OR-Tools VRP basic solve |
| `test_api_optimize.py` | `/api/v1/optimize` endpoint |
| `test_api_score.py` | `/api/v1/score-riders` endpoint |

---

#### 11. Load / Stress Testing

> ใช้ Node.js ทั้งหมด — consistency กับ E2E Simulator เดิม, reuse SignalR/auth code ได้

##### [NEW] [scripts/load-test/](file:///c:/Users/ASUS/Desktop/Project/Delivery/scripts/load-test/)

| Script | Purpose |
|---|---|
| `signalr-stress.js` | จำลอง 50/100 riders ส่ง GPS พร้อมกัน |
| `api-stress.js` | ยิง HTTP requests (create order, get orders) |
| `dispatch-stress.js` | dispatch queue pressure |
| `reconnect-stress.js` | rider disconnect/reconnect stability |
| `report-template.md` | Template บันทึกผลทดสอบ |

**Scenarios:**

| Scenario | Goal |
|---|---|
| 50 riders | baseline |
| 100 riders | stable |
| 500 GPS/sec | throughput ceiling |
| rapid reconnect | SignalR stability |

**Metrics ที่วัด:**

| Metric | Target |
|---|---|
| Dispatch latency | < 500ms |
| API p95 response | < 200ms |
| DB insert/sec | stable under load |
| Redis memory | no overflow |
| SignalR msg/sec | no collapse |

---

### Sprint 4 — Operational Intelligence (Presentation Readiness)

> เป้าหมาย: **"ระบบดู intelligent + นำเสนอได้สวย"**

---

#### 12. Analytics API (Backend)

##### [NEW] [AnalyticsController.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Controllers/Business/AnalyticsController.cs)

| Endpoint | Purpose |
|---|---|
| `GET /analytics/summary` | avg delivery time, success rate, failed dispatch % |
| `GET /analytics/realtime` | active riders, GPS/sec, dispatch queue |
| `GET /analytics/rider-utilization` | workload breakdown |
| `GET /analytics/heatmap` | PostGIS order density by location |

##### [MODIFY] [analytics.component.ts](file:///c:/Users/ASUS/Desktop/Project/Delivery/admin-dashboard/src/app/features/analytics/analytics.component.ts)
- เปลี่ยนจาก client-side calculation → backend aggregate query
- เพิ่ม: delivery trend graph, rider utilization pie chart, realtime heatmap overlay

---

#### 13. ETA Prediction Engine

##### [NEW] [ai-engine/app/eta_predictor.py](file:///c:/Users/ASUS/Desktop/Project/Delivery/ai-engine/app/eta_predictor.py)

| Factor | Source |
|---|---|
| Distance | OSRM route distance |
| Speed history | GPS history average |
| Time-of-day traffic | multiplier (rush hour = 1.5x) |
| Weather | placeholder (manual/API ภายหลัง) |

##### [MODIFY] [ai-engine/main.py](file:///c:/Users/ASUS/Desktop/Project/Delivery/ai-engine/main.py)
- เพิ่ม endpoint `POST /api/v1/predict-eta`

##### [MODIFY] [BackendApi/Services/AiService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/AiService.cs)
- เพิ่ม `PredictEtaAsync()`

##### [MODIFY] [BackendApi/Services/OrderService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/OrderService.cs)
- เรียก ETA ตอน create order + assign rider

---

### Sprint 5 — Optional Enterprise Layer

> ทำเมื่อทุกอย่าง stable แล้วเท่านั้น

---

#### 14. Event-Driven Architecture (Optional)

##### [MODIFY] [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml)
- เพิ่ม service `rabbitmq` (image: `rabbitmq:3-management`, port `5672`, UI `15672`)

##### [NEW] [BackendApi/Infrastructure/EventBus/](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Infrastructure/EventBus/)
- `IEventBus.cs` — interface
- `RabbitMqEventBus.cs` — implementation
- `Events/OrderCreatedEvent.cs`, `OrderStatusChangedEvent.cs`, `RiderLocationUpdatedEvent.cs`

**ใช้เมื่อ:** ระบบ synchronous stable แล้วเท่านั้น

---

## Test Files Location

> [!IMPORTANT]
> ทุกไฟล์ test/stress/load เก็บที่: `C:\Users\ASUS\Desktop\Project\Delivery\scripts`

---

## Verification Checklist

### Production Readiness ✅
- [ ] CI/CD: build + test + docker build ผ่านทุก push
- [ ] Security: HTTPS ผ่าน nginx, refresh token revoke ทำงาน, rate limit block brute force
- [ ] Health: `/health` + `/health/ready` + `/health/detail` ครบ
- [ ] Observability: Seq logs เข้า, Prometheus scrape สำเร็จ
- [ ] Tests: Integration tests ผ่าน, AI tests ผ่าน
- [ ] Stress: 100 riders stable, no SignalR collapse, no Redis overflow

### Presentation Readiness 🎯
- [ ] Grafana Dashboard สวย + มี panels ครบ
- [ ] Analytics charts แสดงข้อมูลจริงจาก backend
- [ ] Benchmark report จาก load test
- [ ] ETA prediction ทำงาน
- [ ] Demo scenario รันได้ end-to-end

---

## Scope Control Rules

> [!CAUTION]
> **ห้ามแตก scope** ไปทำสิ่งเหล่านี้ในเฟสนี้:
> - Kubernetes
> - Real ML training
> - Multi-region
> - Payment gateway
> - Native production mobile polish
> - Cloud deployment จริง

---

## Priority Summary

### MUST DO — ทำแน่นอน
1. CI/CD Pipeline
2. `.env` secrets migration
3. HTTPS reverse proxy
4. Enhanced health checks
5. Seq logging
6. Prometheus metrics
7. Grafana dashboards
8. Integration tests
9. Load tests
10. Analytics API
11. ETA engine

### OPTIONAL — ทำเมื่อทุกอย่าง stable แล้ว
12. RabbitMQ Event Bus

---

## Progress Update - 2026-05-20 (CI + AI Tests)

### Plan Status Checked Against Codebase
- Sprint 1 Foundation Hardening is mostly scaffolded in code:
  - `.github/workflows/ci.yml` exists.
  - `.env.example` exists.
  - `nginx-proxy/nginx.conf` exists.
  - `SignalRHealthCheck` and `DispatchQueueHealthCheck` exist.
  - `/health`, `/health/ready`, `/health/detail`, and `/metrics` are mapped in backend pipeline.
- Sprint 2 Observability is scaffolded:
  - Seq, Prometheus, and Grafana services are present in `docker-compose.yml`.
  - Serilog Seq sink and Prometheus middleware are wired in backend code.
  - `prometheus.yml` exists.
- Sprint 3 Reliability had partial coverage:
  - Backend integration test project exists under `scripts/BackendApi.IntegrationTests/`.
  - Load-test scaffold exists under `scripts/load-test/`.
  - AI engine tests were missing before this update.

### Completed In This Update
- Fixed CI backend integration test path to the real project location:
  - `scripts/BackendApi.IntegrationTests/BackendApi.IntegrationTests.csproj`
- Removed `continue-on-error` from backend and AI test steps so CI now treats tests as real gates.
- Added `httpx` installation to the AI CI job so FastAPI `TestClient` can run.
- Added AI engine pytest suite under `ai-engine/tests/`:
  - `test_vrp_solver.py`
  - `test_api_optimize.py`
  - `test_api_dispatch.py`

### Verification
- `dotnet build BackendApi\BackendApi.csproj --no-restore` passed with 0 errors.
- `dotnet test scripts\BackendApi.IntegrationTests\BackendApi.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~MappingConfigTests --verbosity minimal` passed 1/1.
- Full backend integration test execution reaches the test runner, but Docker/Testcontainers tests cannot run in the current local session because Docker endpoint access is unavailable.
- AI pytest could not be run locally because this Windows session only exposes a broken Microsoft Store `python.exe` alias and no `py` launcher. CI now installs the required Python test dependencies and should run these tests in GitHub Actions.

### Next Action From Plan
- Finish Sprint 3 by expanding backend integration tests beyond spatial/mapping into:
  - Auth flow
  - Order lifecycle
  - Realtime GPS/reconnect
  - AI integration contract
- Then add real load-test scripts split from the current single `simulator.js` into the planned `signalr-stress.js`, `api-stress.js`, `dispatch-stress.js`, and `reconnect-stress.js`.
