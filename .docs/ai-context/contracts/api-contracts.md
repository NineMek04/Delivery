# REST API Contracts

**Base route:** `/api/v1`

## 1. Standard Response

```json
{
  "status": 200,
  "success": true,
  "message": "สำเร็จ",
  "errorDetail": null,
  "code": null,
  "errors": null,
  "value": {}
}
```

Failure ต้องมี HTTP status และ body `status` ตรงกัน. 401/403 ต้องมี JSON body
แม้เกิดใน authentication middleware. `value` เป็น payload ของ `ApiResponse<T>`.

## 2. Auth

```text
POST /auth/login
POST /auth/register
POST /auth/refresh
POST /auth/logout
GET  /auth/session
POST /auth/change-password
```

Login request: `{ "email": "...", "password": "..." }`.
Dashboard ใช้ HttpOnly cookies; native app ใช้ response tokens ตาม secure storage flow.
Register public roles จำกัด Customer, Rider, StorePartner.

## 3. Orders

```text
POST  /orders
GET   /orders
GET   /orders/customer
GET   /orders/{idOrTrackingCode}
GET   /orders/my
GET   /orders/shop
PATCH /orders/{id}/status
POST  /orders/{id}/accept-by-store
POST  /orders/{id}/reject-by-store
POST  /orders/{id}/cancel
POST  /orders/{id}/dispatch
POST  /orders/batch-dispatch
```

Create request fields:

```json
{
  "pickupLat": 17.41,
  "pickupLng": 102.78,
  "dropoffLat": 17.42,
  "dropoffLng": 102.79,
  "expectedDeliveryTime": "2026-06-14T13:00:00Z",
  "customerId": "",
  "shopId": "uuid",
  "items": [
    { "menuItemId": "uuid", "quantity": 1, "notes": null, "optionsDescription": null }
  ]
}
```

Server derives/validates identity and shop/menu ownership; client-supplied IDs
ห้ามใช้ข้าม tenant. Order response uses `trackingCode`, `status`,
`assignedRiderId`, route fields and batch fields.

## 4. Shops And Menus

```text
GET/POST/PUT/DELETE /shops
GET/POST/PUT/DELETE /menu-items
GET /menu-items/shop/{shopId}
GET/POST/PUT/DELETE /menu-categories
GET /menu-categories/shop/{shopId}
```

Create shop:

```json
{
  "name": "Shop",
  "menuName": "Featured item",
  "menuPrice": 50,
  "lat": 17.41,
  "lng": 102.78,
  "isOpen": true,
  "prepTimeMinutes": 15,
  "openingHours": "08:00-20:00"
}
```

Shop update DTO fields `isOpen` และ `prepTimeMinutes` ต้อง nullable เพื่อไม่เขียน
ทับค่าที่ client ไม่ได้ส่ง.

`MenuItem.Id` คือ identity ของเมนู ส่วน `MenuCategoryId` เป็น nullable foreign key
ไปหมวดหมู่และต้องเป็นหมวดของ `ShopId` เดียวกัน.

## 5. Rider Location

```text
GET /rider-locations
GET /rider-locations/{riderId}/history?from=...&to=...
POST /telemetry/gps
POST /telemetry/gps/batch
POST /telemetry/client-route-fallback
GET /telemetry/mobile-config
POST /rider-routes/resolve
```

GPS history อ่าน PostgreSQL history; Redis ใช้ current operational location เท่านั้น.

Route fallback request:

```json
{
  "orderId": "uuid",
  "routePhase": "PICKUP",
  "reason": "MISSING_POLYLINE",
  "encodedLength": 0
}
```

`routePhase` is `PICKUP` or `DELIVERY`; `reason` is `MISSING_POLYLINE` or
`INVALID_POLYLINE` or `LOCAL_OSRM_UNAVAILABLE`. The authenticated rider must
own the assigned order.

Rider route resolution request:

```json
{
  "orderId": "uuid",
  "routePhase": "PICKUP",
  "currentLat": 17.41,
  "currentLng": 102.78
}
```

The Backend verifies assigned-rider ownership and calls local OSRM. Response
fields are `encodedPolyline`, `distanceMeters`, `durationSeconds`, and
`source` (`LOCAL_OSRM` or `HAVERSINE_FALLBACK`).

## 6. AI Proxy

```text
POST /ai/optimize-route
POST /ai/dispatch/rank
```

Backend proxy responses ยังคง wrap ด้วย `ApiResponse<T>`.

## 7. Errors

ใช้ 400 validation/business input, 401 unauthenticated, 403 unauthorized,
404 missing resource, 409 concurrency/conflict, 429 rate limit, 500 unexpected,
503 dependency unavailable. Swagger ต้อง document standard error schema ทุก status.
