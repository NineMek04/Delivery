# 02 Backend API

## Stack

Backend runs on ASP.NET Core 8 with EF Core, Npgsql, NetTopologySuite/PostGIS, SignalR, Redis, RabbitMQ, Serilog/Seq, FluentValidation, and Mapster.

## Actual Code Layout

```text
BackendApi/
  Controllers/              HTTP transport controllers by domain
  Core/                     base controllers, response wrapper, filters, state machines
  Data/                     ApplicationDbContext and seed data
  Features/
    AiRouting/              IAiService, AiService, OsrmRoutingService, rider route endpoint
    DispatchManagement/     dispatch orchestration and state transitions
    FleetTracking/          telemetry endpoints and tracking models
  Hubs/
    Tracking/               TrackingHub partials
    Chat/                   ChatHub
  Infrastructure/
    EventBus/               RabbitMQ integration event infrastructure
    Redis/                  locks, cache, presence
  ServiceMigration/         idempotent PostgreSQL advanced schema setup
  Services/                 application services and background workers
  Setup/
    Extensions/             DI, app pipeline, database migration setup
    Middlewares/            error, auth, csrf, correlation, security headers
```

## Response Contract

REST responses use `ApiResponse`:

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

Errors must return a matching HTTP status and JSON body, including auth failures such as 401/403.

## Important REST Routes

```text
POST /api/v1/auth/login
POST /api/v1/auth/register
POST /api/v1/auth/refresh
POST /api/v1/auth/logout
GET  /api/v1/auth/session

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

GET/POST/PUT/DELETE /api/v1/shops
GET/POST/PUT/DELETE /api/v1/menu-items
GET /api/v1/menu-items/shop/{shopId}
GET/POST/DELETE /api/v1/menu-categories
GET /api/v1/menu-categories/shop/{shopId}

POST /api/v1/telemetry/gps
POST /api/v1/telemetry/gps/batch
GET  /api/v1/telemetry/config/mobile
POST /api/v1/telemetry/client-route-fallback
POST /api/v1/telemetry/client-events
POST /api/v1/rider-routes/resolve
```

## Order Flow

1. Customer creates an order.
2. StorePartner accepts the order and backend queues dispatch.
3. Dispatch service ranks riders through AI/fallback logic.
4. Backend sends `OfferReceived` to `rider:{riderId}`.
5. Rider accepts with `AcceptOffer(offerId, version)`.
6. Order moves through `ASSIGNED`, `PICKING_UP`, `DELIVERING`, `COMPLETED`.

## Database And Migration

- EF baseline exists in `BackendApi/Migrations`.
- Advanced PostgreSQL setup belongs in `BackendApi/ServiceMigration`.
- `ProcessedEvents.ProcessedAt` must be indexed.
- Production migration should run as a single controlled runner, not by multiple app instances racing at startup.

## Soft Delete Rule

`DELETE /api/v1/orders/customer/clear` and other business deletes must remain
soft-delete operations. `DBHandlerCore.DeleteObjectAsync` now handles current
`ISoftDeletableEntity`/`IsDeleted` entities directly before falling back to
legacy `DelFlag`/`DEL_FLAG` compatibility fields.
