# SignalR Contracts

**Hub:** `/hubs/tracking` | **Implementation:** `BackendApi/Hubs/TrackingHub*.cs`

## 1. Transport Rule

Hub ทำเฉพาะ authenticate, validate และ route ไป service. ห้าม query/mutate
business state โดยตรง. WebSocket client ส่ง JWT ผ่าน `access_token`; dashboard
cookie client ใช้ credentials ตาม auth configuration.

## 2. Client To Server

| Method | Arguments | Rule |
|---|---|---|
| `UpdateLocation` | `lat: double, lng: double, accuracy: double` | Rider only, WGS84 |
| `UpdateRiderLocation` | `lat: double, lng: double` | compatibility alias |
| `UpdateHeartbeat` | none | renew presence |
| `UpdateStatus` | Rider state string | only valid RiderState |
| `AcceptOffer` | `offerId: string, version: int` | optimistic offer version |
| `RejectOffer` | `offerId: string, orderId: string` | release and re-dispatch |

Order phases `PICKING_UP`, `DELIVERING`, `COMPLETED` ต้องเปลี่ยนผ่าน Order REST API
ไม่ส่งเข้า `UpdateStatus`.

## 3. Server To Client

### `OfferReceived`

Canonical event name คือ `OfferReceived` ไม่ใช่ `OnOfferReceived`.

```json
{
  "offerId": "uuid",
  "orderId": "uuid",
  "offerVersion": 1,
  "shopName": "Shop",
  "shopLocation": { "lat": 17.41, "lng": 102.78 },
  "dropoffLocation": { "lat": 17.42, "lng": 102.79 },
  "distanceKm": 2.3,
  "deliveryFee": 45.0,
  "expiresAt": "2026-06-14T12:00:30Z"
}
```

Recipient: `rider:{riderId}`.

### `RiderLocationUpdated`

```json
{
  "riderId": "uuid",
  "lat": 17.4138,
  "lng": 102.7872,
  "accuracy": 12.5,
  "timestamp": "2026-06-14T12:00:00Z",
  "state": "BUSY"
}
```

`state` เป็น RiderState เท่านั้น. Recipient คือ admins และ authorized customers
ของ active orders ตาม recipient cache ที่มี PostgreSQL fallback.

### `ShopStatusChanged`

```json
{
  "shopId": "uuid",
  "isOpen": true
}
```

Recipient: `admins` group. Broadcasted when a shop changes its open/closed state.

### Dispatch And Order Events

- `DispatchScanStarted`
- `DispatchCandidatesRanked`
- `DispatchOfferSent`
- `OrderStatusChanged`
- `TelemetryUpdated`

Payload ต้อง camelCase และ event producer/consumer ต้องใช้ชื่อเดียวกัน.
เมื่อเพิ่มหรือ rename event ต้องแก้ contract นี้และ client ทุกตัวใน change เดียวกัน.

## 4. Groups

| Principal | Group |
|---|---|
| Admin/Dispatcher | `admins` |
| Rider | `rider:{riderId}` |
| Customer | `customer:{userId}` |
| StorePartner | `store:{shopId}` |

Legacy `stores` ใช้ได้เฉพาะ compatibility path และห้ามเป็น target หลักของข้อมูลร้าน
ที่ต้องแยก tenant.

## 5. Reconnect

- ห้าม invoke ก่อน connection state เป็น connected
- reconnect handler ต้องไม่ register event ซ้ำ
- pending GPS/status ใช้ local queue และ replay หลัง reconnect
- authorization ต้อง re-evaluate group membership ทุก connection
