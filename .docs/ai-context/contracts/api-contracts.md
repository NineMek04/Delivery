---
scope: REST API Contracts (Endpoints, DTOs, Auth)
source_of_truth:
  - PROJECT-SPEC.md (Sections 5-6, Backend API, Security)
  - AI-CHANGELOG.md (2026-05-15 OrdersController, 2026-05-18 DTO extensions, 2026-05-19 RefNumber)
  - BackendApi/Controllers/ + BackendApi/Models/DTOs/ (codebase)
related_contexts:
  - .docs/ai-context/spec-backend.md
  - .docs/ai-context/contracts/state-machine.md
forbidden_patterns:
  - ไม่ wrap response ด้วย ApiResponse<T>
  - ส่ง state ที่ไม่อยู่ใน state-machine.md
  - ใช้ UUID ตรงๆ ใน URL โดยไม่รองรับ RefNumber
known_pitfalls:
  - GlobalResponseFilter auto-wraps ทุก response → BaseApiService ต้องมี unwrap logic
  - RefNumber search: GET /api/v1/orders/ORD-000001 และ GET /api/v1/orders/{uuid} ทั้งคู่ต้องทำงาน
---

# api-contracts.md — REST API Contracts

> **Source**: `PROJECT-SPEC.md` Sec 5-6 + `AI-CHANGELOG.md`  
> **For state values** → `contracts/state-machine.md`  
> **For backend implementation** → `spec-backend.md`

---

## 1. Standard Response Wrapper

**ทุก endpoint** คืนค่าในรูปแบบ `ApiResponse<T>`:

```json
{
  "success": true,
  "data": { /* T */ },
  "message": null,
  "errors": null
}
```

```json
{
  "success": false,
  "data": null,
  "message": "Validation failed",
  "errors": ["field: error message"]
}
```

**Paginated Response:**
```json
{
  "success": true,
  "data": {
    "items": [ /* T[] */ ],
    "page": 1,
    "pageSize": 10,
    "totalCount": 100,
    "totalPages": 10
  }
}
```

---

## 2. Auth Endpoints

### `POST /api/v1/auth/login`
```json
// Request
{ "email": "admin@delivery.com", "password": "Password123!" }

// Response
{
  "accessToken": "eyJ...",
  "refreshToken": "sha256hash...",
  "expiresAt": "2026-05-21T05:00:00Z",
  "user": {
    "id": "uuid",
    "email": "admin@delivery.com",
    "role": "Admin",
    "name": "Admin User"
  }
}
```

### `POST /api/v1/auth/refresh`
```json
// Request
{ "refreshToken": "sha256hash..." }

// Response (same as login)
```

### `POST /api/v1/auth/logout`
No body required. Clears HttpOnly Cookie and invalidates refresh token.

### `GET /api/v1/auth/session`
Returns current user info if token valid. Used by `APP_INITIALIZER`.

---

## 3. Orders Endpoints

### `GET /api/v1/orders?page=1&pageSize=10`
Returns paginated `OrderDto[]`. Auth: `[Authorize]`.

### `GET /api/v1/orders/{idOrRefNumber}`
Supports both UUID (`uuid`) and RefNumber (`ORD-000001`).

### `POST /api/v1/orders` — Create Order
```json
// Request (CreateOrderDto)
{
  "shopId": "uuid",
  "pickupLocation": { "lat": 17.4150, "lng": 102.7880 },
  "dropoffLocation": { "lat": 17.4100, "lng": 102.7850 },
  "customerNote": "Optional note"
}

// Response (OrderDto)
{
  "id": "uuid",
  "refNumber": "ORD-000001",
  "state": "CREATED",
  "shopId": "uuid",
  "shopName": "ร้านข้าวมันไก่อุดร",
  "shopRefNumber": "SHP-000001",
  "pickupLocation": { "lat": 17.4150, "lng": 102.7880 },
  "dropoffLocation": { "lat": 17.4100, "lng": 102.7850 },
  "distanceKm": 2.3,
  "deliveryFee": 45.0,
  "encodedPolyline": "_p~iF~ps|U...",
  "riderId": null,
  "riderRefNumber": null,
  "createdAt": "2026-05-21T04:00:00Z",
  "assignedAt": null,
  "completedAt": null
}
```

### `PATCH /api/v1/orders/{id}/status`
```json
// Request
{ "status": "PICKING_UP" }
// Auth: Rider role only (assigned rider)
```

### `POST /api/v1/orders/{id}/dispatch` — Force Re-dispatch
Auth: Admin/Operations. Triggers dispatch orchestration.

### `POST /api/v1/orders/{id}/cancel`
Auth: Admin/Operations.

### `POST /api/v1/orders/{id}/accept-by-store`
Auth: StorePartner. Triggers `OrderAcceptedByStore` broadcast to customer.

---

## 4. Riders Endpoints

### `GET /api/v1/riders?page=1&pageSize=10`
Returns paginated `RiderDto[]`. Auth: Admin/Operations.

### `GET /api/v1/riders/{idOrRefNumber}`
Supports UUID or `RID-000001`.

```json
// RiderDto
{
  "id": "uuid",
  "refNumber": "RID-000001",
  "name": "สมชาย ใจดี",
  "email": "rider1@delivery.com",
  "phone": "0812345678",
  "state": "IDLE",
  "currentLocation": { "lat": 17.4138, "lng": 102.7872 },
  "lastUpdated": "2026-05-21T04:00:00Z"
}
```

---

## 5. Shops Endpoints

### `GET /api/v1/shops`
### `POST /api/v1/shops` — Create Shop
```json
{
  "name": "ร้านข้าวมันไก่อุดร",
  "location": { "lat": 17.4150, "lng": 102.7880 },
  "popularMenu": "ข้าวมันไก่",
  "priceRange": "฿40-80"
}
```

### `GET /api/v1/shops/{idOrRefNumber}` — Supports `SHP-000001`

---

## 6. Auth & Role Policies

| Policy | Roles | ใช้กับ |
|---|---|---|
| `[Authorize]` | All authenticated users | Basic endpoints |
| `[Authorize(Policy = "AdminOnly")]` | Admin | User management |
| `[Authorize(Policy = "Operations")]` | Admin, Dispatcher | Order management |
| `[Authorize(Policy = "Rider")]` | Rider | GPS, order status update |

---

## 7. Concurrency Control

| Header/Mechanism | Detail |
|---|---|
| `RowVersion` | `bytea` field — EF Core Concurrency Token |
| Offer Version | monotonic int ส่งกลับใน OfferPayload |
| HTTP 409 Conflict | ตอบเมื่อ RowVersion mismatch |

---

## 8. Error Codes

| HTTP Status | Meaning |
|---|---|
| 200 | Success |
| 201 | Created |
| 400 | Validation error / bad request |
| 401 | Unauthorized (token invalid/expired) |
| 403 | Forbidden (role insufficient) |
| 404 | Resource not found |
| 409 | Concurrency conflict (RowVersion mismatch) |
| 500 | Internal server error |
