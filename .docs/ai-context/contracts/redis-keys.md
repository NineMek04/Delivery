---
scope: Redis Key Schemas, TTLs, and Data Types
source_of_truth:
  - AI-CHANGELOG.md (2026-05-14 Redis Integration, Phase 2 Dispatch)
  - BackendApi/Infrastructure/Redis/ (codebase)
  - BackendApi/Services/Dispatch/DispatchService.cs (codebase)
related_contexts:
  - .docs/ai-context/contracts/state-machine.md
  - .docs/ai-context/spec-backend.md
forbidden_patterns:
  - ให้ Redis เป็น source of truth (ใช้เป็น operational cache เท่านั้น)
  - เพิ่ม Redis key ใหม่โดยไม่ document ที่นี่ก่อน
  - ตั้ง TTL ที่ไม่สอดคล้องกับ Business logic (เช่น offer TTL ต้อง = 30s)
known_pitfalls:
  - maxmemory 256mb + allkeys-lru: keys อาจถูก evict → ต้องมี fallback ไปยัง PostgreSQL
  - offer TTL ต้องตรงกับ DispatchTimeoutWorker check interval
  - presence keys expire → HeartbeatMonitor detect และ mark OFFLINE
---

# redis-keys.md — Redis Key Schemas, TTLs & Data Types

> **Redis Config:** `redis:7-alpine`, maxmemory 256mb, policy allkeys-lru  
> **Critical Rule:** Redis = cache layer เท่านั้น — PostgreSQL = source of truth

---

## 1. Rider Presence Keys

### `riders:heartbeat:{riderId}`

| Field | Value |
|---|---|
| **Key Pattern** | `riders:heartbeat:{riderId}` (UUID) |
| **Data Type** | Hash |
| **Value** | heartbeat timestamp; durable RiderState remains in PostgreSQL |
| **TTL** | short sliding TTL, renewed by heartbeat |
| **SET By** | `TrackingHub.UpdateHeartbeat()` / presence manager |
| **READ By** | `RiderPresenceService`, `DispatchService` |

```
เมื่อ TTL expire → HeartbeatMonitor detect → Rider State = OFFLINE
```

---

### `riders:locations` (Geospatial Sorted Set)

| Field | Value |
|---|---|
| **Key** | `riders:locations` |
| **Data Type** | Sorted Set (ZSET) — Redis GEO |
| **Members** | `riderId` (UUID string) |
| **Score** | Geospatial coordinates (managed by Redis GEO commands) |
| **TTL** | No TTL (managed by ZREM on disconnect) |
| **Commands** | `GEOADD`, `GEORADIUS`, `GEODIST`, `GEOPOS` |

```csharp
// เพิ่ม Rider location
await _redis.GeoAddAsync("riders:locations", longitude, latitude, riderId);

// หา Riders ใกล้ Shop ใน radius 5km
var nearbyRiders = await _redis.GeoRadiusAsync("riders:locations",
  shopLng, shopLat, 5, GeoUnit.Kilometers);
```

---

## 2. GPS Buffer Keys

### `riders:gps:{riderId}`

| Field | Value |
|---|---|
| **Key Pattern** | `riders:gps:{riderId}` (UUID) |
| **Data Type** | Hash |
| **Fields** | `lat`, `lng`, `updated_at`, `speed_kmh` |
| **TTL** | 24 hours |
| **SET By** | `GpsSyncBuffer` on each GPS update |
| **READ By** | `DispatchService` (for most-recent rider location) |

```
ใช้สำหรับ: AI scoring ต้องการตำแหน่ง Rider ล่าสุด (ก่อน flush ลง PostGIS)
```

### `riders:speed_buffer:{riderId}`

| Field | Value |
|---|---|
| **Key Pattern** | `riders:speed_buffer:{riderId}` (UUID) |
| **Data Type** | List |
| **Values** | ค่าความเร็ว km/h ล่าสุด 5 จุด (5-point Moving Average) |
| **TTL** | 5 minutes |
| **SET By** | `RiderPresenceService.UpdateGpsAsync()` (RPUSH + LTRIM) |
| **READ By** | `RiderPresenceService.GetRiderSpeedAsync()` → `DispatchCandidateRanker` |

```
ใช้สำหรับ: คำนวณ Rider velocity เฉลี่ย 5 จุดล่าสุด → ส่งไป AI Engine สำหรับ ETA prediction
```

---

## 3. Dispatch Offer Keys

### `dispatch:lock:rider:{riderId}`

| Field | Value |
|---|---|
| **Key Pattern** | `dispatch:lock:rider:{riderId}` |
| **Data Type** | String |
| **Value** | current `offerId` |
| **TTL** | **30 seconds** (ต้องตรงกับ DispatchTimeoutWorker) |
| **SET By** | `DispatchService` เมื่อส่ง Offer |
| **DELETE By** | Rider accept / reject / timeout |

```csharp
// Set offer lock (SETNX pattern)
var key = $"dispatch:lock:rider:{riderId}";
await _redis.StringSetAsync(key, offerId, TimeSpan.FromSeconds(30), When.NotExists);
```

---

### `dispatch:lock:{orderId}`

| Field | Value |
|---|---|
| **Key Pattern** | `dispatch:lock:{orderId}` |
| **Data Type** | String |
| **Value** | `"1"` (presence = locked) |
| **TTL** | ~5 seconds (prevent concurrent dispatch) |
| **SET By** | `RedisLockService` (SETNX + Lua Script) |
| **Purpose** | Prevent double-dispatch race condition |

---

## 4. Route Cache Keys

### `route:{lat1}:{lng1}:{lat2}:{lng2}`

| Field | Value |
|---|---|
| **Key Pattern** | `route:{lat1_4dp}:{lng1_4dp}:{lat2_4dp}:{lng2_4dp}` |
| **Data Type** | String |
| **Value** | `{ "encodedPolyline": "...", "distanceKm": 5.2, "durationSeconds": 720 }` |
| **TTL** | **24 hours** |
| **SET By** | `OsrmRoutingService` after successful OSRM call |
| **READ By** | `OsrmRoutingService` (cache-first) |

```
ลด latency: Local OSRM call ~50ms, Redis cache hit ~1ms
```

---

## 5. GPS Sync Buffer

**Not a Redis key** — `GpsSyncBuffer` เป็น in-memory `ConcurrentDictionary<string, List<GpsPoint>>`

```
GpsSyncBuffer (in-memory)
  ← TrackingHub.UpdateLocation() เพิ่ม GPS points
  
GpsSyncWorker (runs every 30s)
  → Bulk flush ลง RiderLocationHistories (PostGIS)
  → Clear buffer entries ที่ flush แล้ว
```

---

## 6. Session / Auth Keys (ถ้ามี)

| Key | ใช้เมื่อ |
|---|---|
| `auth:blacklist:{jti}` | Revoked JWT token IDs (optional) |

---

## 7. Redis Data Type Summary

| Key Pattern | Redis Type | TTL |
|---|---|---|
| `presence:rider:{id}` | String | ~60s (sliding) |
| `geo:riders` | Sorted Set (GEO) | No TTL |
| `gps:last:{id}` | Hash | 5 min |
| `riders:speed_buffer:{id}` | List | 5 min |
| `offer:{orderId}` | String (JSON) | **30s** |
| `dispatch:lock:{orderId}` | String | ~5s |
| `route:{coords}` | String (JSON) | 24h |

---

## 8. Critical Rules

```
✅ Redis ใช้สำหรับ:
   - Operational state (presence, GPS buffer)
   - Distributed locking (race condition prevention)
   - Short-lived offer locks (30s business rule)
   - Route caching (performance)

❌ Redis ห้ามใช้สำหรับ:
   - Final order status (ต้องอ่านจาก PostgreSQL)
   - Audit trail (ต้องเก็บใน PostgreSQL)
   - Pagination / search
   - Long-term data storage
```
