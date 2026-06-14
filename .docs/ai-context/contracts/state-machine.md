# Order And Rider State Machines

**Implementation:**
`BackendApi/Core/StateMachines/OrderState.cs`,
`BackendApi/Core/StateMachines/RiderState.cs`,
`BackendApi/Features/DispatchManagement/StateMachineService.cs`

## 1. Order States

`CREATED`, `MATCHING`, `OFFERING`, `ASSIGNED`, `PICKING_UP`, `DELIVERING`,
`COMPLETED`, `CANCELLED`

### Valid Transitions

```text
CREATED -> CREATED | MATCHING | CANCELLED
MATCHING -> CREATED | OFFERING | CANCELLED
OFFERING -> ASSIGNED | MATCHING | CANCELLED
ASSIGNED -> PICKING_UP | CANCELLED
PICKING_UP -> DELIVERING | CANCELLED
DELIVERING -> COMPLETED | CANCELLED
```

`CREATED -> CREATED` เป็น idempotent initialization path.
`MATCHING -> CREATED` ใช้ reset/retry dispatch.
Terminal states ไม่มี outgoing transition.

Semantics:

- `ASSIGNED`: Rider รับงานแล้ว
- `PICKING_UP`: Rider กำลังเดินทางไปรับของ
- `DELIVERING`: รับของแล้วและกำลังส่ง

## 2. Rider States

`OFFLINE`, `IDLE`, `RESERVED`, `BUSY`, `STALE`

```text
OFFLINE -> IDLE
IDLE -> RESERVED | OFFLINE | STALE
RESERVED -> BUSY | IDLE | STALE
BUSY -> IDLE | OFFLINE | STALE
STALE -> IDLE | BUSY | OFFLINE
```

`Recover` เป็น transition reason ไม่ใช่ RiderState. Handler ต้อง resolve เป็น
`IDLE` หรือ `BUSY` จาก active orders ใน PostgreSQL.

## 3. Offer Lifecycle

- Dispatch lock rider TTL ปกติ 30 วินาที
- Order เปลี่ยน `MATCHING -> OFFERING` เมื่อ offer ถูก persist/send
- Accept ต้องตรวจ offer id/version และทำ `OFFERING -> ASSIGNED`,
  `RESERVED -> BUSY` แบบ concurrency-safe
- Reject/timeout ทำ `OFFERING -> MATCHING`, `RESERVED -> IDLE`
- Worker ต้องตรวจ PostgreSQL state ก่อน re-dispatch; Redis ไม่ใช่ authority

## 4. Mandatory Rules

- ทุก transition ผ่าน `StateMachineService`/domain rules
- ห้ามใช้ `PENDING`, `DELIVERED`, `OFFERED`
- ห้ามใช้ Order state เป็น Rider state
- เพิ่ม transition ได้เมื่อมี test/contract update; ห้ามลบ transition เดิมตาม
  critical protection rule
- status broadcast เกิดหลัง persist สำเร็จ
