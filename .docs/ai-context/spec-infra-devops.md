---
scope: Infrastructure & DevOps (Docker, OSRM, Redis, Nginx, Observability)
source_of_truth:
  - OSRM-SETUP.md (ALL)
  - docker-compose.yml (ALL)
  - PROJECT-SPEC.md (Section 3, Docker Services)
  - AI-CHANGELOG.md (2026-05-14 Redis, 2026-05-18 Spatial/Health, 2026-05-19 OSRM+Polly)
related_contexts:
  - .docs/ai-context/spec-backend.md
  - .docs/ai-context/contracts/redis-keys.md
forbidden_patterns:
  - ให้ Redis เป็น source of truth
  - เพิ่ม secrets ใน docker-compose.yml (ต้องผ่าน .env หรือ environment variable)
  - ลบหรือแก้ไข historical root files
known_pitfalls:
  - OSRM ต้องสร้าง osrm_data/ ก่อนรัน container (ถ้าไม่มีไฟล์ container จะ crash)
  - backend ต้องรอ db และ redis เป็น healthy ก่อน start (depends_on condition)
  - GiST index migration ล้มเหลวถ้าใส่บน non-geometry column
  - PartitionMaintenanceWorker ต้องรันก่อน insert GPS history
---

# spec-infra-devops.md — Infrastructure & DevOps

> **Source**: `OSRM-SETUP.md` (ALL) + `docker-compose.yml` + `PROJECT-SPEC.md` Sec 3 + `AI-CHANGELOG.md`  
> **Full OSRM compilation guide** → `OSRM-SETUP.md`  
> **Full compose file** → `docker-compose.yml`

---

## 1. Docker Compose Topology (11 Services)

| # | Service | Container | Image/Build | Port | Role |
|---|---|---|---|---|---|
| 1 | `db` | `delivery-db` | `postgis/postgis:15-3.3` | `5432` | Spatial Database |
| 2 | `redis` | `delivery-redis` | `redis:7-alpine` | `6379` | Hot Data Cache |
| 3 | `backend` | `delivery-backend` | `./BackendApi/Dockerfile` | `5000:80` | .NET 8 API |
| 4 | `ai-service` | `delivery-ai` | `./ai-engine/Dockerfile` | `8000` | Python FastAPI |
| 5 | `frontend` | `delivery-frontend` | `./admin-dashboard/Dockerfile` | `80` | Angular Admin |
| 6 | `rider-app` | `delivery-rider-app` | `./rider_app/Dockerfile` | `8080` | Flutter Web |
| 7 | `osrm` | `delivery-osrm` | `osrm/osrm-backend` | `5001:5000` | Offline Routing |
| 8 | `nginx-proxy` | `delivery-nginx` | `nginx:alpine` | `8081` | Reverse Proxy |
| 9 | `seq` | `delivery-seq` | `datalust/seq:latest` | `5341, 8082` | Centralized Logs |
| 10 | `prometheus` | `delivery-prometheus` | `prom/prometheus:latest` | `9090` | Metrics Scrape |
| 11 | `grafana` | `delivery-grafana` | `grafana/grafana-enterprise` | `3000` | Dashboard UI |

---

## 2. Service Dependencies (Health Check Chain)

```
db (healthy) ──────────────────────────────────►─┐
                                                  │
redis (healthy) ───────────────────────────────►─┤
                                                  │
                                              backend (healthy)
                                                  │
                                    ┌─────────────┤
                                    │             │
                               frontend      rider-app
                            (service_healthy) (service_healthy)
```

---

## 3. Key Environment Variables (backend service)

```yaml
environment:
  - ASPNETCORE_ENVIRONMENT=Development
  - ConnectionStrings__DefaultConnection=Host=db;Database=delivery_db;Username=postgres;Password=${POSTGRES_PASSWORD}
  - ConnectionStrings__Redis=redis:6379
  - AI_SERVICE_URL=http://ai-service:8000
  - Jwt__Key=${JWT_SECRET}              # >= 32 chars!
  - Jwt__Issuer=DeliveryBackendApi
  - Jwt__Audience=DeliveryClients
  - Cors__AllowedOrigins__0=http://localhost
  - Cors__AllowedOrigins__1=http://localhost:80
  - Cors__AllowedOrigins__2=http://localhost:4200
  - Cors__AllowedOrigins__3=http://localhost:8080
  - Cors__AllowedOrigins__4=http://localhost:8081
  - Routing__LocalOsrmUrl=http://osrm:5000
  - Authentication__RequireSecureCookie=false
```

---

## 4. PostgreSQL Performance Tuning (docker-compose.yml)

```yaml
command:
  - "postgres"
  - "-c"
  - "shared_buffers=1GB"
  - "-c"
  - "maintenance_work_mem=256MB"
  - "-c"
  - "work_mem=32MB"
  - "-c"
  - "effective_cache_size=2GB"
  - "-c"
  - "random_page_cost=1.1"
  - "-c"
  - "checkpoint_completion_target=0.9"
```

---

## 5. Redis Configuration

```yaml
command: redis-server --appendonly yes --maxmemory 256mb --maxmemory-policy allkeys-lru
```

- **Persistence:** AOF enabled (`appendonly yes`)
- **Max Memory:** 256MB (eviction policy: LRU)
- **Role:** GPS buffer, Rider presence, Distributed locks, Route cache (TTL 24h)

> **ห้ามให้ Redis เป็น source of truth** — PostgreSQL เท่านั้นที่เป็น persistent truth

---

## 6. OSRM Offline Routing Architecture

### Routing Fallback Chain
```
Request
  │
  ├─► 1. Redis Cache (TTL 24h) → Hit: return immediately
  │         Miss ↓
  ├─► 2. Local OSRM (port 5001) — Dijkstra MLD, offline
  │         Success: Cache → Return
  │         Fail ↓
  ├─► 3. Public OSRM (router.project-osrm.org) — online fallback
  │         Success: Cache → Return
  │         Fail ↓
  └─► 4. Haversine straight-line — emergency fallback
```

### OSRM Setup (One-time, ทำครั้งแรกครั้งเดียว)

```powershell
# Windows PowerShell (automatic)
.\scripts\setup-osrm.ps1

# หรือ manual steps:
# Phase A: Download Thailand OSM data
curl -L -o ./osrm_data/udon-thani.osm.pbf https://download.geofabrik.de/asia/thailand-latest.osm.pbf

# Phase B: Extract road network
docker run --rm --user root -v "$(pwd)/osrm_data:/data" osrm/osrm-backend osrm-extract -p /usr/local/share/osrm/profiles/car.lua /data/udon-thani.osm.pbf

# Phase C: Partition
docker run --rm --user root -v "$(pwd)/osrm_data:/data" osrm/osrm-backend osrm-partition /data/udon-thani.osrm

# Phase D: Customize
docker run --rm --user root -v "$(pwd)/osrm_data:/data" osrm/osrm-backend osrm-customize /data/udon-thani.osrm
```

> สร้างไฟล์ 23 รายการใน `./osrm_data/` รวมถึง `udon-thani.osrm.edges`, `udon-thani.osrm.names`

### OSRM Service Config (docker-compose.yml)

```yaml
osrm:
  image: osrm/osrm-backend
  container_name: delivery-osrm
  user: root
  volumes:
    - ./osrm_data:/data
  command: osrm-routed --algorithm mld /data/udon-thani.osrm
  ports:
    - "5001:5000"
  restart: unless-stopped
```

---

## 7. Polly Resilience (Backend → OSRM)

**File:** `BackendApi/Services/Ai/OsrmRoutingService.cs`

```csharp
// Polly Policy: 2 Retries + 15s Circuit Breaker + 1.5s Timeout
services.AddHttpClient<IOsrmRoutingService, OsrmRoutingService>()
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .RetryAsync(2))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(3, TimeSpan.FromSeconds(15)));
```

---

## 8. Google Polyline Compression

**File:** `BackendApi/Core/PolylineEncoder.cs`

- ก่อน: JSON coordinates array → **~22.5 KB**
- หลัง: Encoded polyline string → **~412 bytes (~99% reduction)**
- เก็บใน PostgreSQL field: `EncodedPolyline` (varchar/text)
- Frontend decode ด้วย pure TypeScript function (ดู `spec-frontend.md`)

---

## 9. Nginx Reverse Proxy

```
Port 8081 (nginx-proxy)
  /         → Angular frontend (port 80)
  /api      → Backend (port 80)
  /hubs     → Backend WebSocket (port 80) [WebSocket Upgrade headers required]
  /metrics  → Prometheus (port 9090)
```

---

## 10. Observability Stack

| Tool | Port | Purpose |
|---|---|---|
| Seq | 5341 (ingest), 8082 (UI) | Structured log viewer |
| Prometheus | 9090 | Metrics scrape + storage |
| Grafana | 3000 | Metrics dashboard |

**Backend Metrics endpoint:** `GET /metrics` (Prometheus format)  
**Log sink:** `Serilog → WriteTo.Seq("http://seq:5341")`

---

## 11. Quick Start Commands

```bash
# รันระบบทั้งหมด
docker-compose up -d --build

# Database migration (ถ้าไม่มี .NET SDK)
docker-compose exec backend dotnet ef database update

# ตรวจสอบ services
docker-compose ps

# ดู logs
docker-compose logs -f backend
docker-compose logs -f delivery-osrm

# รัน E2E Simulator
cd scripts/e2e-simulator
npm install
node simulate-e2e.js
```

---

## 12. Access URLs

| Service | Local Dev | Docker |
|---|---|---|
| Frontend | `http://localhost:4200` | `http://localhost` |
| Backend Swagger | `http://localhost:5000/swagger` | `http://localhost:5000/swagger` |
| AI Docs | `http://localhost:8000/docs` | `http://localhost:8000/docs` |
| Seq Logs | — | `http://localhost:8082` |
| Prometheus | — | `http://localhost:9090` |
| Grafana | — | `http://localhost:3000` |
| Database | `localhost:5432` | `db:5432` (internal) |
| Redis | `localhost:6379` | `redis:6379` (internal) |
| OSRM | `localhost:5001` | `osrm:5000` (internal) |
