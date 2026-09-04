# Backend API (.NET 8)

**Version:** 1.0.0 | **Updated:** 2026-06-14

## 1. Stack

ASP.NET Core 8, EF Core/Npgsql/NetTopologySuite, System.Text.Json, Mapster,
SignalR, StackExchange.Redis, RabbitMQ, Serilog/Seq และ FluentValidation

## 2. Ownership

```text
BackendApi/
  Controllers/              HTTP transport only
  Hubs/                     SignalR transport only
  Core/                     shared contracts, filters, state machines, helpers
  Services/                 cross-feature application services/workers
  Features/
    AiRouting/              route optimizer compatibility client, OSRM client and fallback
    DispatchManagement/     dispatch and state orchestration
    FleetTracking/          telemetry ingestion/tracking
  Infrastructure/
    EventBus/               RabbitMQ + integration events
    Redis/                  presence, locks, operational cache
  Data/                     EF DbContext
  Setup/                    DI and middleware
```

Controllers/Hubs ห้ามทำ business mutation โดยตรงและห้าม inject DbContext
service/worker ใช้ DbContext ได้เมื่อจำเป็นต่อ transaction, batching หรือ spatial query

## 3. API Responses

Application REST responses ใช้ `ApiResponse`:

```json
{
  "status": 401,
  "success": false,
  "message": "Unauthorized",
  "errorDetail": null,
  "code": "UNAUTHORIZED",
  "errors": null,
  "value": null
}
```

HTTP status line ต้องตรงกับ `status` ใน body. Authentication challenge/forbid,
exception, validation และ CSRF middleware ต้องเขียน JSON body มาตรฐานด้วย
ข้อยกเว้น wrapper ดู `runtime-rules.md`

## 4. Data And Concurrency

- PostgreSQL 15 + PostGIS, `geometry(Point,4326)`, GiST indexes
- Base entities มี audit/soft-delete fields
- Concurrency authority คือ PostgreSQL shadow `xmin` ที่ map ด้วย `IsRowVersion()`
- public `byte[] RowVersion` คงไว้เพื่อ compatibility แต่ไม่ใช่ DB authority
- `RefNumber` ใช้ identity + unique index; API ใช้ `TrackingCode` เป็น DTO name
- `ProcessedEvents` ใช้ composite key `(EventId, HandlerName)` และมี B-tree
  index ที่ `ProcessedAt`

## 5. Migration Policy

- Active EF baseline: `20260614152246_ConsolidatedBaseline20260614`
- โฟลเดอร์ `Migrations` ต้องคงเป็น baseline + Designer + Snapshot
- PostgreSQL-specific partition/index/compatibility DDL อยู่ใน service migration
  ที่ idempotent
- ห้ามเพิ่ม hand-written EF migration หลายไฟล์เพื่อ routine partition maintenance
- ก่อน squash ต้องทดสอบ fresh database, existing database history bridge,
  build และ integration tests

## 6. Tracking And SignalR

`TrackingHub` partial classesทำเฉพาะ authenticate, validate และ route ไป:

- `RiderPresenceManager`
- `TelemetryService`
- `DispatchOfferHandler`
- service ที่รับผิดชอบ state

GPS current state เก็บใน Redis เพื่อ realtime, history persist ผ่าน telemetry/RabbitMQ
pipeline และ PostgreSQL ห้ามอ้าง `GpsSyncBuffer` รุ่นเก่า

## 7. Routing And Route Optimization

- `IAiService`: legacy backend interface name for route optimizer calls; rank, optimize, predict ETA พร้อม deterministic fallback
- `OsrmRoutingService`: local OSRM เท่านั้น; ห้าม public OSRM production fallback
- Haversine/raw geometry fallback ต้องคงไว้ตาม critical registry
- OSRM trip `waypoint_index` คือ visit-order value ต่อ input waypoint;
  ผู้เรียกต้องเปรียบเทียบ `seq[inputIndex]` ไม่ใช้ `IndexOf(inputIndex)`

## 8. Spatial Rules

- PostGIS Point: `X=lng`, `Y=lat`, SRID 4326
- Database proximity/filter ใช้ spatial SQL/index
- Haversine ใช้ได้สำหรับ in-memory weighted heuristic หรือ degraded fallback
- `RiderLocationHistories` partition/index maintenance เป็น infrastructure concern

## 9. Security And Observability

- Dashboard: HttpOnly access/refresh cookies, XSRF double-submit, credentials enabled
- Flutter/native: Bearer JWT และ refresh token storage ที่ปลอดภัย
- SignalR รองรับ `access_token` query สำหรับ WebSocket clients
- Role policies: Admin, Dispatcher/Operations, Rider, Customer, StorePartner
- `/health`, `/health/ready`, `/health/detail`, `/metrics`
- Structured log scope ต้องมี correlation/order/rider identifiers เมื่อมีค่า
