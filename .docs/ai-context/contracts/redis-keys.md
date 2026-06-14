# Redis Key Contracts

Redis เป็น operational cache เท่านั้น; PostgreSQL เป็น source of truth.

| Key | Type | Typical TTL | Purpose |
|---|---|---:|---|
| `riders:locations` | GEO ZSET | none | latest rider coordinates |
| `riders:heartbeat:{riderId}` | Hash | sliding | presence/heartbeat |
| `riders:gps:{riderId}` | Hash | short-lived | latest GPS data |
| `riders:speed_buffer:{riderId}` | List | 5 min | moving speed samples |
| `riders:status:{riderId}` | String | 24 h | operational state cache |
| `riders:active_order:{riderId}` | Hash | 30 s | order/customer recipients |
| `riders:snapped_gps:{riderId}` | implementation cache | short-lived | road-snapped point |
| `riders:hotspots:heatmap` | cache | bounded | admin heatmap |
| `dispatch:lock:rider:{riderId}` | String | 30 s | rider offer reservation |
| `dispatch:lock:{orderId}` | String | short | prevent double dispatch |
| `dispatch:inject_lock:rider:{riderId}` | String | short | batch injection lock |
| `lock:offer:{offerId}` | String | short | accept/reject concurrency |
| `route:cache:{lat1:F5}:{lng1:F5}:{lat2:F5}:{lng2:F5}` | String | 24 h | OSRM result cache |

## Rules

- GEO member คือ rider id และ coordinate order ใน command คือ lng, lat
- key ไม่มี/ถูก evict ต้อง fallback PostgreSQL เมื่อ correctness ต้องการ
- active order hash schema marker ต้องตรวจ; unknown schema ต้อง rebuild จาก DB
- lock release ต้องตรวจ ownership/value ก่อน delete
- ห้ามใช้ alias เก่า `presence:rider:*`, `geo:riders`, `gps:last:*`,
  `offer:{orderId}` ในโค้ดใหม่
- GPS durable queue อยู่ใน Flutter SQLite/RabbitMQ/PostgreSQL pipeline
  ไม่ใช่ in-memory `GpsSyncBuffer` รุ่นเก่า
- เพิ่ม key ใหม่ต้องเพิ่ม contract พร้อม type, owner, reader, TTL และ DB fallback
