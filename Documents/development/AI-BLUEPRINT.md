# AI Blueprint - Smart Delivery Routing System

**Document role:** Architecture blueprint for AI-assisted dispatch, routing,
telemetry, and realtime delivery coordination.

**Audience:** Backend, AI, mobile, frontend, QA, and DevOps teams that need to
understand how the intelligent routing system is designed.

**Last updated:** 2026-06-18

## 1. Blueprint Scope

This file explains the design of the AI and dispatch architecture. It is not a
full project specification and it is not a changelog. For complete API,
frontend, mobile, and infrastructure requirements, use `PROJECT-SPEC.md`.

Canonical implementation contracts still live under `.docs/ai-context/`.
This blueprint is the human-readable architecture map derived from those active
contracts and the current codebase.

## 2. System Intent

The system is an AI-assisted delivery platform that connects customers,
stores, riders, and operations staff in realtime. The core intelligence problem
is not only "find a nearby rider"; it is:

- rank riders using location, availability, current workload, and route cost
- avoid assigning inaccurate GPS points into dispatch decisions
- keep rider/order state consistent across REST, SignalR, Redis, PostgreSQL,
  and RabbitMQ
- calculate road-aware routes through local OSRM
- fall back safely when AI or OSRM is degraded
- keep the admin map and rider app reactive without flooding the browser or
  backend

## 3. Architecture Principles

### 3.1 Source Of Truth

PostgreSQL/PostGIS is the durable source of truth. Redis is a speed layer for
presence, locks, route cache, and current operational state. Redis values must
be reconstructable from PostgreSQL or realtime updates.

### 3.2 Pure Transport Hubs

SignalR hubs transport messages only. `TrackingHub` authenticates, validates,
routes to groups, and delegates to services. It must not contain business state
mutation rules.

### 3.3 Local-First Routing

Routing uses local OSRM only. Public OSRM fallback is forbidden for production
GPS or order route data because it can leak location information. When local
OSRM is unavailable, backend fallback must remain local: Haversine/raw geometry
with explicit degraded source metadata.

### 3.4 Deterministic Fallback

AI is an optimizer, not the only path to delivery. Any AI ranking, ETA, or route
operation must have deterministic fallback behavior so orders do not become
stuck when AI is unavailable.

### 3.5 Bounded Complexity

The architecture intentionally uses PostgreSQL/PostGIS, Redis, RabbitMQ,
SignalR, local OSRM, and FastAPI. Do not introduce Kafka, Kubernetes, full CQRS,
Event Store, Saga Orchestrator, gRPC mesh, Redis Cluster, or Elasticsearch.

## 4. Runtime Topology

```text
Flutter App (Rider / Customer / StorePartner)
        |
        | REST /api/v1
        | SignalR /hubs/tracking and /hubs/chat
        v
BackendApi (.NET 8)
        |
        +--> PostgreSQL/PostGIS
        |       Durable orders, riders, shops, users, GPS history,
        |       processed integration events, soft-delete records
        |
        +--> Redis
        |       Rider presence, current location, dispatch locks,
        |       route cache, operational speed layer
        |
        +--> RabbitMQ
        |       Integration events, async telemetry persistence,
        |       background worker handoff, DLQ
        |
        +--> AI Engine (FastAPI)
        |       Candidate ranking, route optimization, ETA prediction
        |
        +--> Local OSRM
                Road routes, nearest-road snap, trip sequence

Angular Admin Dashboard
        |
        +--> REST /api/v1 for operational queries
        +--> SignalR /hubs/tracking for realtime map/order updates
```

## 5. Core Components

### 5.1 Backend API

Backend API owns authentication, order lifecycle, dispatch orchestration,
SignalR events, RabbitMQ events, OSRM route calls, and telemetry persistence.

Important implementation areas:

```text
BackendApi/
  Features/AiRouting/
  Features/DispatchManagement/
  Features/FleetTracking/
  Services/Orders/
  Services/Dispatch/
  Services/BackgroundWorkers/
  Hubs/Tracking/
  Infrastructure/EventBus/
  Infrastructure/Redis/
```

### 5.2 AI Engine

AI engine owns algorithmic computation:

- rider candidate scoring
- VRP-style route optimization
- ETA prediction
- degraded heuristic response when heavy optimization fails

FastAPI endpoints that perform CPU-bound work must be `def`, not `async def`,
so FastAPI runs them in the thread pool.

### 5.3 Local OSRM

OSRM owns route distance, duration, nearest-road snap, and trip sequence.

Backend configuration:

```text
Routing__LocalOsrmUrl=http://osrm:5000
Development host port=http://localhost:5001
```

### 5.4 Redis

Redis stores volatile operational data:

- rider current location
- rider heartbeat/presence
- dispatch locks and offer reservations
- route cache
- short-lived realtime lookup data

Redis must not be treated as the authority for historical GPS, order state, or
final rider state recovery.

### 5.5 RabbitMQ

RabbitMQ carries integration events across async processing boundaries. Every
consumer must check `ProcessedEvents` before executing business logic.

Event naming rule:

```text
<Domain><Action>IntegrationEvent
```

Examples:

```text
OrderCreatedIntegrationEvent
OrderStatusChangedIntegrationEvent
```

## 6. Dispatch Blueprint

Dispatch must choose a rider who is available, close enough by route cost, using
accurate enough GPS, not locked by another active offer, and compatible with the
current order/batch constraints.

```text
Customer creates order
        |
        v
Order state: CREATED
        |
Store accepts order
        |
        v
Order state: MATCHING
        |
Backend queues dispatch task
        |
        v
Candidate discovery from rider state/location
        |
        v
AI ranking with deterministic fallback
        |
        v
Acquire rider lock and persist offer
        |
        v
Order state: OFFERING
Rider state: RESERVED
        |
        v
SignalR OfferReceived -> rider:{riderId}
        |
        +--> Accept: OFFERING -> ASSIGNED, RESERVED -> BUSY
        |
        +--> Reject/timeout: OFFERING -> MATCHING, RESERVED -> IDLE,
             then try next candidate
```

Offer invariants:

- accept must check offer ID and version
- dispatch locks must expire if rider does not respond
- reject and timeout must release or move reservations safely
- Redis locks are guardrails; PostgreSQL state is authority
- admin dispatch events deduplicate by `order.id + dispatchAttempt`

## 7. Route Intelligence

### 7.1 Order Route Creation

When an order is created, backend derives pickup coordinates from the shop and
dropoff coordinates from the customer request. Backend asks local OSRM for
road-aware route details and stores route distance, duration, and encoded
polyline when available.

If local OSRM fails, backend must fall back locally. It must not call public
OSRM in production.

### 7.2 Rider Route Resolution

The rider app calls backend instead of calling OSRM directly:

```text
POST /api/v1/rider-routes/resolve
```

Backend verifies assigned-rider ownership, calls local OSRM, and returns:

```json
{
  "encodedPolyline": "...",
  "distanceMeters": 1200,
  "durationSeconds": 300,
  "source": "LOCAL_OSRM"
}
```

If OSRM is unavailable, source can be `HAVERSINE_FALLBACK`. Flutter may draw a
straight line only as the final fallback and must report the fallback through:

```text
POST /api/v1/telemetry/client-route-fallback
```

### 7.3 Multi-Stop Trip Ordering

OSRM trip response `waypoint_index` is a visit-order value per input waypoint.
Callers must compare `seq[inputIndex]`. They must not use `seq.IndexOf(inputIndex)`.

## 8. GPS Accuracy Model

```text
accuracy <= 50m
    Core telemetry. Can enter dispatch, history, customer tracking.

50m < accuracy <= 300m
    Degraded admin-only telemetry. Can show an accuracy circle on admin map.
    Must not be used by dispatch or customer ETA.

accuracy > 300m
    Reject.
```

This prevents noisy mobile GPS from corrupting AI dispatch decisions while still
giving operations staff useful visibility.

## 9. State Machines

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

`Recover` is a transition reason, not a rider state. Recovery resolves to
`IDLE` or `BUSY` based on active orders in PostgreSQL.

## 10. Realtime Blueprint

SignalR hubs:

```text
/hubs/tracking
/hubs/chat
```

`/hubs/tracking` is used for dispatch offers, GPS, order status, and admin
operations events. `/hubs/chat` is separate and must not carry GPS telemetry.

Groups:

```text
admins
rider:{riderId}
customer:{userId}
store:{shopId}
```

Canonical events:

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

Payloads are camelCase. `RiderLocationUpdated` uses `state`, not `status`.

## 11. Mobile Blueprint

The Flutter app has Rider, Customer, and StorePartner flows. Rider navigation
belongs in the Flutter app. Admin map is operational visibility, not turn-by-turn
rider navigation.

During active delivery:

```text
ASSIGNED/PICKING_UP -> rider-to-pickup route
DELIVERING          -> pickup-to-dropoff route
```

The map follows the rider at navigation zoom around 17.5. Full-route fit is only
for overview states, not continuous navigation.

## 12. Admin Blueprint

Admin dashboard is an operations console for active riders, orders by state,
dispatch scan visibility, rider accuracy circles, shop/rider management, and
analytics.

Map rules:

- `preferCanvas: true`
- escape popup content
- programmatic event binding only
- update affected markers/layers reactively
- avoid repeated full-map scans when backend events identify changes

## 13. Observability Blueprint

Logs must include, when available:

```text
CorrelationId
OrderId
RiderId
```

Metrics should cover active orders, active riders, dispatch attempts, AI
latency, OSRM latency, fallback counts, RabbitMQ queue depth, rate limiting, and
token refresh failures.

## 14. Security And Privacy Boundaries

- Dashboard uses HttpOnly cookies and XSRF protection.
- Native app uses bearer JWT with secure storage.
- SignalR websocket clients may use `access_token`.
- GPS data must not be sent to public OSRM in production.
- Degraded GPS is admin-only, not precise customer tracking.
- Business records use soft-delete where contracts require history retention.

## 15. Blueprint Decisions

| Decision | Reason |
|---|---|
| PostgreSQL/PostGIS is source of truth | Durable relational and spatial data |
| Redis is speed layer only | Prevents cache drift from becoming authority |
| RabbitMQ integration events | Async delivery without Kafka complexity |
| Local OSRM only | Privacy and predictability |
| Haversine fallback | Safe local degraded routing |
| SignalR pure transport | Keeps realtime layer testable |
| Accuracy tiers | Avoids corrupting dispatch with noisy GPS |
| Lightweight compensation | Avoids full Saga Orchestrator complexity |

## 16. Related Documents

- `PROJECT-SPEC.md` - complete system specification
- `Documents/handoff/README.md` - team handoff pack
- `.docs/ai-context/spec-ai-engine.md` - active AI implementation constraints
- `.docs/ai-context/spec-backend.md` - active backend constraints
- `.docs/ai-context/contracts/state-machine.md` - state contract
- `.docs/ai-context/contracts/signalr-contracts.md` - realtime contract
- `.docs/ai-context/contracts/api-contracts.md` - REST contract
