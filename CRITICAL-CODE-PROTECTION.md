# Critical Code Protection Registry

**Version:** 1.1.0 | **Last Updated:** 2026-06-14

กฎบังคับอยู่ใน `AGENTS.md §7`: ห้ามลบไฟล์, comment-out business logic,
เปลี่ยน public contract, ลบ fallback หรือเอา state transition/router เดิมออก.
Line number ไม่ถือเป็น contract เพราะเปลี่ยนได้ทุก commit.

## Tier 1: System Critical

| # | File | Protected contract |
|---:|---|---|
| 1 | `ai-engine/app/core/vrp_solver.py` | `solve_vrp`, `compute_distance_matrix`, pickup/delivery precedence, timeout |
| 2 | `ai-engine/app/core/scoring.py` | `rank_candidates` |
| 3 | `ai-engine/app/core/geo_utils.py` | `haversine_distance`, `calculate_bearing`, `is_same_direction` |
| 4 | `ai-engine/app/api/v1/endpoints/optimize.py` | synchronous `optimize_route` |
| 5 | `ai-engine/app/api/v1/endpoints/dispatch.py` | synchronous `rank_dispatch_candidates` |
| 6 | `ai-engine/app/api/v1/endpoints/predict.py` | synchronous `predict_eta` |
| 7 | `ai-engine/app/api/v1/api.py` | dispatch and prediction router registrations |
| 8 | `ai-engine/main.py` | v1/optimize router registration and `/health` |
| 9 | `BackendApi/Features/AiRouting/IAiService.cs` | all three async methods and signatures |
| 10 | `BackendApi/Features/AiRouting/AiService.cs` | rank/optimize/predict calls and all fallbacks |
| 11 | `BackendApi/Controllers/Business/AiController.cs` | backend AI proxy actions |
| 12 | `BackendApi/Features/AiRouting/OsrmRoutingService.cs` | route, snap, trip sequence and degraded fallback |
| 13 | `BackendApi/Features/DispatchManagement/DispatchService.cs` | dispatch orchestration and rider offer flow |
| 14 | `BackendApi/Hubs/TrackingHub.cs` and partials | connection/auth/transport methods; Hub must stay transport-only |
| 15 | `BackendApi/Setup/ServiceSetup.cs` | AI/OSRM HttpClient and required DI registrations |
| 16 | `BackendApi/Setup/ApplicationSetup.cs` | `/hubs/tracking` mapping and middleware order |

## Tier 2: Feature Critical

| # | File | Protected behavior |
|---:|---|---|
| 17 | `BackendApi/Core/StateMachines/OrderState.cs` | enum and all existing transition rules |
| 18 | `BackendApi/Core/StateMachines/RiderState.cs` | enum, reasons and all existing transitions |
| 19 | `BackendApi/Features/DispatchManagement/StateMachineService.cs` | persisted transitions, recipient cache, integration event |
| 20 | `BackendApi/Infrastructure/EventBus/RabbitMqEventBus.cs` | publish/consume reliability |
| 21 | `BackendApi/Infrastructure/EventBus/Events/IntegrationEvents.cs` | cross-system integration event contracts/naming |
| 22 | `BackendApi/Core/Events/DispatchEvents.cs` | in-process domain events; do not rename as integration events |
| 23 | `BackendApi/Infrastructure/Redis/RiderPresenceService.cs` | GEO, heartbeat, latest GPS and DB-safe behavior |
| 24 | `BackendApi/Infrastructure/Redis/RedisLockService.cs` | ownership-safe distributed lock |
| 25 | `BackendApi/Core/DataHandlers/DBHandlerCore.cs` | shared CRUD/query contract |
| 26 | `BackendApi/Services/BackgroundWorkers/DispatchTimeoutWorker.cs` | timeout re-dispatch |
| 27 | `BackendApi/Services/BackgroundWorkers/HeartbeatMonitor.cs` | STALE/OFFLINE recovery and batched DB access |
| 28 | `BackendApi/Services/BackgroundWorkers/OsrmSnapWorker.cs` | road snapping |
| 29 | `BackendApi/Services/Telemetry/TelemetryService.cs` | GPS pipeline, Redis/DB fallback and authorized broadcast |
| 30 | `rider_app/lib/core/signalr/signalr_service.dart` | `OfferReceived`, GPS, accept/reject |
| 31 | `rider_app/lib/core/location/gps_buffer_service.dart` | SQLite queue, ordering and retry |
| 32 | `rider_app/lib/core/location/location_service.dart` | GPS permission/filter/background behavior |
| 33 | `rider_app/lib/core/database/local_database_service.dart` | pending GPS/status durable schema |
| 34 | `admin-dashboard/src/app/features/map/map.component.ts` | live map, polyline, Canvas rendering and safe popups |

## Tier 3: Configuration Critical

| # | File | Verification required |
|---:|---|---|
| 35 | `docker-compose.yml` and `docker-compose.override.yml` | topology, loopback exposure, health dependencies |
| 36 | `BackendApi/Program.cs` | bootstrap |
| 37 | `nginx-proxy/nginx.conf` | API, SignalR and frontend proxy routes |
| 38 | `ai-engine/app/models/` | strict Pydantic request contracts |
| 39 | `BackendApi/Data/ApplicationDbContext.cs` | PostGIS, indexes, filters, `xmin`, ProcessedEvents |
| 40 | `BackendApi/Migrations/20260614152246_ConsolidatedBaseline20260614.cs` | consolidated fresh-database baseline |
| 41 | `BackendApi/ServiceMigration/PostgresAdvancedConfigurator.cs` and `MigrationBaselineCompatibility.cs` | idempotent PostgreSQL-specific schema evolution/compatibility bridge |

## Protected Endpoints

| Endpoint | Registration/handler |
|---|---|
| `POST /api/optimize-route` | `ai-engine/main.py` + `endpoints/optimize.py` |
| `POST /api/v1/dispatch/rank` | `app/api/v1/api.py` + `endpoints/dispatch.py` |
| `POST /api/v1/predict-eta` | `app/api/v1/api.py` + `endpoints/predict.py` |
| `GET /health` | `ai-engine/main.py` |
| `POST /api/v1/ai/optimize-route` | `AiController` through `DeliveryControllerBase` |
| `POST /api/v1/ai/dispatch/rank` | `AiController` through `DeliveryControllerBase` |
| `WS /hubs/tracking` | `ApplicationSetup.cs` + `TrackingHub` |

## Mandatory Verification

- AI routers and all protected endpoints still register
- `IAiService` still has rank, optimize and predict methods
- AI/OSRM fallback paths still execute under dependency failure
- all existing Order/Rider transitions remain
- TrackingHub contains no business orchestration
- RabbitMQ consumers retain ProcessedEvents idempotency
- Flutter offline GPS/status queues retain ordering and retry
- build/tests for every touched component pass

เมื่อย้ายหรือเพิ่ม critical file ให้อัปเดต registry นี้ใน change เดียวกัน.
ห้ามลบรายการโดยไม่มี Project Owner approval.
