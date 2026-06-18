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
  are re-resolved through the authenticated Backend rider-route endpoint,
  which uses local OSRM and its Redis route cache. A straight line is the
  final fallback only while OSRM is unavailable; the client reports that
  fallback once per order/phase/reason without interrupting navigation.
- During an active order, the map follows the rider at navigation zoom 17.5;
  route fitting is reserved for non-navigation/initial overview states.
- การเคลื่อนที่จำลองของไรเดอร์ใช้จุดพิกัดจริงของ OSRM ที่สตรีมเข้ามา แทนการวาดเส้นพิกัดกระจัด (Straight Line) และทำการวัดระยะทางถนนจริงที่เหลืออยู่โดยหาผลรวมความยาวของเซกเมนต์ OSRM เส้นทางทั้งหมด
- การวาดเส้นทางบนแผนที่จะคำนวณและดึงพิกัดที่ผ่านไปแล้วออก (`_getTailRoute`) เพื่อแสดงเฉพาะเส้นทางส่วนที่ยังวิ่งไม่ถึงไปยังปลายทางแบบเรียลไทม์
- Flutter web map tiles use the Rider Nginx same-origin `/map-tiles/` proxy;
  Nginx stores successful tiles in a persistent 30-day disk cache. Native
  clients may continue using a direct tile provider with local cache.
- Order phase เปลี่ยนผ่าน `PATCH /api/v1/orders/{id}/status`
- Rider state ใช้เฉพาะ `OFFLINE`, `IDLE`, `RESERVED`, `BUSY`, `STALE`
- offline status mutation ต้อง queue และ preserve order

## 6. Store And Customer

- Store menu/category calls ต้องส่ง `ShopId` จาก authenticated shop context
- `MenuCategoryId` เป็น optional relation ของ menu item ไม่ใช่ alias ของ item `Id`
- create/update สำเร็จต้อง refresh provider จาก API response/DB ไม่อาศัย optimistic
  local list เพียงอย่างเดียว
- Customer order/tracking ต้อง filter ตาม authenticated customer และ assigned rider
- Customer order history สามารถล้างลบประวัติแบบ Soft Delete ได้ผ่าน API `DELETE /api/v1/orders/customer/clear`; current entities use `IsDeleted`, while legacy entities may use `DelFlag`.
- ปุ่ม "ซื้อทันที" (Buy Now) บนรายละเอียดเมนูร้านค้า ข้ามขั้นตอนการหยิบลงตะกร้าแบบเดิมโดยการล้างรายการในตะกร้าทั้งหมด แล้วเพิ่มสินค้านี้รายการเดียวพร้อมตัวเลือกเสริม จากนั้นแสดงบานหน้าต่างยืนยันสั่งซื้อและชำระเงิน (`CartBottomSheet`) ทันที
- **Store Dashboard / Management Screen Flow:** 
  - จัดการรายการเมนูร้านค้า (ดู/ลบ/แก้ไข ในรูปแบบ Grid เมนูการ์ด รวมถึงการเลือกปักหมุดตำแหน่งร้านค้าด้วยแผนที่)
  - จัดการคำสั่งซื้อที่เข้ามาเรียลไทม์ (แจ้งเตือนออเดอร์ใหม่ผ่าน SignalR ดักฟังก์ชันกดยอมรับเพื่อเข้าคิวจ่ายงานจัดหาไรเดอร์ AI)
  - แดชบอร์ดสรุปรายได้ ยอดขายแต่ละเมนู และจำนวนออเดอร์ย้อนหลัง
  - โปรไฟล์ร้านค้าเพื่อเปิด/ปิดร้าน (`IsOpen`), กำหนดเวลาเปิด-ปิด และข้อมูลทั่วไปของร้านค้า
- **Customer Shopping Flow:**
  - เลือกดูรายการร้านค้าที่เปิดอยู่
  - หน้ารายละเอียดร้านค้าและเมนูแยกตามหมวดหมู่ สามารถระบุตัวเลือกเสริมของอาหาร และกดสั่งซื้อตามขั้นตอนทั่วไป หรือปุ่ม "ซื้อทันที"
  - หน้าต่างติดตามพิกัดของออเดอร์แบบเรียลไทม์ (`CustomerTrackingScreen`) สำหรับเฝ้าดูตำแหน่งพิกัดปัจจุบันของคนขับที่จะส่งผ่านสตรีม SignalR Telemetry
  - การจัดการที่อยู่และประวัติออเดอร์ (ล้างประวัติคำสั่งซื้อแบบ Soft Delete)


## 7. Required APIs

```text
POST /api/v1/auth/login
POST /api/v1/auth/refresh
POST /api/v1/auth/logout
GET  /api/v1/orders/my
GET  /api/v1/orders/customer
DELETE /api/v1/orders/customer/clear
GET  /api/v1/orders/shop
PATCH /api/v1/orders/{id}/status
GET/POST/PUT/DELETE /api/v1/menu-items
GET/POST/PUT/DELETE /api/v1/menu-categories
WS   /hubs/tracking
```

ใช้ contract จริงใน `api-contracts.md` และ `signalr-contracts.md`; ห้ามสร้างชื่อ
endpoint/event จากการคาดเดา
