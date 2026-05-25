---
scope: SignalR Hub Contracts (TrackingHub)
source_of_truth:
  - AI-CHANGELOG.md (2026-05-14 TrackingHub, 2026-05-19 SignalR fixes, 2026-05-20 Dispatch events)
  - BackendApi/Hubs/TrackingHub.cs (codebase)
related_contexts:
  - .docs/ai-context/spec-backend.md
  - .docs/ai-context/contracts/state-machine.md
forbidden_patterns:
  - เพิ่ม SignalR method ใหม่โดยไม่ลงที่นี่ก่อน
  - เรียก SignalR method ที่ไม่ได้ define ไว้ (hallucination)
  - ส่ง GPS payload โดยไม่มี fallback mapper ฝั่ง Angular
known_pitfalls:
  - JWT ต้องส่งผ่าน ?access_token= query string (ไม่ใช่ Authorization header)
  - GPS payload field names: Lat/lat/latitude ต้องใช้ fallback mapper ฝั่ง client
  - Reconnect race: client อาจส่ง GPS ก่อน connection established ต้องมี buffer
---

# signalr-contracts.md — SignalR Hub Contracts

> **Hub:** `TrackingHub.cs` | **URL:** `/hubs/tracking`  
> **Auth:** JWT via `?access_token=` query string

---

## 1. Connection Setup

```typescript
// Angular / JavaScript
const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/tracking', {
    accessTokenFactory: () => accessToken  // JWT
  })
  .withAutomaticReconnect([0, 2000, 10000, 30000])
  .build();

// Flutter (Dart)
final connection = HubConnectionBuilder()
  .withUrl('${baseUrl}/hubs/tracking',
    options: HttpConnectionOptions(
      accessTokenFactory: () async => await authService.getAccessToken()
    ))
  .withAutomaticReconnect()
  .build();
```

---

## 2. Client → Server Methods (Hub Invocations)

### `UpdateLocation` — ส่ง GPS พิกัด

```typescript
// Invocation
await connection.invoke('UpdateLocation', latitude, longitude, accuracy);

// Parameters
latitude: number   // WGS84, e.g. 17.4138
longitude: number  // WGS84, e.g. 102.7872
accuracy: number   // meters, e.g. 12.5
```

**Server behavior:**
1. ตรวจ GPS Sanity (max drift 5km ต่อ update)
2. บันทึกไปยัง `GpsSyncBuffer` (in-memory)
3. อัปเดต `Rider.CurrentLocation` ลง PostgreSQL ทันที
4. Broadcast `RiderLocationUpdated` ไปยัง group `"admins"`

---

### `UpdateStatus` — อัปเดตสถานะ Rider

```typescript
await connection.invoke('UpdateStatus', status);
// status: "IDLE" | "OFFLINE" | "PICKING_UP" | "DELIVERING"
```

---

### `AcceptOffer` — รับงาน

```typescript
await connection.invoke('AcceptOffer', offerId, offerVersion);
// offerId: string (UUID)
// offerVersion: number (optimistic concurrency)
```

**Server behavior:**
1. ตรวจ `offerVersion` (ป้องกัน double-accept)
2. Transition Order: `OFFERING` → `ASSIGNED`
3. Transition Rider: `OFFERED` → `ASSIGNED`
4. Broadcast `OrderStatusChanged` ไปยัง `"admins"` และ `"rider:{riderId}"`

---

### `RejectOffer` — ปฏิเสธงาน

```typescript
await connection.invoke('RejectOffer', offerId, orderId);
// offerId: string (UUID)
// orderId: string (UUID)
```

**Server behavior:**
1. Release Redis offer lock
2. Rider: `OFFERED` → `IDLE`
3. Order: re-dispatch ไปหา Rider คนถัดไป

---

## 3. Server → Client Events (Hub Broadcasts)

### `RiderLocationUpdated` — GPS update broadcast

```typescript
// Angular subscription
connection.on('RiderLocationUpdated', (data: RiderLocationPayload) => {
  const lat = data.latitude ?? data.lat ?? data.Lat;  // fallback mapper!
  const lng = data.longitude ?? data.lng ?? data.Lng;
});
```

```json
{
  "riderId": "uuid",
  "lat": 17.4138,
  "lng": 102.7872,
  "accuracy": 12.5,
  "timestamp": "2026-05-21T04:00:00Z",
  "state": "DELIVERING"
}
```

**Recipients:** group `"admins"`

---

### `OnOfferReceived` — ข้อเสนองานใหม่

```typescript
connection.on('OnOfferReceived', (offer: OfferPayload) => {
  // แสดง OfferBottomSheet + countdown 30s
});
```

```json
{
  "offerId": "uuid",
  "orderId": "uuid",
  "offerVersion": 1,
  "shopName": "ร้านข้าวมันไก่อุดร",
  "shopLocation": { "lat": 17.4150, "lng": 102.7880 },
  "dropoffLocation": { "lat": 17.4100, "lng": 102.7850 },
  "distanceKm": 2.3,
  "deliveryFee": 45.0,
  "expiresAt": "2026-05-21T04:00:30Z",
  "pickupRoute": {
    "encodedPolyline": "_p~iF~ps|U...",
    "distanceKm": 1.2,
    "durationSeconds": 180
  }
}
```

**Recipients:** group `"rider:{riderId}"`

---

### `DispatchScanStarted` — เริ่มสแกนหา Rider

```json
{
  "orderId": "uuid",
  "orderRefNumber": "ORD-000001",
  "shopLocation": { "lat": 17.4150, "lng": 102.7880 },
  "radiusKm": 5.0,
  "timestamp": "2026-05-21T04:00:00Z"
}
```

**Recipients:** group `"admins"`

---

### `DispatchCandidatesRanked` — AI ranking ผล

```json
{
  "orderId": "uuid",
  "candidates": [
    {
      "riderId": "uuid",
      "riderRefNumber": "RID-000003",
      "rank": 1,
      "score": 2.3,
      "distanceKm": 1.2,
      "location": { "lat": 17.4200, "lng": 102.7900 }
    }
  ]
}
```

**Recipients:** group `"admins"`

---

### `DispatchOfferSent` — ส่ง offer แล้ว

```json
{
  "orderId": "uuid",
  "riderId": "uuid",
  "riderRefNumber": "RID-000003",
  "expiresAt": "2026-05-21T04:00:30Z"
}
```

**Recipients:** group `"admins"`

---

### `OrderStatusChanged` — สถานะ order เปลี่ยน

```json
{
  "orderId": "uuid",
  "orderRefNumber": "ORD-000001",
  "previousStatus": "ASSIGNED",
  "newStatus": "PICKING_UP",
  "riderId": "uuid",
  "timestamp": "2026-05-21T04:00:00Z"
}
```

**Recipients:** group `"admins"` + group `"rider:{riderId}"`  
สำหรับ Customer broadcast: group `"customer:{customerId}"` (เมื่อ Backend Tier 1 พร้อม)

---

## 4. Group Membership

| Role | Group(s) |
|---|---|
| Admin / Dispatcher | `"admins"` |
| Rider | `"rider:{riderId}"` |
| Customer | `"customer:{userId}"` |
| StorePartner | `"stores"` |
