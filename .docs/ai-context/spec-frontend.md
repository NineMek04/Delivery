---
scope: Frontend Admin Dashboard (Angular 19)
source_of_truth:
  - AI-BLUEPRINT.md (Section: Latest Working Context - Sim Map Behavior)
  - PROJECT-SPEC.md (Section 8, Frontend Architecture)
  - AI-CHANGELOG.md (2026-05-15 SignalR, 2026-05-18 BaseApiService, 2026-05-19 Map Fixes, 2026-05-20 Sim Map)
  - admin-dashboard/src/ (codebase)
related_contexts:
  - .docs/ai-context/contracts/signalr-contracts.md
  - .docs/ai-context/spec-backend.md
forbidden_patterns:
  - ใช้ NgModules (ต้องเป็น Standalone Components เท่านั้น)
  - manual unwrap ApiResponse ใน Component (ให้ BaseApiService จัดการ)
  - subscribe() แบบ nested ใน lifecycle hooks (memory leak)
  - ใช้ model types ที่ไม่ได้ generate จาก OpenAPI
  - hardcode API URLs (ต้องผ่าน environment หรือ service)
known_pitfalls:
  - SignalR GPS payload case mismatch (Lat vs lat vs latitude) → ต้องใช้ fallback mapper
  - RxJS subscribe() ไม่ unsubscribe → memory leak ใน map component
  - APP_INITIALIZER blocking → ต้องมี timeout 5s และ always-resolve
  - Leaflet + SignalR ต้องระวัง fitBounds กระตุก UX ระหว่าง user interaction
  - Bundle budget warnings: leaflet + sweetalert2 เป็น CommonJS (non-fatal)
---

# spec-frontend.md — Frontend Admin Dashboard (Angular 19)

> **Source**: `AI-BLUEPRINT.md` + `PROJECT-SPEC.md` Sec 8 + `AI-CHANGELOG.md`  
> **For SignalR event names & payloads** → `contracts/signalr-contracts.md`

---

## 1. Tech Stack

| Layer | Technology | Notes |
|---|---|---|
| Framework | Angular | v19.2.0 |
| Component Style | Standalone Components | ห้ามใช้ NgModules |
| Maps | Leaflet | ผ่าน `@types/leaflet` |
| Real-time | `@microsoft/signalr` | WebSocket connection |
| HTTP | `DeliveryHttpRequest` (Fluent API) | Custom wrapper |
| Models | OpenAPI Generated | `src/app/api/generated/` |
| State/Auth | `AuthService` + `localStorage` | JWT + Refresh Token |
| UI Alerts | SweetAlert2 | Auth guards, error messages |

---

## 2. Architecture Patterns

### HTTP Layer (ต้องใช้เสมอ)

```typescript
// Standard CRUD Service → extends BaseApiService<T>
@Injectable({ providedIn: 'root' })
export class OrderService extends BaseApiService<OrderDto> {
  constructor(http: DeliveryHttpRequest) {
    super(http, '/api/v1/orders');
  }
}

// BaseApiService ทำ auto-unwrap ApiResponse<T> และ PaginatedResult<T>
const orders = await this.orderService.getAll();        // returns T[]
const page = await this.orderService.getPaginated(1, 10); // returns PaginatedResult<T>

// Custom endpoint → DeliveryHttpRequest (Fluent API)
const result = await req<OrderDto>('/api/v1/orders/dispatch')
  .body({ orderId })
  .post();
```

### Route Structure

```
/login           → LoginComponent (guestGuard)
/register        → RegisterComponent (guestGuard)
/dashboard       → DashboardComponent (authGuard + roleGuard)
/map             → SimMapComponent ← ใช้ระหว่าง simulator testing
/map-live        → MapComponent ← production/mobile flow (เก็บไว้)
/orders          → OrdersComponent (authGuard)
/analytics       → AnalyticsComponent (authGuard)
/customer        → CustomerComponent (simulation prototype)
/store-partner   → StorePartnerComponent (simulation prototype)
```

### Guards

| Guard | หน้าที่ |
|---|---|
| `authGuard` | ตรวจ JWT + proactive refresh ก่อน expire |
| `roleGuard` | เฉพาะ Admin/Dispatcher เข้า dashboard ได้ |
| `guestGuard` | ผู้ login แล้วไม่ให้เข้าหน้า login/register |

---

## 3. Sim Map — Core Behaviors

**File:** `admin-dashboard/src/app/features/sim-map/`

### Animation

```typescript
// smooth rider marker movement ด้วย requestAnimationFrame
function animateMarker(marker, from, to, duration) {
  const start = performance.now();
  function step(now) {
    const t = Math.min((now - start) / duration, 1);
    const lat = from.lat + (to.lat - from.lat) * t;
    const lng = from.lng + (to.lng - from.lng) * t;
    marker.setLatLng([lat, lng]);
    if (t < 1) requestAnimationFrame(step);
  }
  requestAnimationFrame(step);
}
```

### Auto-Follow Camera

- ติดตาม selected rider ระหว่าง pickup/delivery
- ใช้ `map.fitBounds([riderPos, targetPos])` เมื่อ simulation running
- ปุ่ม `🎬 [Sim Auto-Follow]` toggle เพื่อปกป้อง UX จากการกระตุก

### Sim Map UI Elements

- **HUD phases:** `Scan → Offer → Assign → Pickup → Dropoff`
- **Scan circle:** แสดงรัศมีค้นหา rider รอบร้านค้า
- **Candidate ranking board:** แสดง AI ranked riders
- **Realtime event timeline:** ลำดับ events ที่เกิดขึ้น
- **Route progress:** Pickup route และ Dropoff route

---

## 4. SignalR Real-time (Angular)

**File:** `admin-dashboard/src/app/core/signalr/tracking-signalr.service.ts`

```typescript
// Connection
const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/tracking', { accessTokenFactory: () => token })
  .withAutomaticReconnect()
  .build();

// GPS Payload — ต้องใช้ fallback mapper (pitfall!)
function extractCoords(payload: any): { lat: number, lng: number } {
  return {
    lat: payload.latitude ?? payload.lat ?? payload.Lat,
    lng: payload.longitude ?? payload.lng ?? payload.Lng
  };
}
```

**Events ที่ Subscribe:**
- `RiderLocationUpdated` → อัปเดต marker บน map
- `DispatchScanStarted` → แสดง scan circle
- `DispatchCandidatesRanked` → แสดง candidate board
- `DispatchOfferSent` → แสดง offer notification
- `OrderStatusChanged` → อัปเดต order status

> ดู payload schemas ทั้งหมด → `contracts/signalr-contracts.md`

---

## 5. Polyline Decoding (Client-side)

```typescript
// Pure TypeScript — ไม่ต้องใช้ external library
function decodePolyline(encoded: string): [number, number][] {
  const points: [number, number][] = [];
  let index = 0, lat = 0, lng = 0;
  while (index < encoded.length) {
    let shift = 0, result = 0, b: number;
    do {
      b = encoded.charCodeAt(index++) - 63;
      result |= (b & 0x1f) << shift;
      shift += 5;
    } while (b >= 0x20);
    lat += (result & 1) ? ~(result >> 1) : result >> 1;
    shift = result = 0;
    do {
      b = encoded.charCodeAt(index++) - 63;
      result |= (b & 0x1f) << shift;
      shift += 5;
    } while (b >= 0x20);
    lng += (result & 1) ? ~(result >> 1) : result >> 1;
    points.push([lat / 1e5, lng / 1e5]);
  }
  return points;
}
```

---

## 6. RxJS Memory Leak Prevention

**ห้ามใช้:**
```typescript
// ❌ Memory leak — nested subscribes ใน lifecycle hooks
ngOnInit() {
  this.signalRService.offerReceived$.subscribe(offer => {
    this.riderService.getRiders().subscribe(riders => { ... }); // leak!
  });
}
```

**ให้ใช้:**
```typescript
// ✅ ถูกต้อง — synchronous service call หรือใช้ switchMap
ngOnInit() {
  this.signalRService.offerReceived$.subscribe(offer => {
    const riders = this.riderService.getRiderLocations(); // synchronous
  });
}

// หรือใช้ takeUntilDestroyed (Angular 16+)
ngOnInit() {
  this.signalRService.offerReceived$
    .pipe(takeUntilDestroyed(this.destroyRef))
    .subscribe(offer => { ... });
}
```

---

## 7. OpenAPI Code Generation

```bash
# Generate models จาก Backend Swagger
npm run generate:api

# Output location
admin-dashboard/src/app/api/generated/
```

**ห้ามเขียน DTO types manual** — ต้องใช้ generated models เสมอ

---

## 8. Map Markers & Layering

| Element | Color/Icon | หน้าที่ |
|---|---|---|
| Rider (IDLE) | 🟢 Green | Idle rider marker |
| Rider (DELIVERING) | 🔵 Blue | Active rider marker |
| Shop | 🟠 Orange 🏪 | Shop location |
| Temp Shop (adding) | 🟡 Yellow 📍 bounce | Pending shop placement |
| Pickup Point | 🔴 Red | Order pickup |
| Dropoff Point | 🟣 Purple | Order dropoff |
| Scan Circle | Semi-transparent cyan | Dispatch search radius |
