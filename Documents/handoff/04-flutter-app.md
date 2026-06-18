# 04 Flutter App

## Roles

The Flutter app is multi-role:

- Rider delivery workflow
- Customer shopping and tracking
- StorePartner menu/order management

## Main Routes

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

## API Rules

- Use `core/api/` services for backend calls.
- Do not call OSRM directly from the app. Route resolution goes through `POST /api/v1/rider-routes/resolve`.
- Auth uses secure storage on native and web fallback storage in browser.
- 401 refresh must be single-flight. If refresh fails, stop SignalR/GPS, clear session, and navigate to login.

## Rider GPS

- Real GPS and mock GPS must not run together.
- Mock GPS is dev-only via `ENABLE_MOCK_GPS=true`.
- `<= 50m` accuracy is core telemetry.
- `> 50m` and `<= 300m` can be sent only as degraded admin UI telemetry.
- `> 300m` must be rejected.
- Offline GPS/status mutations queue locally and sync FIFO.

## Rider Map

After accepting an order:

1. `ASSIGNED`/`PICKING_UP`: show rider-to-pickup route.
2. `DELIVERING`: show pickup-to-dropoff or route re-resolved from backend.
3. If backend/local OSRM cannot return a polyline, draw a straight line only as final fallback.
4. Report fallback once per order/phase/reason through `POST /api/v1/telemetry/client-route-fallback`.
5. Navigation mode follows rider around zoom 17.5; do not keep auto-fitting the full route while riding.

## StorePartner Flow

- Menu items use `MenuItem.Id` as identity.
- `MenuCategoryId` is an optional relation to category, not a duplicate identity.
- Create/update/delete menu calls must refresh from API response or DB-backed API state, not only optimistic local lists.

## Customer Flow

- Customer orders are filtered by authenticated customer.
- Tracking uses authorized rider/order data.
- Clear order history must remain soft-delete by contract.

