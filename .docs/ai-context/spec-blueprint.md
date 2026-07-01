# Active Architecture Blueprint

**Version:** 1.0.0 | **Updated:** 2026-06-14

## 1. Active Stack

- PostgreSQL 15 + PostGIS, SRID 4326; PostgreSQL เป็น source of truth
- PgBouncer แบบ transaction pooling
- Redis 7 สำหรับ presence, GEO, cache และ distributed locks เท่านั้น
- RabbitMQ 3 สำหรับ integration events
- ASP.NET Core SignalR สำหรับ realtime transport
- .NET 8 Backend API, Angular 19 admin dashboard, Flutter multi-role app
- Python 3.11 FastAPI + OR-Tools
- Local OSRM MLD; ห้ามส่งพิกัด production ไป public OSRM
- Seq, Prometheus, Grafana และ Alertmanager สำหรับ observability
- Vault AppRole สำหรับ production secrets

## 2. Event Boundaries

ชื่อและหน้าที่ต้องไม่ปนกัน:

1. **Domain Events**: ภายใน bounded context/process
2. **Integration Events**: ข้าม component ผ่าน RabbitMQ ชื่อ
   `<Domain><Action>IntegrationEvent`
3. **Telemetry Events**: realtime/high-frequency ผ่าน SignalR หรือ telemetry pipeline
   ชื่อ `<Subject><Action>TelemetryEvent` เมื่อเป็นชนิดข้อมูลในโค้ด

RabbitMQ consumer ทุกตัวต้องตรวจ `ProcessedEvents` ก่อนทำ side effect และ index
`IX_ProcessedEvents_ProcessedAt` ต้องคงอยู่สำหรับ cleanup งานปริมาณสูง

## 3. Order Flow

```text
Customer creates order
  -> Store accepts
  -> CREATED -> MATCHING -> OFFERING
  -> Rider accepts -> ASSIGNED
  -> PICKING_UP -> DELIVERING -> COMPLETED
```

- Store rejection/cancellation ต้องผ่าน service และ state machine
- Dispatch ใช้ PostGIS/Redis candidate discovery, weighted heuristic ranking และ local OSRM
- Route optimizer/OSRM failure ต้องมี deterministic fallback ตาม critical registry
- ทุก state change สำคัญต้อง persist ใน PostgreSQL ก่อน broadcast

Current naming rule: dispatch ranking is weighted heuristic ranking, not a
trained AI/ML model. Route sequencing uses mathematical optimization and local
OSRM where available, with deterministic fallback required for ranking,
optimization, and OSRM failures.

## 4. GPS Flow

```text
Flutter location
  -> SignalR UpdateLocation or REST batch queue
  -> TrackingHub validates/authenticates/routes only
  -> TelemetryService / RabbitMQ pipeline
  -> Redis current operational state
  -> PostgreSQL history
  -> SignalR broadcast to authorized groups
```

`TrackingHub` เป็น Pure Transport Layer ห้ามมี business state mutation หรือ query
orchestration ฝังใน Hub

## 5. Resilience Rules

- PostgreSQL เป็น final authority; Redis eviction ต้องมี DB fallback
- Mobile mutation ที่เปลี่ยนสถานะงานต้อง queue ใน SQLite เมื่อ offline และ replay
  แบบ idempotent ตามลำดับ
- Local OSRM ล่มได้ แต่ระบบต้องไม่ส่งพิกัดไป public routing service
- Logs ของ flow ต้องมี `CorrelationId`, `OrderId` และ `RiderId` เมื่อมีค่า
- ห้ามเพิ่ม stack ใน forbidden list ของ `AGENTS.md`
