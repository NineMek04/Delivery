---
scope: Order & Rider State Machines
source_of_truth:
  - AI-CHANGELOG.md (2026-05-14 StateMachineService, 2026-05-18 Order states frontend fix)
  - BackendApi/Services/Dispatch/StateMachineService.cs (codebase)
  - BackendApi/Models/Order.cs, Rider.cs (codebase)
related_contexts:
  - .docs/ai-context/contracts/signalr-contracts.md
  - .docs/ai-context/contracts/redis-keys.md
  - .docs/ai-context/spec-backend.md
forbidden_patterns:
  - ใช้ state names ที่ไม่อยู่ในรายการนี้ (เช่น PENDING, DELIVERED)
  - ข้าม state transition (เช่น CREATED → ASSIGNED โดยไม่ผ่าน MATCHING)
  - Allow Rider เปลี่ยน state เองโดยไม่ผ่าน StateMachineService
known_pitfalls:
  - Frontend เคยใช้ PENDING/DELIVERED ซึ่งผิด — ต้องใช้ตามรายการนี้เท่านั้น
  - DispatchTimeoutWorker ต้องรัน check ทุก interval ที่ตรงกับ Redis offer TTL 30s
  - OFFERED state ต้องมี version number สำหรับป้องกัน double-accept
---

# state-machine.md — Order & Rider State Machines

> **Implementation:** `BackendApi/Services/Dispatch/StateMachineService.cs`  
> **For event names when transitioning** → `contracts/signalr-contracts.md`  
> **For Redis locks during transitions** → `contracts/redis-keys.md`

---

## 1. Order State Machine

### States

```
CREATED      → Order ถูกสร้างใหม่ รอ Dispatch
MATCHING     → กำลังค้นหา Rider ที่เหมาะสม (AI ranking)
OFFERING     → ส่ง Offer ไปยัง Rider แล้ว รอคำตอบ (TTL 30s)
ASSIGNED     → Rider รับงานแล้ว กำลังไปรับของ
PICKING_UP   → Rider กำลังเดินทางไปร้าน
DELIVERING   → Rider รับของแล้ว กำลังไปส่ง
COMPLETED    → ส่งสำเร็จ (terminal state)
CANCELLED    → ยกเลิก (terminal state)
```

### Transition Diagram

```
CREATED
  │
  ▼ (Dispatch trigger)
MATCHING
  │
  ▼ (Rider found, Offer sent)
OFFERING ──(30s timeout, no response)──► MATCHING (re-dispatch)
  │
  ▼ (Rider accepts)
ASSIGNED
  │
  ▼ (Rider UpdateStatus → PICKING_UP)
PICKING_UP
  │
  ▼ (Rider UpdateStatus → DELIVERING)
DELIVERING
  │
  ▼ (Rider UpdateStatus → COMPLETED)
COMPLETED ← terminal

Any state ──(Admin cancel)──► CANCELLED ← terminal
```

### Valid Transitions (StateMachineService rules)

| From | To | Trigger | Actor |
|---|---|---|---|
| `CREATED` | `MATCHING` | `POST /api/v1/orders/{id}/dispatch` | Admin/System |
| `MATCHING` | `OFFERING` | Dispatch finds Rider | System (DispatchService) |
| `OFFERING` | `ASSIGNED` | Rider accepts offer | Rider (SignalR AcceptOffer) |
| `OFFERING` | `MATCHING` | 30s timeout | System (DispatchTimeoutWorker) |
| `ASSIGNED` | `PICKING_UP` | Rider status update | Rider (PATCH /orders/{id}/status) |
| `PICKING_UP` | `DELIVERING` | Rider status update | Rider (PATCH /orders/{id}/status) |
| `DELIVERING` | `COMPLETED` | Rider status update | Rider (PATCH /orders/{id}/status) |
| Any | `CANCELLED` | Admin cancel | Admin (POST /orders/{id}/cancel) |

---

## 2. Rider State Machine

### States

```
OFFLINE      → ไม่ได้เชื่อมต่อ (SignalR disconnected)
IDLE         → Online พร้อมรับงาน
OFFERED      → กำลังพิจารณา Offer (TTL 30s)
ASSIGNED     → รับงานแล้ว กำลังไปรับของ
PICKING_UP   → เดินทางไปร้าน
DELIVERING   → กำลังส่งของ
```

### Transition Diagram

```
OFFLINE ◄──────────────────────────────────────────────────────┐
  │                                                             │
  ▼ (SignalR connect)                                          │
IDLE ◄──────────────────────────────────────────────────── DELIVERING
  │                          (Order COMPLETED)                  │
  ▼ (Offer received)                                           │
OFFERED ──(30s timeout / Reject)──► IDLE                      │
  │                                                            │
  ▼ (Accept offer)                                            │
ASSIGNED                                                       │
  │                                                            │
  ▼ (UpdateStatus PICKING_UP)                                  │
PICKING_UP                                                     │
  │                                                            │
  ▼ (UpdateStatus DELIVERING)                                  │
DELIVERING ─────────────────────────────────────────────────────┘
              (UpdateStatus COMPLETED → auto-release → IDLE)
```

### Auto-release Rule
เมื่อ Order เป็น `COMPLETED`: Rider กลับไป `IDLE` อัตโนมัติ  
เมื่อ Order เป็น `CANCELLED` ระหว่าง `ASSIGNED/PICKING_UP/DELIVERING`: Rider กลับไป `IDLE`

---

## 3. Dispatch Offer Lifecycle (30-Second Rule)

```
T+0s:  DispatchService ส่ง Offer → Rider group
       Redis: SET offer:{orderId} = {riderId, version} EX 30
       Redis: SET presence:rider:{riderId} = "OFFERED" EX 30
       Order State: OFFERING

T+0-30s: รอ Rider ตอบรับ
  ├─ Accept → Order: ASSIGNED, Rider: ASSIGNED, Redis lock released
  └─ Reject → ค้นหา Rider คนถัดไปทันที

T+30s: DispatchTimeoutWorker ตรวจสอบ
  ├─ ถ้า Order ยังเป็น OFFERING → re-dispatch
  └─ ถ้า Order เปลี่ยนแล้ว → skip
```

**Offer Version:** ตัวเลข monotonic increment ป้องกัน race condition double-accept  
**Redis TTL:** `offer:{orderId}` = 30 วินาที (ต้องตรงกับ DispatchTimeoutWorker interval)

---

## 4. HeartbeatMonitor — Ghost Rider Detection

```
ทุก N วินาที:
  foreach IDLE/OFFERED Rider:
    if Redis presence:rider:{id} หมดอายุ (ไม่ได้ส่ง GPS):
      → Rider State = OFFLINE
      → ลบออกจาก dispatch pool
      → Broadcast Rider offline ไปยัง "admins"
```

---

## 5. State Values (Enum — ต้องใช้ตามนี้เท่านั้น)

### OrderState (C# Enum)
```csharp
public enum OrderState
{
    CREATED,
    MATCHING,
    OFFERING,
    ASSIGNED,
    PICKING_UP,
    DELIVERING,
    COMPLETED,
    CANCELLED
}
```

### RiderState (C# Enum)
```csharp
public enum RiderState
{
    OFFLINE,
    IDLE,
    OFFERED,
    ASSIGNED,
    PICKING_UP,
    DELIVERING
}
```

> ⚠️ **ห้ามใช้:** `PENDING`, `DELIVERED`, `BUSY`, `ACTIVE` — ไม่มีใน codebase
