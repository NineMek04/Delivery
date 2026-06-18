# 05 Admin Dashboard

## Stack

Angular 19 standalone components, RxJS, Leaflet with `preferCanvas: true`, SignalR, SweetAlert2, and HttpOnly-cookie dashboard auth.

## Routes

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

## Auth And API

- Dashboard auth uses HttpOnly access/refresh cookies.
- HTTP calls must send credentials and XSRF token.
- 401 refresh is single-flight. Refresh failure must clear state and navigate `/login` using replace URL.
- Components should consume unwrapped `ApiResponse.value` from service helpers.

## Realtime UI

- Use `/hubs/tracking`.
- Do not register duplicate SignalR handlers on reconnect.
- Long-lived RxJS subscriptions must teardown with `takeUntilDestroyed` or an aggregate subscription.
- UI must react to state/location updates without a manual page refresh.

## Map Rules

- Use `preferCanvas: true`.
- Escape popup content and bind events programmatically.
- Do not use inline `onclick` in popup HTML.
- Avoid repeated full-map scans. Prefer updating markers/layers only when backend state/location events change.
- Do not use admin map as rider navigation. Rider route drawing and navigation belong in Flutter app.
- Admin map may display active assigned/delivering order route for operations visibility only.

## Operational Data

Admin should show:

- Active riders and rider states
- Orders by state
- Dispatch scan attempts and candidate ranking events
- Rider GPS accuracy circle for degraded admin-only telemetry
- Historical GPS through backend history endpoint, not Redis

