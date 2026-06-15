# Flutter Multi-Role App

**Version:** 1.0.0 | **Updated:** 2026-06-14

## 1. Active Stack

`dio`, `flutter_riverpod`, `go_router`, `signalr_netcore`, `geolocator`,
`flutter_map`, `flutter_secure_storage`, `jwt_decoder`, `sqflite`

แอปมี flow Rider, Customer และ StorePartner ที่ใช้งานจริง ไม่ใช่ placeholder.

## 2. Boundaries

- API clients อยู่ใต้ `core/api/`
- SignalR clients แยก rider/customer/store
- domain/server state อยู่ใน Riverpod notifier/provider
- `setState` ใช้เฉพาะ transient UI state
- navigation ใช้ GoRouter

## 3. Auth

- Native token ใช้ secure storage; web fallback ใช้ sessionStorage
- refresh 401 ต้อง single-flight และไม่ retry `/auth/*`
- refresh สำเร็จต้อง update credential ก่อน replay request
- refresh ล้มเหลวต้องหยุด SignalR/GPS session, clear storage และไป login
- ห้ามตีความ network error เป็น token expiry โดยอัตโนมัติ

## 4. GPS And Offline Queue

- GPS accuracy `<= 50m` is Core telemetry. Accuracy `> 50m` and `<= 300m`
  may be sent only as degraded Admin UI telemetry and must not enter
  dispatch/history. Accuracy `> 300m` is rejected.
- Flutter web Mock GPS is a compile-time development mode. The base image
  defaults `ENABLE_MOCK_GPS=false`; `docker-compose.override.yml` enables it
  for local dispatch testing only. Production builds must keep it disabled.

- `LocationService` ส่งจุดเข้า `GpsBufferService`
- SQLite เก็บ pending GPS และ `pending_status_updates`
- sync ตามลำดับเมื่อ network กลับมา; ลบ local mutation หลัง server ยืนยันเท่านั้น
- batch endpoint: `POST /api/v1/telemetry/gps/batch`
- SignalR method: `UpdateLocation(lat,lng,accuracy)`
- background platform permission/service ต้องตั้งตาม Android/iOS

## 5. Dispatch

- canonical offer event คือ `OfferReceived`
- accept: `AcceptOffer(offerId, version)`
- reject: `RejectOffer(offerId, orderId)`
- After accept, the rider map retains the offer pickup route and displays
  Rider-to-pickup during `ASSIGNED`/`PICKING_UP`, then the persisted
  pickup-to-dropoff route during `DELIVERING`. Missing or invalid polylines
  fall back to a straight line without breaking map rendering.
- Flutter web map tiles use the Rider Nginx same-origin `/map-tiles/` proxy;
  native clients may continue using a direct tile provider with local cache.
- Order phase เปลี่ยนผ่าน `PATCH /api/v1/orders/{id}/status`
- Rider state ใช้เฉพาะ `OFFLINE`, `IDLE`, `RESERVED`, `BUSY`, `STALE`
- offline status mutation ต้อง queue และ preserve order

## 6. Store And Customer

- Store menu/category calls ต้องส่ง `ShopId` จาก authenticated shop context
- `MenuCategoryId` เป็น optional relation ของ menu item ไม่ใช่ alias ของ item `Id`
- create/update สำเร็จต้อง refresh provider จาก API response/DB ไม่อาศัย optimistic
  local list เพียงอย่างเดียว
- Customer order/tracking ต้อง filter ตาม authenticated customer และ assigned rider

## 7. Required APIs

```text
POST /api/v1/auth/login
POST /api/v1/auth/refresh
POST /api/v1/auth/logout
GET  /api/v1/orders/my
GET  /api/v1/orders/customer
GET  /api/v1/orders/shop
PATCH /api/v1/orders/{id}/status
GET/POST/PUT/DELETE /api/v1/menu-items
GET/POST/PUT/DELETE /api/v1/menu-categories
WS   /hubs/tracking
```

ใช้ contract จริงใน `api-contracts.md` และ `signalr-contracts.md`; ห้ามสร้างชื่อ
endpoint/event จากการคาดเดา
