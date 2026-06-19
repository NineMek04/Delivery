# Mobile Map Navigation Plan

**Document role:** แผนปรับปรุงหน้าวาดแผนที่และนำทางใน Flutter mobile app ให้มีพฤติกรรมใกล้เคียงภาพตัวอย่าง Google Maps navigation

**Scope:** `rider_app` เท่านั้น โดยเฉพาะหน้ารับงาน/ติดตามงานของไรเดอร์ และหน้าติดตามออเดอร์ของลูกค้าในส่วนที่แชร์ route visualization

**Last updated:** 2026-06-18

## 1. เป้าหมาย

ต้องการให้แผนที่ฝั่ง mobile app แสดงผลแบบ navigation-first:

- ไรเดอร์เห็นเส้นทางสีเด่นชัดหลังรับงาน
- กล้องตามตำแหน่งไรเดอร์อัตโนมัติในโหมดนำทาง
- มีแผงคำสั่งด้านบน เช่น “ตรงไป”, “เลี้ยวซ้าย”, ชื่อถนน/จุดหมาย
- มีแผงสรุปด้านล่าง เช่น ETA, ระยะทางคงเหลือ, ปุ่มจบ/ปิด navigation
- มีปุ่ม floating control เช่น center, compass, search/zoom, sound/report
- route ต้องมาจาก backend/local OSRM ก่อนเสมอ
- ถ้า route พังจึง fallback เป็นเส้นตรงและส่ง telemetry เงียบๆ กลับ backend

## 2. ขอบเขตที่ต้องไม่ทำ

- ไม่ย้าย logic วาด route ไปฝั่ง Admin Dashboard
- ไม่ให้ Flutter app เรียก OSRM container โดยตรง
- ไม่ใช้ public OSRM fallback ใน production
- ไม่ใช้พิกัด accuracy `> 50m` เป็นข้อมูล dispatch/navigation หลัก
- ไม่เพิ่ม stack หนักอย่าง Mapbox/Google SDK โดยไม่ตัดสินใจระดับโปรเจกต์ก่อน

## 3. Current State จาก Codebase

ไฟล์หลักที่มีอยู่แล้ว:

```text
rider_app/lib/features/tracking/screens/map_tracking_screen.dart
rider_app/lib/features/delivery/screens/route_tracking_screen.dart
rider_app/lib/features/tracking/customer_tracking_screen.dart
rider_app/lib/core/api/services/rider_route_api_service.dart
rider_app/lib/core/api/services/client_route_telemetry_service.dart
rider_app/lib/core/location/location_service.dart
rider_app/lib/shared/utils/polyline_util.dart
```

สิ่งที่มีแล้ว:

- ใช้ `flutter_map`
- มี `MapController`
- มี `TileLayer`, `PolylineLayer`, `MarkerLayer`, `CircleLayer`
- มี navigation zoom `_navigationZoom = 17.5`
- มี heading animation ของไรเดอร์
- มี `rider-routes/resolve` สำหรับขอ route จาก backend
- มี `client-route-fallback` สำหรับแจ้ง route fallback
- web tile ใช้ `/map-tiles/{z}/{x}/{y}.png`

สิ่งที่ต้องจัดระเบียบเพิ่ม:

- แยก map rendering ออกจาก state/route resolution
- ทำ navigation UI overlay ให้ชัดเหมือนภาพตัวอย่าง
- ทำ route phase และ camera mode เป็น state machine ย่อย
- คำนวณ remaining distance/ETA จาก route tail
- ทำ instruction banner จาก maneuver/heuristic
- ลดการ rebuild ทั้งแผนที่เมื่อพิกัดเปลี่ยน

## 4. Target UX Modes

### 4.1 Route Preview Mode

ใช้ก่อนเริ่มนำทางหรือเมื่อลูกค้าดูภาพรวม:

- fit route ทั้งเส้น
- แสดงจุดต้นทาง/ปลายทาง
- แสดง bubble เวลา เช่น `3 นาที`
- แสดง bottom sheet สรุปจุดหมายและปุ่มเริ่ม
- ไม่ follow rider แบบ aggressive

### 4.2 Turn-By-Turn Navigation Mode

ใช้ตอนไรเดอร์รับงานและกำลังวิ่ง:

- กล้อง follow rider ที่ zoom 17.5
- rider marker เป็นลูกศรหมุนตาม heading
- route tail ตัดส่วนที่ผ่านไปแล้วออก
- top instruction card แสดงคำสั่งถัดไป
- bottom navigation bar แสดง ETA/remaining distance
- มีปุ่ม center/compass/sound/report

### 4.3 Degraded Route Mode

ใช้เมื่อ backend/local OSRM route ไม่พร้อม:

- วาดเส้นตรงแบบสีอ่อนหรือ dashed line
- แสดง badge “เส้นทางประมาณ”
- ส่ง `POST /api/v1/telemetry/client-route-fallback` ครั้งเดียวต่อ order/phase/reason
- navigation ยังทำงานต่อได้ แต่ไม่เอาข้อมูลนี้ไปทำ dispatch scoring

## 5. UI Layout ตามภาพตัวอย่าง

```text
┌───────────────────────────────────────┐
│ Map fills screen                       │
│                                       │
│  ┌─────────────────────────────────┐  │
│  │ Top instruction card             │  │
│  │ icon + direction + road name     │  │
│  └─────────────────────────────────┘  │
│                                       │
│                      [compass]        │
│                      [search/zoom]    │
│                      [sound]          │
│                      [report]         │
│                                       │
│       thick purple/blue route line    │
│             rider arrow marker        │
│                                       │
│  [speed]                              │
│                                       │
│  ┌─────────────────────────────────┐  │
│  │ Bottom ETA panel                 │  │
│  │ 3 นาที | 950 ม. | arrival time   │  │
│  └─────────────────────────────────┘  │
└───────────────────────────────────────┘
```

## 6. Component Plan

### 6.1 สร้าง widget แยก

```text
rider_app/lib/features/tracking/widgets/navigation_instruction_card.dart
rider_app/lib/features/tracking/widgets/navigation_bottom_sheet.dart
rider_app/lib/features/tracking/widgets/navigation_floating_controls.dart
rider_app/lib/features/tracking/widgets/rider_navigation_marker.dart
rider_app/lib/features/tracking/widgets/route_status_badge.dart
```

เหตุผล:

- ลดขนาด `map_tracking_screen.dart`
- เทส/ปรับ UI ได้ง่าย
- reuse ได้ระหว่าง rider tracking และ customer tracking บางส่วน

### 6.2 สร้าง model สำหรับ route view

```text
rider_app/lib/features/tracking/models/navigation_route_view_state.dart
```

Fields:

```dart
enum NavigationRoutePhase { pickup, delivery }
enum NavigationCameraMode { overview, follow, manual }
enum NavigationRouteSource { backendResolved, orderPolyline, geometryFallback, unavailable }

class NavigationRouteViewState {
  final NavigationRoutePhase phase;
  final NavigationCameraMode cameraMode;
  final NavigationRouteSource source;
  final List<LatLng> routePoints;
  final LatLng? riderPoint;
  final LatLng? pickupPoint;
  final LatLng? dropoffPoint;
  final double? remainingMeters;
  final double? remainingSeconds;
  final String? nextInstructionText;
  final bool isDegraded;
}
```

### 6.3 สร้าง service คำนวณ route tail

```text
rider_app/lib/features/tracking/services/navigation_route_view_service.dart
```

Responsibilities:

- decode polyline and fall back to backend coordinates when decode fails
- choose route source priority
- cut route tail from current rider location
- estimate remaining distance
- estimate remaining duration
- generate simple instruction if backend does not return maneuver data

Route source priority:

```text
1. backend-resolved route from /rider-routes/resolve encodedPolyline
2. backend-resolved route coordinates [[lng, lat], ...]
3. order/offer encoded polyline
4. route-unavailable/degraded state
```

## 7. Route Resolution Flow

```text
Rider accepts offer
        |
        v
Delivery provider stores active order + pickup route
        |
        v
Map screen enters NavigationRoutePhase.pickup
        |
        v
Use cached route for orderId|phase
        |
        +--> exists: draw road route tail from rider position
        |
        +--> missing/invalid:
              call POST /api/v1/rider-routes/resolve
                    |
                    +--> LOCAL_OSRM encoded polyline decodes: draw backend route
                    |
                    +--> encoded decode fails but coordinates exist: draw coordinates route
                    |
                    +--> HAVERSINE_FALLBACK/no geometry: route-unavailable and report telemetry
```

When order changes to `DELIVERING`:

```text
NavigationRoutePhase.pickup -> NavigationRoutePhase.delivery
clear pickup route cache for that order
resolve pickup-to-dropoff/current-to-dropoff route
```

When order is `COMPLETED` or `CANCELLED`:

```text
clear active route state
stop follow camera
return to idle/overview screen
```

## 8. Camera Behavior

### 8.1 Follow Mode

Use when rider has active order:

```text
center = riderPoint
zoom = 17.5
rotation = heading if supported safely by flutter_map version
```

If map rotation causes UX or tile issues, keep north-up but rotate rider marker.
Do not block implementation on true Google Maps-style tilted 3D view because
current stack is `flutter_map` raster tiles.

### 8.2 Manual Mode

If user drags map:

- switch `cameraMode` to `manual`
- show “กลับไปตำแหน่งปัจจุบัน” floating button
- do not auto-center until user taps center button

### 8.3 Overview Mode

Use before navigation starts or after completed:

- fit pickup/dropoff/route points
- avoid repeating fit on every GPS tick
- use viewport signature to dedupe camera movement

## 9. Visual Style

Map route:

- active route: thick purple/blue line, 6-8 px
- route shadow/outline: dark translucent line underneath, 9-11 px
- degraded fallback: dashed or lighter line
- completed/passed route: optional grey thin line

Markers:

- rider: blue navigation arrow with white circular base
- pickup/store: orange store marker
- dropoff/customer: red or green pin depending phase
- accuracy: blue translucent circle only when useful

Instruction card:

- dark teal background like sample
- large direction icon
- road/destination text
- optional secondary maneuver pill

Bottom sheet:

- draggable visual handle
- ETA large green text
- remaining distance and arrival time
- close/stop navigation action
- phase action button if needed

## 10. Maneuver Instruction Strategy

Backend currently returns encoded polyline, distance, duration, and source. If
backend does not return maneuver steps yet:

Phase 1 heuristic:

- compare next 3 route segments
- calculate bearing difference
- map bearing delta to:
  - go straight
  - slight left/right
  - turn left/right
  - u-turn
- show destination name when near final point

Phase 2 backend enhancement:

- extend `POST /api/v1/rider-routes/resolve` to optionally return maneuver steps
- keep app compatible if `steps` is absent

## 11. State And Performance Rules

- Do not rebuild the entire `FlutterMap` for every GPS tick if only marker moves.
- Keep route resolution idempotent by `orderId + phase`.
- Cache resolved route geometry per order/phase.
- GPS ticks move the marker and trim route tail only; do not call backend route
  resolve on every location change.
- Retry invalid/missing route geometry only through a cooldown guard or phase
  change.
- Do not register duplicate SignalR listeners on reconnect.
- Dispose `MapController`, animation controllers, and subscriptions.
- Avoid nested async calls inside `build`; route fetch must be triggered by state change guards.

## 12. File-Level Implementation Phases

### Phase 1 - UI Skeleton

Files:

```text
map_tracking_screen.dart
navigation_instruction_card.dart
navigation_bottom_sheet.dart
navigation_floating_controls.dart
rider_navigation_marker.dart
route_status_badge.dart
```

Work:

- replace normal app bar map view with fullscreen Stack
- add top instruction card
- add bottom ETA panel
- add floating controls
- keep existing route drawing logic

Acceptance:

- active order opens map fullscreen
- route line visible
- rider marker visible
- top/bottom overlays do not block essential map gestures

### Phase 2 - Route View State

Files:

```text
navigation_route_view_state.dart
navigation_route_view_service.dart
map_tracking_screen.dart
```

Work:

- centralize route source priority
- calculate route tail
- calculate remaining meters/seconds
- expose `isDegraded`

Acceptance:

- pickup/delivery phases select correct route
- route tail shrinks as rider moves
- bottom ETA updates without manual refresh

### Phase 3 - Backend Route Resolve Integration

Files:

```text
rider_route_api_service.dart
client_route_telemetry_service.dart
map_tracking_screen.dart
```

Work:

- preserve current backend route resolve flow
- throttle re-resolve
- cache per order/phase
- report fallback once per reason

Acceptance:

- no direct OSRM call from app
- backend route appears after accepting order
- fallback telemetry fires once when route is degraded

### Phase 4 - Navigation Camera

Files:

```text
map_tracking_screen.dart
navigation_floating_controls.dart
```

Work:

- implement `follow`, `manual`, `overview`
- user drag switches to manual
- center button returns to follow
- marker heading animation remains smooth

Acceptance:

- active rider is followed at zoom 17.5
- dragging map stops auto-follow
- tapping center resumes follow

### Phase 5 - Maneuver Instructions

Files:

```text
navigation_route_view_service.dart
navigation_instruction_card.dart
```

Work:

- calculate simple turn instruction from bearing deltas
- show destination/road label fallback
- keep backend maneuver steps optional for future

Acceptance:

- instruction card changes while route progresses
- instruction does not crash on short or straight fallback route

### Phase 6 - Customer Tracking Polish

Files:

```text
customer_tracking_screen.dart
shared navigation route widgets if useful
```

Work:

- keep customer map in overview mode by default
- show rider moving along route
- do not show rider-only controls such as report/sound if not relevant

Acceptance:

- customer sees clear route and ETA
- customer UI does not expose rider operational controls

## 13. Test Plan

Manual scenarios:

1. Rider accepts order with valid pickup route.
2. Rider accepts order with missing pickup route; app resolves route through backend.
3. Rider switches from `PICKING_UP` to `DELIVERING`.
4. Local OSRM unavailable; app draws degraded route and reports fallback once.
5. User drags map during active navigation; auto-follow pauses.
6. User taps center; auto-follow resumes.
7. Order completed; map clears active route state.
8. Flutter web uses `/map-tiles/`; native uses normal tile provider/cache.

Automated candidates:

- unit test `navigation_route_view_service`
- widget test instruction/bottom sheet rendering
- provider test route phase changes
- smoke test `map_tracking_screen` with fake active order and fake route

## 14. Risks

| Risk | Mitigation |
|---|---|
| `flutter_map` cannot provide true Google Maps 3D tilt | Use 2D navigation style first; rotate marker and optionally map if stable |
| Re-resolve route too often | throttle by distance/time/order phase |
| UI jank from rebuilds | isolate widgets and avoid route fetch in `build` |
| Wrong route source priority | enforce source order in one service |
| Missing maneuver steps | heuristic first, backend steps optional later |
| Public tile or OSRM abuse | keep local backend route and `/map-tiles/` cache rules |

## 15. Definition Of Done

- Rider map looks and behaves like navigation screen, not generic tracking page
- route line appears immediately after accepted order when data exists
- backend route resolve fills missing route without app calling OSRM directly
- ETA/remaining distance visible in bottom panel
- top instruction card visible in active navigation
- follow/manual/overview camera modes work
- fallback route is visually distinct and telemetry is reported
- no duplicate subscriptions or map controller leaks
- customer tracking remains overview-focused and does not inherit rider controls
