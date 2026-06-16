# Rider App — สรุปงานในเชทนี้ (Session Summary)

> อ่านไฟล์นี้เมื่อเริ่มเชทใหม่เพื่อรู้ว่าทำอะไรไปแล้ว และทดสอบอย่างไรผ่าน Docker

## สถานะโดยรวม

| ส่วน | สถานะ |
|------|--------|
| Core services (Auth, API, SignalR, GPS, Session) | ✅ เสร็จ |
| Providers (Auth, Home, Delivery, Tracking) | ✅ เชื่อมครบ |
| UI ทั้ง 4 หน้าหลัก | ✅ เสร็จ |
| Shared widgets (Offer, OrderCard, Badge, Connection bar) | ✅ เสร็จ |
| Docker Web + nginx proxy `/api` + `/hubs` | ✅ เสร็จ |
| Backend fix `GetMyOrders` (UserId → RiderId) | ✅ เสร็จ |

## สิ่งที่ทำในเชทนี้ (Timeline)

### รอบที่ 1 — เชื่อม Services
- `AuthApiService`, `OrderApiService`, `RiderApiService`
- แก้ `SignalRService` ให้ตรง Hub: `UpdateLocation`, `OfferReceived`, `AcceptOffer`, …
- `RiderSessionService` — orchestrate Online/Offline (SignalR + GPS)
- Wire providers + แก้ `OrderService.GetMyOrdersAsync` ใช้ `User.RiderId`
- Refactor เป็น Riverpod manual (ไม่ต้อง `build_runner` บนเครื่อง dev)

### รอบที่ 2 — UI + Docker (เชทปัจจุบัน)
- **Environment:** same-origin `/api/v1`, `/hubs/tracking` (เหมาะ Docker)
- **nginx/default.conf:** proxy ไป `backend:80`
- **Dockerfile:** build web + nginx, ไม่พึ่ง build_runner
- **UI ครบ:** Login, Home (online toggle + offer), Active Delivery, History, Map
- **Widgets:** `OfferBottomSheet`, `OrderCard`, `StatusBadge`, `ConnectionStatusBar`
- **Utils:** `polyline_util`, `order_status_helper`

## โครงสร้างไฟล์สำคัญ

```
rider_app/lib/
├── core/
│   ├── api/services/     # auth, order, rider REST
│   ├── session/          # rider_session_service.dart
│   ├── signalr/          # signalr_service.dart
│   ├── location/         # location_service.dart
│   └── auth/             # auth_service.dart
├── features/
│   ├── auth/screens/login_screen.dart
│   ├── home/screens/home_screen.dart
│   ├── delivery/screens/ active_delivery, delivery_history
│   └── tracking/screens/map_tracking_screen.dart
└── shared/widgets/       # offer, order_card, badge, connection bar
```

## ทดสอบผ่าน Docker

```bash
# จาก root โปรเจกต์ Delivery
docker compose build rider-app backend
docker compose up -d db redis rabbitmq backend rider-app

# เปิดแอป
# http://localhost:8080
```

- API/SignalR ผ่าน nginx ใน container `rider-app` (same-origin)
- Backend ยัง expose ที่ `http://localhost:5000` สำหรับ debug ตรง

### Flow ทดสอบ
1. Login ด้วยบัญชี Role **Rider** (ต้องมี `User.RiderId` ใน DB)
2. หน้าหลัก → เปิดสวิตช์ **ออนไลน์** → SignalR + GPS
3. Admin dispatch งาน → Rider เห็น **OfferBottomSheet** → รับงาน
4. **งานส่ง** → กดปุ่มเปลี่ยนสถานะ ASSIGNED → PICKING_UP → DELIVERING → COMPLETED
5. **แผนที่** → ดู marker + polyline (ถ้ามี `EncodedPolyline`)

## SignalR Contract (ฝั่ง Rider)

| ทิศทาง | ชื่อ | หมายเหตุ |
|--------|------|----------|
| → Server | `UpdateLocation(lat, lng, accuracy)` | |
| → Server | `UpdateStatus(status)` | AVAILABLE → IDLE |
| → Server | `AcceptOffer`, `RejectOffer` | |
| ← Client | `OfferReceived` | ไม่ใช่ OnOfferReceived |
| ← Client | `OrderStatusChanged` | 2 args: orderId, status |

## ข้อจำกัด / ถัดไป

- **Flutter native (Android/iOS):** ต้อง build ด้วย `--dart-define=API_BASE_URL=http://10.0.2.2:5000`
- **GPS บน Web:** ใช้ Geolocation API ของ browser (ต้อง HTTPS หรือ localhost)
- **Profile screen:** ยังไม่มี (logout อยู่ที่ Home)
- **Push notification / เสียงเตือน offer:** ยังไม่มี

## คำสั่ง build เฉพาะ rider-app

```bash
docker compose build rider-app   # ✅ ผ่านแล้ว (Flutter web + nginx proxy)
docker compose up -d rider-app
```

### Login fix (2026-05-22)
- **Dio path:** เปลี่ยน endpoint เป็น `auth/login` (ไม่มี `/` นำหน้า) เพื่อให้รวมกับ baseUrl `/api/v1` ถูกต้อง
- **GoRouter:** เพิ่ม `refreshListenable` + `context.go('/')` หลัง login สำเร็จ
- **Secure storage:** ตั้ง `WebOptions` สำหรับ Flutter Web

### Docker build fixes (เชทนี้)
- `import package:signalr_netcore/signalr_client.dart` (ไม่ใช่ `signalr_netcore.dart`)
- `withAutomaticReconnect(retryDelays: [...])` สำหรับ v1.4.4
- `nginx/default.conf` proxy `/api` + `/hubs` → `backend:80`

### แผนที่นำทางจริงและการสั่งซื้อด่วนฝั่งลูกค้า (2026-06-16)
- **OSRM Simulation & Dynamic Routing:** ยึดเส้นทางการวิ่งและคำนวณระยะทางที่เหลือตามถนนจริงจากพิกัด OSRM แทนเส้นตรงกระจัดแบบเดิม และวาดเส้นทางแบบตัดปลายจุดที่คนขับเดินทางผ่านพ้นไปแล้ว (`_getTailRoute`) ทำให้ได้เอฟเฟกต์แผนที่หดตัวแบบเรียลไทม์
- **Customer Buy Now / Direct Checkout:** เพิ่มปุ่ม "ซื้อทันที" (Buy Now) บนนิวเคลียสรายการอาหารเพื่อข้ามขั้นตอนหยิบลงตะกร้าเดิม โดยล้างตะกร้าเก่า เติมรายการนี้รายการเดียวเดี่ยวๆ และเปิดบานพับยืนยันสรุปรายการเช็คเอาต์ทันที
- **Wipe Order History (Soft Delete):** ลูกค้าสามารถกดยืนยันการล้างลบประวัติคำสั่งซื้อทั้งหมดที่สำเร็จแล้วออกจากการแสดงผล โดยระบบหลังบ้านจะอัปเดตสถานะลบอาร์เรย์แบบอ่อน (`DelFlag = 'Y'`) เพื่อไม่ให้ส่งผลเสียต่อความสมบูรณ์ในการอ้างอิงข้อมูลของ DB
