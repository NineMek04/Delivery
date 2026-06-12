# State Sync Audit Report — Smart Delivery Routing System
**วันที่ตรวจล่าสุด:** 2026-06-12 | **โดย:** QA Elite Agent  
**Scope:** Backend (.NET 8) ↔ Angular Dashboard ↔ Flutter Rider/Store App

---

## สรุปผลการตรวจสอบ (Executive Summary)

| ลำดับ | ระบบ / จุดบกพร่อง | สถานะ | ความรุนแรง |
|---|---|---|---|
| ✅ **BUG-01** | `OrderStatusChanged` payload mismatch | **Resolved (แก้ไขแล้ว)** | CRITICAL |
| ✅ **BUG-02** | `OrderDto.Status` default = `"PENDING"` (ผิด Enum ทั้ง C# และ Dart) | **Resolved (แก้ไขแล้ว)** | HIGH |
| ✅ **BUG-07** | [NEW] Flutter App Compile Error — เมธอด `sendLocationUpdate` ไม่มีใน `SignalRService` | **Resolved (แก้ไขแล้ว)** | CRITICAL |
| ✅ **BUG-08** | [NEW] Inconsistent Parameter Semantics — ส่ง `speed` แต่หลังบ้านรับเป็น `accuracy` | **Resolved (แก้ไขแล้ว)** | HIGH |
| ✅ **BUG-03** | `signalr-contracts.md` ระบุ payload เป็น JSON object แต่ Backend ส่งแบบ positional args | **Resolved (แก้ไขแล้ว)** | HIGH |
| ✅ **BUG-09** | [NEW] DTO Inconsistency Gap — Angular ขาดฟิลด์ `items`/`shopId` และ Flutter ขาดฟิลด์ `batch` | **Resolved (แก้ไขแล้ว)** | HIGH |
| ✅ **BUG-10** | [NEW] REST API Endpoint Missing — ปุ่มลบข้อมูลทั้งหมดในหน้าบ้าน 404 (ไม่มี endpoint รองรับ) | **Resolved (แก้ไขแล้ว)** | HIGH |
| ✅ **BUG-04** | `AcceptOffer` ใน signalr-contracts ระบุ Rider state = `"OFFERED"` (ไม่มีใน Enum) | **Resolved (แก้ไขแล้ว)** | MEDIUM |
| ✅ **BUG-11** | [NEW] Search Parameter Ignored — ค้นหาใน `CrudControllerBase.GetAll` ถูกเพิกเฉย | **Resolved (แก้ไขแล้ว)** | MEDIUM |
| ✅ **BUG-05** | `UpdateStatus` contract ระบุ `"PICKING_UP"/"DELIVERING"` เป็น Rider status (ไม่มี in Enum) | **Resolved (แก้ไขแล้ว)** | MEDIUM |
| ✅ **BUG-06** | Customer ไม่ได้รับ `OrderStatusChanged` ผ่าน Flutter Customer App (ไม่มี customer SignalR service) | **Resolved (แก้ไขแล้ว)** | MEDIUM |
| ✅ **GAP-01** | [NEW] Customer Map Tracking — ไม่ได้รับพิกัดตำแหน่ง Rider ในแบบเรียลไทม์ | **Resolved (แก้ไขแล้ว)** | MEDIUM |
| ✅ **GAP-02** | [NEW] Connection Reconnection — ขาดการทำ Pull-after-reconnected เพื่อ Sync สถานะของ Order | **Resolved (แก้ไขแล้ว)** | LOW |
| ✅ **OK-01** | GPS (`RiderLocationUpdated`) payload ตรงกันทุก client | **ตรง** | — |
| ✅ **OK-02** | Dispatch events (DispatchScanStarted, DispatchOfferSent) ตรงกัน | **ตรง** | — |
| ✅ **OK-03** | Order State Machine transitions (Backend) ครบถ้วนถูกต้อง | **ตรง** | — |
| ✅ **BUG-12** | Multi-drop ส่ง GPS ให้ลูกค้าได้คนเดียว (Redis เก็บ customer เดียวต่อ rider แม้รองรับ 3 orders) | **Resolved (แก้ไขแล้ว)** | HIGH |
| ✅ **BUG-13** | หากล้าง active-order cache ตอนจบงานไม่สำเร็จ cache เก่าจะส่ง GPS ผิดคนค้าง 24 ชม. | **Resolved (แก้ไขแล้ว)** | HIGH |
| ✅ **BUG-14** | เมื่อสลับไปติดตาม Order ใหม่ พิกัดไรเดอร์จาก Order ก่อนหน้าไม่ถูกล้าง | **Resolved (แก้ไขแล้ว)** | MEDIUM |
| ✅ **BUG-15** | หน้าติดตาม force unwrap พิกัด nullable อาจ crash เมื่อไม่มีพิกัด | **Resolved (แก้ไขแล้ว)** | LOW |
| ❌ **BUG-16** | Telemetry Double-Counting for STALE Riders in `TelemetryBroadcastWorker` | **Pending (ยังไม่แก้ไข)** | MEDIUM |
| ❌ **BUG-17** | Missing SignalR Notification on Offer Reject/Timeout in `DispatchOfferHandler` | **Pending (ยังไม่แก้ไข)** | HIGH |
| ❌ **BUG-18** | Missing SignalR Broadcast for Rider STALE/OFFLINE in `HeartbeatMonitor` | **Pending (ยังไม่แก้ไข)** | MEDIUM |
| ❌ **BUG-19** | Indeterminate Order Cancellation for Multi-Drop Riders on Admin Map | **Pending (ยังไม่แก้ไข)** | LOW |

---

## ✅ BUG-01 — Resolved: `OrderStatusChanged` Payload Mismatch

### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: Backend เปลี่ยนมาส่ง JSON object เป็น payload เดียวแทน positional arguments แล้ว
- **Backend**: ใน [OrderNotificationService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/OrderNotificationService.cs) ปรับให้สร้าง anonymous object payload ที่มีฟิลด์ `orderId`, `orderRefNumber`, `previousStatus`, `newStatus`, `riderId`, `timestamp` และส่งไปยังทุกกลุ่ม (admins, rider, store, customer) อย่างสมบูรณ์
- **Angular Dashboard**: อัปเดต [tracking-signalr.service.ts](file:///c:/Users/ASUS/Desktop/Project/Delivery/admin-dashboard/src/app/core/services/tracking-signalr.service.ts) ให้รองรับการแกะ payload จาก object ใหม่นี้ โดยยังมี fallback สำหรับ positional args แบบเดิม
- **Flutter Client**: อัปเดต `signalr_service.dart` และ `customer_signalr_service.dart` ให้สามารถ parse JSON object เป็น map และดึงค่า status/orderId ได้อย่างถูกต้องโดยมี backward compatibility

---

## ✅ BUG-02 — Resolved: `OrderDto.Status` Default = `"CREATED"`

### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: เปลี่ยนค่าเริ่มต้นจาก `"PENDING"` เป็น `"CREATED"` ให้ตรงกับ state machine
- **Backend (C#)**: อัปเดต [OrderDto.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Models/DTOs/OrderDto.cs) ให้มีค่าเริ่มต้นเป็น `"CREATED"`
- **Flutter (Dart)**: อัปเดต [order.dart](file:///c:/Users/ASUS/Desktop/Project/Delivery/rider_app/lib/models/order.dart) ให้ status default เป็น `'CREATED'` และลบค่าคงที่ dead code `'PENDING'` ออกจาก `app_constants.dart`

---

## ✅ BUG-03 — Resolved: Contract vs Reality — Document Updated

### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: เนื่องจากโค้ดฝั่ง Backend ได้เปลี่ยนไปส่ง JSON object ตรงกับความตั้งใจเดิมของเอกสารเรียบร้อยแล้ว และตัวเอกสาร [signalr-contracts.md](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/contracts/signalr-contracts.md) ได้รับการอัปเดตให้แสดงรายละเอียดตรงกับโค้ดเรียบร้อยแล้ว

---

## ✅ BUG-04 — Resolved: `AcceptOffer` Rider State Document Corrected

### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: ปรับปรุงเอกสาร [signalr-contracts.md](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/contracts/signalr-contracts.md) ให้ตรงตามความเป็นจริงของระบบ state machine คือไรเดอร์จะเปลี่ยนสถานะจาก `RESERVED` → `BUSY` เมื่อกดตอบรับงานออเดอร์สำเร็จ

---

## ✅ BUG-05 — Resolved: `UpdateStatus` Contract Rider Status Corrected

### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: แก้ไขเอกสาร [signalr-contracts.md](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/contracts/signalr-contracts.md) เพื่อแก้ไขหมายเหตุและตัวอย่างพารามิเตอร์ `UpdateStatus` ของไรเดอร์ โดยระบุว่ารองรับสถานะ `OFFLINE | IDLE | RESERVED | BUSY | STALE` และชี้แจงว่าสถานะการจัดส่งออเดอร์ (`PICKING_UP`, `DELIVERING`) จะต้องอัปเดตผ่าน REST API เท่านั้น ไม่ใช่ผ่าน SignalR status update ของไรเดอร์

---

## ✅ BUG-06 — Resolved: Customer App SignalR Integration

### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: สร้างคลาส [customer_signalr_service.dart](file:///c:/Users/ASUS/Desktop/Project/Delivery/rider_app/lib/core/signalr/customer_signalr_service.dart) ขึ้นมาใหม่สำหรับจัดการการเชื่อมต่อ SignalR ของผู้ใช้งานทั่วไป (Customer) และผูกการอัปเดตของออเดอร์ (OrderStatusChanged) เข้าสู่หน้าจอติดตามออเดอร์และหน้าจอแสดงรายการออเดอร์ของลูกค้าในแอปพลิเคชันอย่างสมบูรณ์ ทำให้แอปรีเฟรชข้อมูลตาม SignalR broadcast เสนอ
- **หมายเหตุ**: พบช่องว่างเพิ่มเติมเกี่ยวกับตำแหน่งไรเดอร์ (Rider Location GPS) ที่ส่งผ่าน SignalR แต่ยังไม่แสดงผลบนแผนที่ฝั่งลูกค้า ซึ่งบันทึกไว้ในหัวข้อ **GAP-01** ด้านล่าง

---

## ✅ สิ่งที่ทำงานถูกต้อง

### GPS Location Sync — ✅ ตรงกันทุกชั้น

| Layer | ส่ง/รับ | Field names |
|---|---|---|
| Backend broadcast | `RiderLocationUpdated` | `lat`, `lng`, `riderId`, `timestamp`, `state` |
| Angular | รับ `data.lat || data.Lat` | ✅ fallback mapper ครบ |
| Flutter | รับ `map['lat'] ?? map['Lat']` | ✅ fallback mapper ครบ |

### Order State Machine (Backend) — ✅ ถูกต้อง

State transitions ใน `StateMachineService` ตรงกับ contract ทุก path:
- `CREATED → MATCHING → OFFERING → ASSIGNED → PICKING_UP → DELIVERING → COMPLETED`
- Rider transitions: `OFFLINE ↔ IDLE ↔ RESERVED ↔ BUSY ↔ STALE`

---

## แผนแก้ไขที่แนะนำ (สำหรับ Bug ในส่วนที่ 1)

### Priority 1 — แก้ทันที

1. **แก้ `OrderDto.Status` default** จาก `"PENDING"` → `"CREATED"` (ทั้ง C# และ Dart)

2. **แก้ `OrderNotificationService.NotifyOrderStatusChangedAsync`** ให้ส่ง enriched payload แทน positional args:
   ```csharp
   await _hubContext.Clients.Group("admins").SendAsync(
       "OrderStatusChanged",
       new {
           orderId = order.Id,
           orderRefNumber = order.TrackingCode,
           previousStatus = (string?)null, // pass as parameter
           newStatus = order.State.ToString(),
           riderId = order.AssignedRiderId,
           timestamp = DateTime.UtcNow
           // previousStatus สามารถดึงมาใส่เพิ่มได้
       }, ct);
   ```
   > ⚠️ **ต้องอัปเดต Angular และ Flutter client** ให้รับแบบ JSON object ด้วย (Flutter มี fallback แล้ว)

### Priority 2 — แก้ใน Sprint ถัดไป

3. **แก้ `signalr-contracts.md`**: ระบุ Rider states ที่ถูก (`OFFLINE|IDLE|RESERVED|BUSY|STALE`)

4. **สร้าง Customer SignalR Service** ใน Flutter customer app เพื่อรับ `OrderStatusChanged` และ track delivery real-time

---

## Data Flow Diagram (สรุปวิเคราะห์)

```
Rider กด "รับงาน"
  │
  ▼ SignalR: AcceptOffer(offerId, version)
Backend: DispatchOfferHandler.AcceptOfferAsync()
  │  ├─ DB Transaction: Order.State → ASSIGNED
  │  ├─ DB Transaction: Rider.State → BUSY
  │  └─ NotifyOrderStatusChangedAsync(order)
  │       │
  │       ├─→ "admins" group: SendAsync("OrderStatusChanged", orderId, "ASSIGNED")
  │       │       └─ Angular: ✅ รับได้ (positional args)
  │       │
  │       ├─→ "rider:{id}" group: SendAsync("OrderStatusChanged", orderId, "ASSIGNED")
  │       │       └─ Flutter Rider: ✅ รับได้ (fallback)
  │       │
  │       ├─→ "store:{id}" group: SendAsync("OrderStatusChanged", orderId, "ASSIGNED")
  │       │       └─ Flutter Store: ✅ รับได้ (fallback)
  │       │
  │       └─→ "customer:{id}" group: SendAsync("OrderStatusChanged", orderId, "ASSIGNED")
  │               └─ Flutter Customer: ❌ ไม่มี listener! (BUG-06)
  │
  ▼
Rider กด "PICKING_UP"
  │
  ▼ REST API: PATCH /api/orders/{id}/status {status: "PICKING_UP"}
Backend: OrderService.UpdateOrderStatusAsync()
  │  └─ NotifyOrderStatusChangedAsync() → broadcast ซ้ำ (ครบทุก group เหมือนเดิม)
```

---

## Section 2: Cancellation Logic & ETA Calculation Audit

---

### 📋 Cancellation Logic — สรุปผล

| ลำดับ | จุดที่ตรวจ | Status | ความรุนแรง |
|---|---|---|---|
| ✅ **CANCEL-01** | `DELIVERING → CANCELLED` ไม่มีใน `OrderStateRules` | **Resolved (แก้ไขแล้ว)** | HIGH |
| ✅ **CANCEL-02** | ยกเลิก Order ที่ยัง `OFFERING` ไม่ได้ Release Redis Offer Lock | **Resolved (แก้ไขแล้ว)** | HIGH |
| 🟡 **CANCEL-03** | `DispatchTimeoutWorker` scan interval 5s แต่ Offer TTL 30s — OK แต่มี race window | **Minor Gap (ยืนยันความถูกต้อง)** | LOW |

---

### ✅ CANCEL-01 — Resolved: `DELIVERING → CANCELLED` Added to OrderStateRules

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: เพิ่มกฎการข้ามสถานะ `(OrderState.DELIVERING, OrderState.CANCELLED) => true` เข้าไปใน [OrderState.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Core/StateMachines/OrderState.cs) ทำให้ผู้ดูแลระบบสามารถกดยกเลิกรายการสั่งซื้อที่อยู่ระหว่างขั้นตอนกำลังจัดส่งได้สำเร็จในกรณีฉุกเฉิน

---

### ✅ CANCEL-02 — Resolved: Release Redis Offer Lock on Cancellation

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: สร้างเมธอดผู้ช่วย `CleanupOfferReservationAfterCancellationAsync` ใน [OrderService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/OrderService.cs) ซึ่งจะทำหน้าที่ยกเลิกและคืน Redis lock (`ReleaseLockAsync`) ของไรเดอร์ที่ติดค้างอยู่ และรีเซ็ตฟิลด์ข้อเสนอออเดอร์ออกอย่างถูกต้อง ทันทีที่มีการเรียกใช้ฟังก์ชัน `CancelOrderAsync` หรืออัปเดตสถานะออเดอร์เป็น `CANCELLED`

---

### 🟡 CANCEL-03 — LOW: DispatchTimeoutWorker scan 5s แต่ OFFERING window 30s

`DispatchTimeoutWorker` scan ทุก 5 วินาที — **ดีกว่า spec** (ที่กำหนด 30s interval) แต่มี window เล็กน้อยที่อาจเกิด duplicate scan:

- Worker ทำงานตรง `OfferExpiresAt < now` ซึ่งถูกต้องแล้ว ✅
- Idempotency ป้องกันด้วย Redis `lock:offer:{offerId}` 5s lock ✅
- ถ้า worker crash ขณะกำลัง process → offer จะถูก pick up ใน scan รอบถัดไป ✅

สรุป: จัดการได้ดีแล้ว ไม่มีปัญหาจริง

---

### 📋 ETA Calculation — สรุปผล

| ลำดับ | จุดที่ตรวจ | Status | ความรุนแรง |
|---|---|---|---|
| ✅ **ETA-01** | `weather_condition` และ `traffic_level` ถูก hardcode เป็น `"clear"` / `"normal"` | **Resolved (แก้ไขแล้ว)** | MEDIUM |
| ✅ **ETA-02** | Fallback ETA (C# side) ไม่รวม `dispatch_pickup_seconds` แต่ AI Engine รวม | **Resolved (แก้ไขแล้ว)** | LOW |
| ✅ **ETA-03** | Re-calculate ETA ตอน Dispatch พร้อม cumulative batch timing | **ถูกต้อง** | — |

---

### ✅ ETA-01 — Resolved: Dynamic Weather and Traffic Factors

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: ดึงค่าปัจจัยการจราจรและสภาพอากาศจากไฟล์ config (`appsettings.json` ผ่าน `EtaPrediction:WeatherCondition` และ `EtaPrediction:TrafficLevel`) นอกจากนี้ยังเพิ่มระบบคาดเดาความหนาแน่นของการจราจรตามเวลาชั่วโมงเร่งด่วนของวันโดยอัตโนมัติ (Rush Hour: 7-9AM, 5-7PM -> `heavy` และ Late Night: 10PM-5AM -> `light`) เพื่อส่งข้อมูลจริงเข้าไปคำนวณใน AI Engine

---

### ✅ ETA-02 — Resolved: Fallback ETA Calculations Adjusted

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: ปรับปรุงเมธอด `GenerateFallbackEtaResponse` ใน [AiService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/AiRouting/AiService.cs) ให้บวกเวลาในส่วนของการเดินทางไปรับสินค้า (Pickup Overhead: 10 นาที) และเวลาการส่งมอบสินค้า (Dropoff Overhead: 3 นาที) รวมไปถึงตัวคูณคัดกรองสภาพจราจรและสภาพอากาศ เพื่อให้ได้ค่าเวลาสำรองที่สมจริงใกล้เคียงกับการคำนวณจริงของฝั่ง Python AI Engine

---

### ✅ ETA-03 — ถูกต้อง: Batch ETA Re-calculation

DispatchService คำนวณ ETA แบบ cumulative สำหรับ batch orders:

```csharp
// DispatchService.cs:410-436
double cumulativePickupSeconds = pickupRouteDurationSeconds.Value;
foreach (var order in sortedOrders)
{
    // ... เรียก AI ETA ต่อ order โดยใช้ cumulative time
    cumulativePickupSeconds += order.RouteDurationSeconds + 180; // +3 นาที handoff
}
```

ถูกต้องตาม multi-stop delivery logic ✅

---

## Section 3: System Connection & REST/SignalR Data Fetching Audit
**เพิ่มเมื่อ:** 2026-06-11 | **Scope:** การดึงข้อมูลความสอดคล้อง (Data Consistency) และการสื่อสารระหว่าง Node

---

### 📋 Connection & API Gaps — สรุปผล

| ลำดับ | จุดที่ตรวจ | Status | ความรุนแรง |
|---|---|---|---|
| ✅ **BUG-07** | Flutter App Compile Error — `sendLocationUpdate` เรียกใช้เมธอดที่ไม่มีจริง | **Resolved (แก้ไขแล้ว)** | CRITICAL |
| ✅ **BUG-08** | Inconsistent Parameter Semantics — GPS Accuracy vs Speed | **Resolved (แก้ไขแล้ว)** | HIGH |
| ✅ **BUG-09** | DTO Inconsistency Gap — Angular ขาดฟิลด์ `items`/`shopId`/`customerId` และ Flutter ขาดฟิลด์สำหรับงานพ่วง | **Resolved (แก้ไขแล้ว)** | HIGH |
| ✅ **BUG-10** | REST API Endpoint Missing — ปุ่มล้างข้อมูลทั้งหมดในหน้าบ้านจะ Error 404/405 | **Resolved (แก้ไขแล้ว)** | HIGH |
| ✅ **BUG-11** | Search Query Ignored — พารามิเตอร์ `search` ใน `CrudControllerBase` ไม่ถูกส่งไปกรองใน DB | **Resolved (แก้ไขแล้ว)** | MEDIUM |

---

### ✅ BUG-07 — Resolved: `sendLocationUpdate` Implemented in Flutter Client

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: เพิ่มเมธอด `sendLocationUpdate` และ `updateLocation` เข้าไปในคลาส `SignalRService` ของ Flutter เพื่อเรียกส่งพิกัดพร้อมค่าความแม่นยำ (accuracy) ไปยัง SignalR Hub ส่งผลให้แก้ปัญหาคอมไพล์ไม่ผ่านบนโมบายแอปพลิเคชันได้สำเร็จ

---

### ✅ BUG-08 — Resolved: Aligned GPS Semantics (Accuracy)

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: ปรับเปลี่ยนพารามิเตอร์ตัวที่ 3 ในฟังก์ชันส่งตำแหน่งของฝั่ง Flutter ให้ส่งค่า `accuracy` เข้าไปแทน `speed` ตามข้อกำหนด API ของฝั่งหลังบ้าน ทำให้ระบบ telemetry คัดกรองสัญญาณรบกวนสามารถคำนวณตำแหน่งและแสดงจุดพิกัดการเดินทางของไรเดอร์ขึ้นแผงควบคุมได้อย่างถูกต้องโดยพิกัดไม่ถูกลบทิ้งเมื่อความเร็วมากกว่า 50 km/h

---

### ✅ BUG-09 — Resolved: DTO Synchronized (Angular & Flutter)

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: 
  - **Angular**: เพิ่มฟิลด์ `items`, `shopId`, `customerId` รวมไปถึงฟิลด์สัดส่วนงานพ่วงและข้อมูล polyline อื่นๆ เข้าไปในโมเดลอินเทอร์เฟซ `OrderDto` ส่งผลให้หน้าจอควบคุมสามารถแสดงรายการสินค้าและร้านค้าปลายทางได้ถูกต้อง
  - **Flutter**: เพิ่มตัวแปรสำหรับรับข้อมูลงานพ่วง (`batchGroupId`, `batchSequence`, `batchSize`, `routeDistanceMeters`, `routeDurationSeconds` และรายการสินค้า `items`) ใน `OrderDto` เรียบร้อยแล้ว ไรเดอร์สามารถมองเห็นลำดับการส่งและข้อมูลงานพ่วงที่สอดคล้องกันได้จริง

---

### ✅ BUG-10 — Resolved: Security Hardening (Dangerous Feature Removed)

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: เพื่อรักษาความปลอดภัยให้กับข้อมูลจริงในฐานข้อมูล และป้องกันการลบข้อมูลทั้งหมดในกระบวนการทำงานจริง ฝ่ายพัฒนาได้ตัดสินใจทำการ **ลบปุ่มและฟังก์ชัน `deleteAllData()`** ออกจากหน้าจอและบริการหลังบ้านของ Angular Dashboard ทั้งหมด และลบ API endpoint ดังกล่าวออกจากโค้ดของฝั่งหลังบ้าน เพื่อความปลอดภัยสูงสุด (Security-by-Design)

---

### ✅ BUG-11 — Resolved: Generic Search in Base Controller

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: พัฒนาฟังก์ชัน `ApplySearch` ในคลาสแม่ [CrudControllerBase.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Core/CrudControllerBase.cs) โดยใช้ Dynamic Expression Trees และ Reflection ในการดึงและคัดกรองข้อมูลทุกฟิลด์ที่เป็นประเภท `string` ของเอนทิตีนั้นๆ ส่งผลให้คอนโทรลเลอร์ย่อยทั้งหมดที่สืบทอดมาจากคลาสแม่สามารถใช้ความสามารถการค้นหาข้อความ (search parameter) ได้อย่างอัตโนมัติ

---

## Section 4: Newly Identified Gaps (ช่องว่างใหม่ที่ตรวจพบ)

### ✅ GAP-01 — Resolved: Customer Map Tracking

#### ปัญหา
- แม้ว่าฝั่งหลังบ้านใน `TelemetryService.cs` จะทำหน้าที่กระจายสัญญาณพิกัด `RiderLocationUpdated` ไปยังกลุ่มลูกค้า `customer:{customerId}` แล้ว แต่จากการตรวจสอบโค้ดในแอปพลิเคชันลูกค้า (`rider_app/lib/core/signalr/customer_signalr_service.dart`) พบว่ายังไม่มีการเปิดตัวรับอีเวนต์ (SignalR handler) `'RiderLocationUpdated'` และเก็บข้อมูลส่งต่อออกมา
- นอกจากนี้ตัวจัดการสถานะออเดอร์ในหน้าแผนที่ติดตาม (`ActiveOrderNotifier` ใน `tracking_provider.dart`) ไม่ได้ลงทะเบียนฟังก์ชันการรับพิกัดพนักงานขับรถ ส่งผลให้ตัวแปร `riderLat` และ `riderLng` ใน `ActiveOrderState` มีสถานะเป็น `null` เสมอ แผนที่ติดตามออเดอร์ฝั่งลูกค้าจึง **ไม่แสดง** ตำแหน่งการขับขี่แบบสดของไรเดอร์

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**:
  - พัฒนาการรับฟีดพิกัด `'RiderLocationUpdated'` ในตัวรับฝั่ง Client และคัดกรองข้อมูล GPS ด้วย `assignedRiderId` ของไรเดอร์ที่รับงานออเดอร์นั้น ๆ เพื่อความถูกต้องและความปลอดภัยของข้อมูลตำแหน่ง
  - อัปเดตรายละเอียดและ payload ลงในสัญญา [signalr-contracts.md](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/contracts/signalr-contracts.md) เรียบร้อย
  - ทดสอบบิลด์แอปไรเดอร์ (`rider-app`) ด้วย Docker Compose สำเร็จเรียบร้อยแล้ว

---

### ✅ GAP-02 — Resolved: Connection Reconnection Sync

#### ปัญหา
- ในช่วงจังหวะการเดินทางจริงของไรเดอร์/ลูกค้า เมื่อเครือข่ายหลุดและระบบเชื่อมต่อ SignalR ใหม่สำเร็จ (`onreconnected` callback) หากมีสถานะออเดอร์ที่ถูกปรับเปลี่ยนระหว่างหลุดสัญญาณไป การอัปเดตเหล่านั้นจะไม่ถูกส่งย้อนหลังมาถึงทาง websocket
- ปัจจุบันแอปพลิเคชันยังไม่มีกระบวนการดึงข้อมูลทวนซ้ำ (fetch latest state from API) ทันทีเมื่อกลับมาออนไลน์ ส่งผลให้อาจเกิดความแตกต่างระหว่างหน้าจอกับฐานข้อมูลหลังบ้าน

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**:
  - ผูกลอจิกเรียกดึงข้อมูลออเดอร์ล่าสุด (`_refreshOrder()`) ทันทีที่เชื่อมต่อ SignalR คืนสัญญาณสำเร็จ ผ่าน `onReconnected` callback
  - นำเทคนิคสุ่มหน่วงเวลา (Jitter Delay) มาใช้ในการเรียก API ดึงข้อมูลล่าสุดหลังเชื่อมต่อสำเร็จ เพื่อป้องกันปัญหา Thundering Herd

---

## Section 5: Newly Identified Gaps after Implementation (จุดบกพร่อง/ช่องว่างที่พบใหม่ล่าสุด)

---

### ✅ BUG-12 — Resolved: Multi-drop ส่ง GPS ให้ลูกค้าได้ทุกคน (Redis Multi-Recipient Hash Cache)

#### ปัญหา
- ใน [StateMachineService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/DispatchManagement/StateMachineService.cs) (บรรทัดที่ 109) และ [TelemetryService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/Telemetry/TelemetryService.cs) (บรรทัดที่ 243) การแมปเพื่อระบุว่าไรเดอร์คนใดรันออเดอร์ของลูกค้ารายใดอยู่ใน Redis ใช้ Key รูปแบบ: `riders:active_order:{riderId}` ซึ่งเก็บฟิลด์ `customer_id` และ `order_id` แบบค่าเดี่ยว (Single Field)
- จากสถาปัตยกรรมระบบที่รองรับการจัดส่งแบบพ่วงสูงสุด 3 ออเดอร์ (Multi-drop/Batch) ส่งผลให้คีย์ดังกล่าวถูกเขียนทับด้วยข้อมูลออเดอร์ตัวสุดท้ายในกลุ่มเท่านั้น ส่งผลให้ลูกค้ารายอื่น ๆ ในกลุ่มพ่วงงานเดียวกันไม่ได้รับสัญญาณพิกัดตำแหน่งขับเคลื่อนสดของไรเดอร์

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**: 
  - พัฒนาคลาสช่วยเหลือ [ActiveOrderRecipientCache.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/Telemetry/ActiveOrderRecipientCache.cs) เพื่อจัดเก็บโครงสร้าง Hash แบบใหม่ใน Redis เป็นฟิลด์ `order:{orderId} -> customerId` และมีการกำหนดเวอร์ชัน Schema `__schema = 2`
  - ปรับปรุง [StateMachineService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/DispatchManagement/StateMachineService.cs) ให้ทำการคิวรีออเดอร์ทั้งหมดของไรเดอร์ที่มีสถานะทำงาน (`ASSIGNED`, `PICKING_UP`, `DELIVERING`) และเรียกเขียนแคชใหม่ทดแทนแบบ Atomic ด้วยสคริปต์ Lua
  - อัปเดต [TelemetryService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/Telemetry/TelemetryService.cs) ในการดึงและลูปส่งพิกัดตำแหน่งให้กับทุกลูกค้าที่อยู่ใน Active Order ของไรเดอร์คนดังกล่าว

---

### ✅ BUG-13 — Resolved: Cache Stale Protection with Short TTL & Fallback

#### ปัญหา
- ใน [StateMachineService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/DispatchManagement/StateMachineService.cs) (บรรทัดที่ 117-122) เมื่อปิดงานจัดส่งสำเร็จ (`COMPLETED`) หรือออเดอร์ถูกยกเลิก (`CANCELLED`) ระบบจะสั่งลบ Redis key `riders:active_order:{riderId}` แต่หากเกิดข้อผิดพลาดในการเรียกลบ (เช่น Redis connection timeout) ตัวระบบดักจับ Exception เอาไว้เพื่อไม่ให้ขัดจังหวะการทำธุรกรรมหลัก (บรรทัดที่ 125) โดยไม่มีการสั่งรันลบซ้ำ (Retry Mechanism)
- ส่งผลให้คีย์แคชอ้างอิงออเดอร์เก่ายังคงค้างอยู่ในฐานข้อมูล Redis ต่อเนื่องยาวนานถึง 24 ชั่วโมงตามค่า TTL (บรรทัดที่ 117) ทำให้ตำแหน่งพิกัด GPS ใหม่ของไรเดอร์รายดังกล่าวถูกส่งตรงไปยังไคลเอนต์ของลูกค้ารายเดิมในงานก่อนหน้าอยู่ตลอดเวลา

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**:
  - กำหนดค่าหมดอายุของแคชไรเดอร์ (TTL) สั้นลงเหลือเพียง **30 วินาที** (`ActiveOrderRecipientCache.TimeToLive = TimeSpan.FromSeconds(30)`)
  - พัฒนาระบบสำรองใน [TelemetryService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/Telemetry/TelemetryService.cs) เพื่อสืบค้นข้อมูลโดยตรงจาก PostgreSQL (Source of Truth) และรีเฟรชแคชใหม่เมื่อเกิดเหตุการณ์ Cache Miss หรือเวอร์ชัน Schema ของเดิมไม่สอดคล้องกัน (เช่น Schema Marker mismatch)

---

### ✅ BUG-14 — Resolved: Rider Coordinates Reset on Order Switch & Reassignment

#### ปัญหา
- ใน [tracking_provider.dart](file:///c:/Users/ASUS/Desktop/Project/Delivery/rider_app/lib/features/tracking/providers/tracking_provider.dart) (บรรทัดที่ 108) เมธอดสร้างสำเนาสถานะ `copyWith` ของ `ActiveOrderState` ใช้การคืนค่าพิกัดพนักงานขับรถตาม `riderLat: riderLat ?? this.riderLat` และ `riderLng: riderLng ?? this.riderLng`
- เมื่อลูกค้ากดเปลี่ยนไปชมแผนที่ติดตามออเดอร์ใบใหม่ พิกัดและหมุดตำแหน่งของไรเดอร์คนเดิมจะคงค้างอยู่บนแผนที่ออเดอร์ใหม่โดยทันที และจะไม่ถูกปรับเปลี่ยนจนกว่าจะได้รับสัญญาณพิกัดชุดแรกจากไรเดอร์คนใหม่จริง ๆ เข้ามาทาง SignalR

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**:
  - ปรับปรุง `watchOrder` ใน [tracking_provider.dart](file:///c:/Users/ASUS/Desktop/Project/Delivery/rider_app/lib/features/tracking/providers/tracking_provider.dart) ให้รีเซ็ตค่าสถานะเริ่มต้นด้วย `ActiveOrderState(isLoading: true)` ซึ่งเป็นการล้างพิกัดละติจูดและลองจิจูดของไรเดอร์คนเก่าออกทันทีที่สลับออเดอร์
  - ในส่วนของเมธอด `_refreshOrder` เมื่อออเดอร์ถูกอัปเดตและพบการเปลี่ยนแปลงของ `assignedRiderId` (เช่น ไรเดอร์คนใหม่ได้รับมอบหมายงานแทนคนเดิม) ระบบจะทำการล้างพิกัดเดิมทันที
  - มีการเช็ค `_watchedOrderId` ก่อนอัปเดตสถานะเพื่อป้องกันปัญหาสัญญาณตอบกลับจากเน็ตเวิร์กย้อนกลับมาทับซ้อนกัน (Race Condition)

---

### ✅ BUG-15 — Resolved: Null-safe Coordinates in Tracking Screen

#### ปัญหา
- ใน [customer_tracking_screen.dart](file:///c:/Users/ASUS/Desktop/Project/Delivery/rider_app/lib/features/tracking/customer_tracking_screen.dart) (บรรทัดที่ 74 และ 81) มีการใช้เครื่องหมายแกะค่าแบบเด็ดขาด `!` (Force Unwrap) บนพิกัดตำแหน่งร้านค้าและลูกค้า ได้แก่ `state.order!.pickupLat!` และ `state.order!.dropoffLat!`
- เนื่องจากตัวแปรในโมเดล `OrderDto` ของแอปไรเดอร์ถูกกำหนดเป็นแบบ Nullable (`double?`) การบังคับแกะค่าดังกล่าวจะทำให้แอปพลิเคชันเกิดข้อผิดพลาด Null Pointer Exception และเกิดการล่มแครชทันทีหากออเดอร์นั้นไม่มีระบุค่าพิกัดพิกเตอร์ในระบบ

#### สถานะการแก้ไข (Resolution)
- **แก้ไขเรียบร้อยแล้ว**:
  - พัฒนาฟังก์ชันปลอดภัย `_toPoint` ใน [customer_tracking_screen.dart](file:///c:/Users/ASUS/Desktop/Project/Delivery/rider_app/lib/features/tracking/customer_tracking_screen.dart) เพื่อกรองและตรวจสอบพิกัด (เช่น เช็คค่า `null`, ตรวจสอบพิกัดจริงด้วย `isFinite` และขอบเขตละติจูด/ลองจิจูด) ก่อนเรนเดอร์ลงในแผนที่
  - เปลี่ยนการเขียนแบบบังคับแกะค่าด้วยเครื่องหมาย `!` มาใช้แบบกำหนดเงื่อนไขการเรนเดอร์ Marker (Conditional rendering) เพื่อป้องกันแอปพลิเคชันแครชเมื่อพิกัดไม่มีค่า

---

## ❌ BUG-16 — Pending: Telemetry Double-Counting for STALE Riders in `TelemetryBroadcastWorker`

### ปัญหา
- ในไฟล์ [TelemetryBroadcastWorker.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/BackgroundWorkers/TelemetryBroadcastWorker.cs) บรรทัดที่ 108-111 มีความขัดแย้งของการจัดกลุ่มเอนทิตีสถานะไรเดอร์:
  - `activeRiders` ถูกคำนวณจาก `stateCounts.Where(s => s.State != RiderState.OFFLINE).Sum(s => s.Count)` (ซึ่งทำให้ไรเดอร์ที่เป็น `STALE` ถูกนับรวมเข้าไปด้วยเพราะ `STALE != OFFLINE`)
  - `offline` ถูกคำนวณจาก `stateCounts.Where(s => s.State == RiderState.OFFLINE || s.State == RiderState.STALE).Sum(s => s.Count)` (ซึ่งก็นับรวม `STALE` ด้วยเช่นกัน)
- ส่งผลให้ในหน้า Admin Dashboard ข้อมูลภาพรวม Telemetry ของทั้งระบบมีความคลาดเคลื่อนเนื่องจากไรเดอร์สถานะ `STALE` ถูกนับซ้ำทั้งในกลุ่ม active และ offline

---

## ❌ BUG-17 — Pending: Missing SignalR Notification on Offer Reject/Timeout in `DispatchOfferHandler`

### ปัญหา
- ในคลาส [DispatchOfferHandler.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/DispatchManagement/DispatchOfferHandler.cs) เมธอด `RejectOrTimeoutAsync` (บรรทัดที่ 162-258) เมื่อไรเดอร์กดปฏิเสธหรือหมดเวลาส่งข้อเสนอ ออเดอร์จะถูกเปลี่ยนสถานะกลับไปเป็น `OrderState.MATCHING` ในฐานข้อมูล และระบบจะยิง integration event ผ่าน RabbitMQ
- อย่างไรก็ตาม เมธอดนี้ไม่ได้เรียกใช้งาน `orderNotifier.NotifyOrderStatusChangedAsync` เพื่ออัปเดตสถานะออเดอร์ไปยัง SignalR clients ส่งผลให้หน้าจอผู้ดูแลระบบ (Admin Dashboard) ร้านค้า (Store Partner) และลูกค้า (Customer App) ไม่รับรู้อีเวนต์นี้ในแบบเรียลไทม์ และจะแสดงสถานะค้างเป็น `OFFERING` หรือระบุไรเดอร์คนเดิมไปจนกว่าจะมีการทำ offer งานรอบใหม่สำเร็จ

---

## ❌ BUG-18 — Pending: Missing SignalR Broadcast for Rider STALE/OFFLINE in `HeartbeatMonitor`

### ปัญหา
- ในคลาส [HeartbeatMonitor.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/BackgroundWorkers/HeartbeatMonitor.cs) บรรทัดที่ 107-132 และ 158-171 เมื่อตรวจพบว่าไรเดอร์ขาดการส่งสัญญาณชีพ (Heartbeat Timeout) ระบบจะเปลี่ยนสถานะไรเดอร์เป็น `RiderState.STALE` และหลังจากนั้นเป็น `RiderState.OFFLINE` โดยเรียกใช้ `stateMachine.TransitionRiderAsync`
- ทว่า `HeartbeatMonitor` หรือ `StateMachineService` ไม่ได้ทำการบรอดแคสต์สถานะใหม่นี้หา Admin Dashboard ผ่าน SignalR event `'RiderStatusUpdated'`
- ส่งผลให้ผู้ใช้บน Admin Dashboard ยังคงมองเห็นพิกัดและหมุดของไรเดอร์นั้นออนไลน์ค้างอยู่เป็นสถานะ `IDLE` หรือ `RESERVED` บนแผนที่และรายการ จนกว่าจะมีการโหลดหน้าเว็บใหม่หรือไรเดอร์หลุดในแบบ WebSocket Disconnect โดยตรง

---

## ❌ BUG-19 — Pending: Indeterminate Order Cancellation for Multi-Drop Riders on Admin Map

### ปัญหา
- บนหน้าจอ Admin Dashboard ในไฟล์ [map.component.ts](file:///c:/Users/ASUS/Desktop/Project/Delivery/admin-dashboard/src/app/features/map/map.component.ts) บรรทัดที่ 723 เมื่อผู้ดูแลระบบคลิกยกเลิกออร์เดอร์ของไรเดอร์บนหมุดแผนที่ เมธอด `cancelRiderOrder(riderId)` จะเรียกใช้ลอจิก:
  `const activeOrder = this.activeOrders.find(o => o.assignedRiderId === riderId);`
  ซึ่งจะสุ่มหยิบออเดอร์ตัวแรกที่เจอเท่านั้นมายกเลิก
- สำหรับระบบ Multi-drop / Batch delivery ที่ไรเดอร์สามารถถือออเดอร์พ่วงได้พร้อมกันสูงสุด 3 ออเดอร์ การยกเลิกแบบนี้จะทำให้ออเดอร์ที่เหลือในกลุ่มเดียวกันยังคงค้างส่งอยู่กับไรเดอร์ และผู้ดูแลระบบไม่มีตัวเลือกในการยกเลิกทั้งกลุ่มออเดอร์พ่วงหรือเจาะจงเลือกยกเลิกออเดอร์ที่ต้องการ
