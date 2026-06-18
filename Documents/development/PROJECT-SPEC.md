# Project Specification - Smart Delivery Routing System

**Document role:** Complete system specification for the Delivery project.

**Audience:** Engineering, QA, DevOps, product, and handoff teams.

**Last updated:** 2026-06-18

## 1. Document Scope

This document defines the expected behavior and technical requirements for the
whole system. It is intentionally broader than `AI-BLUEPRINT.md`.

Use this file for:

- system scope
- subsystem responsibilities
- API and realtime contract overview
- data ownership
- security rules
- non-functional requirements
- testing and operations requirements

For AI/dispatch architecture details, read `AI-BLUEPRINT.md`.

Canonical low-level contracts still live in `.docs/ai-context/`; this document
is the consolidated human-readable project spec.

## 2. Product Scope

The system supports realtime delivery operations across four roles:

| Role | Responsibilities |
|---|---|
| Customer | Browse shops, place orders, manage addresses, track delivery |
| StorePartner | Manage shop profile/menu, accept or reject orders |
| Rider | Receive offers, accept/reject work, send GPS, update delivery phase |
| Admin/Dispatcher | Monitor operations, riders, shops, orders, analytics, dispatch health |

## 3. High-Level Requirements

The system must create and manage orders, require store acceptance before
dispatch, rank rider candidates, enforce state machines, track riders in
realtime, store GPS history durably, route through local OSRM, provide degraded
fallback when AI/OSRM fails, keep dashboards reactive, expose standard JSON
errors, and provide logs/metrics for operations.

## 4. Technology Stack

| Area | Technology |
|---|---|
| Backend | ASP.NET Core 8 |
| ORM | EF Core 8, Npgsql, NetTopologySuite |
| Database | PostgreSQL 15 + PostGIS |
| Cache/locks | Redis |
| Event bus | RabbitMQ |
| Realtime | SignalR |
| AI Engine | Python FastAPI |
| Routing | Local OSRM |
| Admin | Angular 19, RxJS, Leaflet |
| Mobile/Web App | Flutter, Riverpod, GoRouter, Dio, SignalR client |
| Logs | Serilog + Seq |
| Metrics | Prometheus + Grafana + Alertmanager + exporters |
| Secrets | Vault when `VAULT_REQUIRED=true` |

Forbidden additions:

```text
Kafka
Kubernetes
full CQRS
Event Store
Saga Orchestrator
gRPC mesh
Redis Cluster
Elasticsearch
```

## 5. Runtime Services

Current compose topology:

```text
db
pgbouncer
backend
redis
ai-service
frontend
rider-app
osrm
nginx-proxy
seq
prometheus
grafana
alertmanager
rabbitmq
vault
vault-bootstrap
cadvisor
node-exporter
postgres-exporter
redis-exporter
```

Development ports:

```text
Backend:       5000 -> 80
PostgreSQL:    5432 -> 5432
PgBouncer:     6432 -> 5432
Redis:         6379 -> 6379
AI:            8009 -> 8000
Admin:         4201 -> 80
Flutter web:   8083 -> 80
OSRM:          5001 -> 5000
Seq UI:        8082 -> 80
Prometheus:    9090 -> 9090
Grafana:       3000 -> 3000
Alertmanager:  9093 -> 9093
RabbitMQ UI:  15672 -> 15672
Vault:         8200 -> 8200
```

## 6. Repository Layout

```text
BackendApi/
  Controllers/
  Core/
  Data/
  Features/
  Hubs/
  Infrastructure/
  ServiceMigration/
  Services/
  Setup/

admin-dashboard/
  src/app/

rider_app/
  lib/

ai-engine/

RootScripts/scripts.test/test/

Documents/
  development/
  handoff/
  infrastructure/
  setup/

.docs/ai-context/
```

## 7. Backend Specification

Backend owns authentication, authorization, REST APIs, order lifecycle, store
and menu management, rider state/location ingestion, dispatch orchestration,
SignalR events, RabbitMQ integration events, OSRM route calls, telemetry
history, and standard response/error formatting.

REST responses use:

```json
{
  "status": 200,
  "success": true,
  "message": "OK",
  "errorDetail": null,
  "code": null,
  "errors": null,
  "value": {}
}
```

Failures must have matching HTTP status and JSON body, including 401/403.

## 8. REST API Specification

Auth:

```text
POST /api/v1/auth/login
POST /api/v1/auth/register
POST /api/v1/auth/refresh
POST /api/v1/auth/logout
GET  /api/v1/auth/session
POST /api/v1/auth/change-password
```

Orders:

```text
POST   /api/v1/orders
GET    /api/v1/orders
GET    /api/v1/orders/customer
DELETE /api/v1/orders/customer/clear
GET    /api/v1/orders/{idOrTrackingCode}
GET    /api/v1/orders/my
GET    /api/v1/orders/shop
PATCH  /api/v1/orders/{id}/status
POST   /api/v1/orders/{id}/accept-by-store
POST   /api/v1/orders/{id}/reject-by-store
POST   /api/v1/orders/{id}/cancel
POST   /api/v1/orders/{id}/dispatch
POST   /api/v1/orders/batch-dispatch
```

Shops and menus:

```text
GET/POST/PUT/DELETE /api/v1/shops
GET/POST/PUT/DELETE /api/v1/menu-items
GET /api/v1/menu-items/shop/{shopId}
GET/POST/DELETE /api/v1/menu-categories
GET /api/v1/menu-categories/shop/{shopId}
```

Telemetry and routes:

```text
GET  /api/v1/rider-locations
GET  /api/v1/rider-locations/{riderId}/history
POST /api/v1/telemetry/gps
POST /api/v1/telemetry/gps/batch
GET  /api/v1/telemetry/config/mobile
POST /api/v1/telemetry/client-route-fallback
POST /api/v1/telemetry/client-events
POST /api/v1/rider-routes/resolve
```

## 9. Order And Rider State Specification

Order states:

```text
CREATED
MATCHING
OFFERING
ASSIGNED
PICKING_UP
DELIVERING
COMPLETED
CANCELLED
```

Valid order transitions:

```text
CREATED -> CREATED | MATCHING | CANCELLED
MATCHING -> CREATED | OFFERING | CANCELLED
OFFERING -> ASSIGNED | MATCHING | CANCELLED
ASSIGNED -> PICKING_UP | CANCELLED
PICKING_UP -> DELIVERING | CANCELLED
DELIVERING -> COMPLETED | CANCELLED
```

Rider states:

```text
OFFLINE
IDLE
RESERVED
BUSY
STALE
```

Valid rider transitions:

```text
OFFLINE -> IDLE
IDLE -> RESERVED | OFFLINE | STALE
RESERVED -> BUSY | IDLE | STALE
BUSY -> IDLE | OFFLINE | STALE
STALE -> IDLE | BUSY | OFFLINE
```

All transitions go through backend state machine services, and status broadcast
happens after persistence.

## 10. Realtime Specification

Hubs:

```text
/hubs/tracking
/hubs/chat
```

Client-to-server methods:

```text
UpdateLocation(lat, lng, accuracy)
UpdateRiderLocation(lat, lng)
UpdateHeartbeat()
UpdateStatus(riderState)
AcceptOffer(offerId, version)
RejectOffer(offerId, orderId)
```

Server events:

```text
OfferReceived
RiderLocationUpdated
ShopStatusChanged
DispatchScanStarted
DispatchCandidatesRanked
DispatchOfferSent
OrderStatusChanged
TelemetryUpdated
```

Groups:

```text
admins
rider:{riderId}
customer:{userId}
store:{shopId}
```

Payloads are camelCase. Rider location payload uses `state`, not `status`.

## 11. Dispatch Specification

Dispatch starts after store acceptance. It ranks candidates through AI and
fallback logic, locks riders while offering, sends offers through SignalR,
checks offer versions, transitions order/rider state consistently, and retries
next candidate on timeout or rejection.

Offer lifecycle:

```text
MATCHING -> OFFERING -> ASSIGNED
                     -> MATCHING on reject/timeout
                     -> CANCELLED when order cancelled
```

Redis locks are guardrails. PostgreSQL is the authority.

## 12. AI And Routing Specification

AI engine provides rider ranking, route optimization, ETA prediction, and
deterministic fallback.

Backend OSRM rules:

- use local OSRM only
- do not call public OSRM in production
- fallback locally to Haversine/raw coordinate logic
- protect route endpoints by rider/order ownership

Rider route resolution:

```text
POST /api/v1/rider-routes/resolve
```

Response fields:

```text
encodedPolyline
distanceMeters
durationSeconds
source = LOCAL_OSRM | HAVERSINE_FALLBACK
```

## 13. GPS And Telemetry Specification

Accuracy tiers:

```text
<= 50m
    Core telemetry. Allowed for dispatch/history/customer tracking.

> 50m and <= 300m
    Degraded admin-only telemetry. Show warning/accuracy circle only.

> 300m
    Reject.
```

Telemetry must support batch ingestion, PostgreSQL history, mobile offline queue
replay, and client fallback telemetry for degraded route drawing.

## 14. Flutter App Specification

Active roles:

- Rider
- Customer
- StorePartner

Routes:

```text
/login
/register
/
/delivery/active
/delivery/confirm/:orderId
/delivery/history
/tracking
/delivery/tracking/:orderId
/profile
/store
/store/orders
/store/summary
/store/profile
/customer
/customer/shop/:shopId
/customer/orders
/customer/profile
/customer/tracking/:orderId
/customer/addresses
/customer/addresses/map
```

Requirements:

- secure token storage on native
- single-flight refresh on 401
- stop GPS/SignalR and clear session when refresh fails
- mock GPS only in development
- real GPS and mock GPS must not run together
- offline GPS/status mutations sync FIFO
- rider navigation map draws active order route in app
- store menu changes refresh from backend-confirmed state

## 15. Admin Dashboard Specification

Routes:

```text
/login
/register
/dashboard
/map
/orders
/analytics
/riders
/shops
/customer
/store-partner
```

Requirements:

- Angular 19 standalone components
- HttpOnly-cookie dashboard auth
- `withCredentials` and XSRF token for protected requests
- SignalR reconnect without duplicate subscriptions
- Leaflet map with `preferCanvas: true`
- escaped popup content and programmatic event binding
- reactive updates without manual refresh
- admin map is operations visibility, not rider navigation

## 16. Data Specification

Main durable entities:

- `User`
- `Rider`
- `Shop`
- `MenuCategory`
- `MenuItem`
- `MenuItemOption`
- `MenuItemOptionItem`
- `Order`
- `OrderItem`
- `CustomerAddress`
- `RiderLocationHistory`
- `ProcessedEvent`
- `FcmToken`
- `ChatMessage`
- `DistributedLock`

Soft-delete entities use `ISoftDeletableEntity` / `IsDeleted`. Legacy
`DelFlag` fields are compatibility only. Business delete APIs must not hard
delete customer/order history.

Spatial rules:

- PostGIS points use SRID 4326
- longitude is `X`
- latitude is `Y`
- Leaflet receives `[lat, lng]`
- OSRM coordinates are `[lng, lat]`
- Google encoded polyline precision is 1e5

## 17. Database And Migration Specification

- EF migration baseline is kept minimal
- PostgreSQL-specific partition/index/view work belongs in `ServiceMigration`
- service migration scripts must be idempotent
- `ProcessedEvents.ProcessedAt` must be indexed
- production schema updates run through a controlled single runner
- routine partition maintenance must not become many hand-written EF migrations

## 18. Security Specification

- role policies for Admin, Dispatcher/Operations, Rider, Customer, StorePartner
- JWT bearer support for native clients
- HttpOnly cookie flow for admin dashboard
- XSRF protection for cookie-auth requests
- JSON auth error bodies for 401/403
- rate limiting for auth and telemetry
- secure headers: frame denial, nosniff, referrer policy, permissions policy
- no public OSRM fallback for GPS/route data in production
- never commit `.env`, tokens, JWT secrets, database passwords, or Vault secrets

## 19. Observability Specification

Logs must include when available:

```text
CorrelationId
OrderId
RiderId
```

Metrics and dashboards must cover order volume, active riders, dispatch
latency, AI latency, OSRM latency/fallback rate, RabbitMQ queue depth and DLQ,
Redis/PostgreSQL health, token refresh failures, rate limiting, and container
CPU/memory.

## 20. Infrastructure Specification

Nginx must route `/api/...` to backend, `/hubs/...` to backend SignalR, serve or
proxy frontend apps, cache rider web `/map-tiles/`, protect metrics, and apply
rate limits.

Prometheus targets:

```text
backend:80/metrics
rabbitmq:15692
cadvisor:8080
node-exporter:9100
postgres-exporter:9187
redis-exporter:9121
```

## 21. Testing Specification

Test location rule:

```text
RootScripts/scripts.test/test/
```

Allowed exception:

```text
Angular *.spec.ts beside Angular components
```

Load/stress logs must be stored under:

```text
LogsTest/YYYY-MM-DD/
```

with standard names:

```text
stage5_stats.csv
stage5_run.log
stage5_final_report.md
```

## 22. Non-Functional Requirements

Operational targets:

- max SignalR connections target: 500
- max GPS ingestion target: 100/sec
- max telemetry payload: 16 KB
- RabbitMQ processing lag target: less than 3 seconds
- admin telemetry summary stream target: about 0.5 Hz
- dashboard map must avoid memory leaks and duplicate subscriptions

Reliability:

- deterministic fallback for AI and routing
- idempotent RabbitMQ consumers
- bounded retry and DLQ
- PostgreSQL fallback for Redis-derived state recovery
- no business logic in SignalR hubs

## 23. Known Implementation Gaps

These are product/implementation follow-ups, not spec relaxations:

- decide whether `20260615045328_AddDispatchAttemptsToOrder` should be folded
  into a verified migration baseline later
- replace any remaining mock store analytics data if StorePartner summary is
  expected to be production-grade
- continue tightening UI consistency between customer app, store app, rider app,
  and admin dashboard when backend status contracts evolve

## 24. Acceptance Criteria

A release candidate should satisfy:

- backend build passes
- backend unit/integration tests pass
- AI tests pass
- admin build passes
- Flutter app builds for target platform
- order create -> store accept -> dispatch -> rider accept -> pickup ->
  delivering -> completed flow passes
- admin dashboard receives realtime updates without manual refresh
- rider map draws active route after accepting order
- GPS degraded accuracy is not used by dispatch
- 401/403 responses return JSON body
- no public OSRM fallback is used in production

## 25. Related Documents

- `AI-BLUEPRINT.md` - AI/dispatch architecture blueprint
- `Documents/handoff/README.md` - subsystem handoff pack
- `.docs/ai-context/contracts/api-contracts.md`
- `.docs/ai-context/contracts/signalr-contracts.md`
- `.docs/ai-context/contracts/state-machine.md`
- `.docs/ai-context/spec-backend.md`
- `.docs/ai-context/spec-mobile-rider.md`
- `.docs/ai-context/spec-frontend.md`
- `.docs/ai-context/spec-infra-devops.md`
