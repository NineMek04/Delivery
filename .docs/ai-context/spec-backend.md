---
scope: Backend API Layer (.NET 8)
source_of_truth:
  - PROJECT-SPEC.md (Sections 4-6, Backend Packages, Project Structure, Security)
  - AI-BLUEPRINT.md (Section 2, Technology Stack)
  - AI-CHANGELOG.md (2026-05-13 TrackingHub, 2026-05-14 Auth, 2026-05-18 Spatial Scaling)
  - BackendApi/ (codebase)
related_contexts:
  - .docs/ai-context/contracts/signalr-contracts.md
  - .docs/ai-context/contracts/api-contracts.md
  - .docs/ai-context/contracts/redis-keys.md
  - .docs/ai-context/contracts/state-machine.md
forbidden_patterns:
  - business logic inside controllers
  - direct DbContext inject in Controllers or Hubs
  - services ใน Controllers/Services/ (ต้องอยู่ใน BackendApi/Services/)
  - เรียก Database โดยไม่ผ่าน DBHandlerCore
  - ไม่ wrap response ด้วย ApiResponse<T>
known_pitfalls:
  - SignalR reconnect race condition → ต้องมี GpsSyncBuffer
  - Redis TTL 30s ต้องตรงกับ DispatchTimeoutWorker check interval
  - GiST index ห้ามใส่บน non-geometry column (ทำให้ migration ล้มเหลว)
  - RowVersion (bytea) ต้องมี DEFAULT '\\x'::bytea ใน PostgreSQL
  - RiderLocationHistories เป็น Partitioned Table → PartitionMaintenanceWorker ต้องสร้าง partition ล่วงหน้า
  - JWT via QueryString สำหรับ SignalR WebSocket connection
---

# spec-backend.md — Backend API Layer (.NET 8)

> **Source**: `PROJECT-SPEC.md` (Sec 4-6) + `AI-BLUEPRINT.md` + `AI-CHANGELOG.md`  
> **For SignalR payloads** → `contracts/signalr-contracts.md`  
> **For REST endpoints** → `contracts/api-contracts.md`

---

## 1. Tech Stack

| Layer | Technology | Version |
|---|---|---|
| Framework | ASP.NET Core | .NET 8 |
| ORM | EF Core + NetTopologySuite | 8.0.11 |
| Spatial | Npgsql + PostGIS | 8.0.11 |
| Mapper | Mapster | — |
| Real-time | SignalR | Built-in |
| Cache | StackExchange.Redis | — |
| Auth | JWT Bearer + Refresh Token | 8.0.11 |
| Logging | Serilog (File + Seq Sinks) | 10.0.0 |
| Validation | FluentValidation | — |
| JSON | Newtonsoft.Json (for spatial) | — |

---

## 2. Project Structure (สำคัญ)

```
BackendApi/
├── Core/
│   ├── Models/
│   │   ├── ApiResponse.cs           ← Standard JSON Wrapper (ทุก response ต้องใช้)
│   │   └── PaginatedResult.cs       ← Pagination model
│   ├── Filters/
│   │   ├── GlobalResponseFilter.cs  ← Auto-wrap ทุก response ด้วย ApiResponse<T>
│   │   ├── GlobalExceptionFilter.cs
│   │   └── ValidationFilter.cs      ← Auto-validate ด้วย FluentValidation
│   ├── Mappings/
│   │   └── MappingConfig.cs         ← Mapster: Point ↔ Lat/Lng, Entity ↔ DTO
│   ├── CrudControllerBase.cs        ← Generic CRUD Base (ใช้สำหรับ Master Data)
│   └── DeliveryControllerBase.cs    ← Base สำหรับ custom business logic
├── Controllers/
│   ├── MasterData/
│   │   ├── RidersController.cs      ← extends CrudControllerBase
│   │   └── ShopsController.cs       ← extends CrudControllerBase
│   └── Business/
│       ├── AuthController.cs        ← Login, Logout, Refresh
│       └── OrdersController.cs      ← Order lifecycle + dispatch trigger
├── Hubs/
│   └── TrackingHub.cs               ← SignalR Hub (GPS + Dispatch events)
├── Services/                        ← Business logic HARUS ada di sini
│   ├── Auth/AuthService.cs
│   ├── Dispatch/
│   │   ├── DispatchService.cs       ← Dispatch orchestration
│   │   └── StateMachineService.cs   ← Order/Rider state transitions
│   └── Ai/
│       ├── AiService.cs             ← Typed HttpClient → AI Engine
│       └── OsrmRoutingService.cs    ← OSRM + Polly fallback
├── Infrastructure/
│   ├── Redis/
│   │   ├── RedisLockService.cs      ← SETNX Distributed Lock
│   │   └── RiderPresenceService.cs  ← GeoAdd/GeoRadius for nearby riders
│   └── Background/
│       ├── GpsSyncBuffer.cs         ← In-memory GPS buffer
│       ├── GpsSyncWorker.cs         ← Batch flush → PostGIS every 30s
│       ├── DispatchTimeoutWorker.cs ← Janitor: re-dispatch expired offers
│       ├── HeartbeatMonitor.cs      ← Detect ghost/offline riders
│       └── PartitionMaintenanceWorker.cs ← Auto-create monthly partitions
├── Security/
│   ├── JwtTokenService.cs
│   ├── PasswordHasher.cs           ← PBKDF2
│   └── LoginAttemptService.cs
├── Data/
│   ├── ApplicationDbContext.cs
│   └── MockData/DataSeeder.cs      ← Seed users, riders, orders (อุดรธานี)
└── Setup/
    ├── ServiceSetup.cs              ← DI registration
    └── ApplicationSetup.cs         ← Middleware pipeline (CORS ต้องอยู่บนสุด!)
```

---

## 3. DBHandlerCore — Required Usage Pattern

**ห้ามใช้** `_context.SomeEntity.Where(...)` โดยตรง — ต้องใช้ผ่าน `DBHandlerCore`:

```csharp
// ✅ ถูกต้อง
var riders = await DB.GetObjectListAsync<Rider>();
var rider = await DB.GetObjectByKeyAsync<Rider>(id);
await DB.InsertObject(entity);
await DB.UpdateObject(entity);
await DB.CommitChangesAsync();
var paginated = await DB.GetPaginatedListAsync<Rider>(page, pageSize);

// Custom query
var query = DB.GetQuery<Rider>().Where(r => r.State == RiderState.IDLE);
```

---

## 4. Base Entity Hierarchy

```
BaseEntity<T>           → มี RowVersion (Concurrency Token bytea)
  └── BaseAuditableEntity<T>  → เพิ่ม CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IP
        └── BaseSoftDeleteEntity<T>  → เพิ่ม IsDeleted, DeletedAt, DeletedBy, DeletedFromIp
```

**Global Query Filter:** ซ่อน `IsDeleted = true` อัตโนมัติใน query ปกติ  
**Filtered Unique Index:** Email check เฉพาะ `IsDeleted = false`  
**Concurrency:** `RowVersion` ป้องกัน overwrite ใน real-time system

---

## 5. TrackingHub (SignalR) — Key Behaviors

**Registration:**
- Rider เข้าร่วม group `"rider:{riderId}"`
- Admin/Dispatcher เข้าร่วม group `"admins"`
- Customer เข้าร่วม group `"customer:{userId}"`
- StorePartner เข้าร่วม group `"stores"`

**JWT Auth:** SignalR รับ JWT ผ่าน `?access_token=` query string  
(เพราะ WebSocket ไม่รองรับ Authorization header)

**GPS Flow:**
1. Rider call `UpdateLocation(lat, lng, accuracy)`
2. Hub บันทึก → `GpsSyncBuffer` (in-memory)
3. ตรวจสอบ GPS Sanity: max drift 5km
4. อัปเดต `Rider.CurrentLocation` ลง PostgreSQL ทันที (สำหรับ AI ranking)
5. Broadcast `RiderLocationUpdated` ไปยัง "admins"
6. `GpsSyncWorker` flush bulk → `RiderLocationHistories` ทุก 30s

---

## 6. RefNumber System (Tracking Codes)

Format: `PREFIX-NNNNNN` (เลขลำดับ 6 หลัก)

| Entity | Prefix | ตัวอย่าง |
|---|---|---|
| Order | `ORD-` | `ORD-000001` |
| Rider | `RID-` | `RID-000003` |
| Shop | `SHP-` | `SHP-000002` |
| User | `USR-` | `USR-000001` |

**Implementation:** `UseIdentityByDefaultColumn()` + Unique Index  
**Search:** `TrackingSearchService` รองรับ mixed-key search (UUID หรือ RefNumber)  
**Performance:** O(1) ด้วย Unique Index, O(log N) ด้วย B-tree

---

## 7. PostGIS Spatial Configuration

```csharp
// Entity Configuration
entity.Property(e => e.CurrentLocation)
    .HasColumnType("geometry(Point, 4326)");

// GiST Index (ต้องมีทุก geometry column)
entity.HasIndex(e => e.CurrentLocation)
    .HasMethod("gist")
    .HasDatabaseName("IX_Riders_CurrentLocation_Gist");

// EF Core Query (ใช้ ST_Distance)
var nearbyRiders = await DB.GetQuery<Rider>()
    .Where(r => r.CurrentLocation.Distance(orderPoint) < radiusInDegrees)
    .OrderBy(r => r.CurrentLocation.Distance(orderPoint))
    .ToListAsync();
```

**ห้ามใช้** Haversine C# ใน Backend — ใช้ PostGIS `.Distance()` แทน

**RiderLocationHistories** เป็น **Monthly Range Partitioned Table**:
- Parent table: `RiderLocationHistories`
- Child partitions: `RiderLocationHistories_2026_05`, `RiderLocationHistories_2026_06`, ...
- `PartitionMaintenanceWorker` สร้าง partition ล่วงหน้าอัตโนมัติที่ startup และ 02:00 UTC ทุกวัน

---

## 8. Security Configuration

| Feature | Implementation |
|---|---|
| JWT Auth | Bearer token + HttpOnly Cookie `access_token` |
| Refresh Token | SHA-256 hash, 7-day lifetime, Token Rotation |
| Role Policies | `AdminOnly`, `Operations`, `Rider` |
| Rate Limiting | `auth` policy (brute-force prevention) |
| Security Headers | `Referrer-Policy`, `X-Frame-Options` via Middleware |
| CORS | **ต้อง** อยู่บนสุดของ Middleware pipeline |

---

## 9. AI Integration

**`AiService`** (Typed HttpClient) เชื่อมต่อกับ Python AI Engine:

```csharp
// VRP Optimization
await _aiService.OptimizeRouteAsync(orderLocations);

// Rider Ranking
await _aiService.RankRidersAsync(candidates, orderLocation);
```

**`OsrmRoutingService`** คำนวณเส้นทางจริง:
- Local OSRM → Public OSRM → Haversine fallback
- Polly: 2 retries + 15s Circuit Breaker + 1.5s timeout
- `PolylineEncoder.cs` compress route coordinates (~99% reduction)

---

## 10. Health Checks & Observability

| Endpoint | Purpose |
|---|---|
| `/health` | Basic liveness |
| `/health/ready` | Database + Redis readiness |
| `/health/detail` | Full metrics (DB latency, Redis, SignalR, Dispatch queue) |
| `/metrics` | Prometheus metrics scrape |

**Structured Logging:** Serilog → File + Seq (`http://seq:5341`)
