---
scope: Runtime Rules & Coding Constraints (All Components)
source_of_truth:
  - .cursorrules
  - AGENTS.md
  - AI-BOOTSTRAP.md
related_contexts:
  - .docs/ai-context/spec-backend.md
  - .docs/ai-context/spec-frontend.md
  - .docs/ai-context/spec-mobile-rider.md
forbidden_patterns:
  - อัปเดต AI-CHANGELOG.md โดยอัตโนมัติ (ต้องถามผู้ใช้ก่อน)
  - ลบ entries เก่าใน AI-CHANGELOG.md
  - แก้ไขไฟล์ที่ไม่เกี่ยวกับ task ปัจจุบัน
  - เพิ่ม Kubernetes, payment gateway, cloud deployment ในเฟสปัจจุบัน
known_pitfalls:
  - npm-related work: ต้องตรวจ .npmrc และ VPN/private registry ก่อน
  - GPU-dependent implementation: dev machine เป็น ASUS ROG (heat/driver issues)
---

# runtime-rules.md — Runtime Rules & Coding Constraints

> **Source**: `.cursorrules` + `AGENTS.md`

---

## 1. Mandatory Pre-Task Protocol

```
อ่านก่อนทุกงาน:
  1. AI-INDEX.md       → route ไปยัง spec ที่ถูกต้อง
  2. AI-BOOTSTRAP.md   → behavior constraints
  3. เฉพาะ spec ที่เกี่ยวข้อง (ไม่โหลดทั้ง PROJECT-SPEC.md)
  4. AI-CHANGELOG.md ส่วนล่าสุดเท่านั้น
```

---

## 2. Backend Runtime Rules

| Rule | Detail |
|---|---|
| Base Controller | Master Data → `CrudControllerBase` / Business → `DeliveryControllerBase` |
| Database Access | `DBHandlerCore` เท่านั้น — ห้าม inject `DbContext` ตรงใน controller |
| Service Location | `BackendApi/Services/` เท่านั้น — ห้าม `Controllers/Services/` |
| Response Format | ทุก endpoint ต้อง return `ApiResponse<T>` |
| Object Mapping | `Mapster` เท่านั้น ผ่าน `MappingConfig.cs` |
| Spatial Type | `geometry(Point, 4326)` + `NetTopologySuite` |
| GPS Calculation | ใช้ PostGIS `.Distance()` — ห้ามใช้ Haversine C# ใน backend |
| CORS | ต้องอยู่บนสุดของ Middleware pipeline |
| JWT for WebSocket | ส่งผ่าน `?access_token=` query string |

---

## 3. Frontend Runtime Rules

| Rule | Detail |
|---|---|
| Component Style | Standalone Components เท่านั้น |
| CRUD Services | `BaseApiService<T>` (inherit) |
| Custom HTTP | `DeliveryHttpRequest` (Fluent API) |
| Models | OpenAPI generated จาก `src/app/api/generated/` เท่านั้น |
| GPS Payload | ต้องมี fallback mapper (lat/Lat/latitude) |
| RxJS | ห้าม nested subscribe — ใช้ takeUntilDestroyed |

---

## 4. Mobile Runtime Rules

| Rule | Detail |
|---|---|
| HTTP Client | Dio เท่านั้น (ห้าม http.dart) |
| State | Riverpod providers เท่านั้น |
| Navigation | GoRouter เท่านั้น |
| Token Storage | `flutter_secure_storage` |
| GPS Noise Filter | accuracy > 50m → ทิ้งทันที |
| Background GPS Android | Foreground Service + notification required |
| Concurrent Refresh | `_isRefreshing` flag ป้องกัน race condition |

---

## 5. AI Engine Runtime Rules

| Rule | Detail |
|---|---|
| Solver | Google OR-Tools `PATH_CHEAPEST_ARC` เท่านั้น |
| Distance Matrix | Haversine ใน `geo_utils.py` เท่านั้น |
| GPU | ห้ามเพิ่ม GPU dependency (dev machine: ASUS ROG) |
| Endpoints | ห้าม break `/api/optimize-route` หรือ `/api/v1/dispatch/rank` |

---

## 6. Redis Rules (Critical)

```
Redis = Operational Realtime State เท่านั้น
  ✅ GPS buffer (short-lived)
  ✅ Rider presence (heartbeat)
  ✅ Distributed locks (SETNX)
  ✅ Route cache (TTL 24h)
  ✅ Dispatch offer locks (TTL 30s)

Redis ≠ Source of Truth
  ❌ ห้ามอ่าน final order/rider status จาก Redis
  ❌ ห้ามเขียน audit trail ลง Redis
  ❌ ห้ามใช้ Redis สำหรับ pagination หรือ search
```

---

## 7. Logging & Changelog Rules

- **ห้ามเขียน AI-CHANGELOG.md โดยอัตโนมัติ** — ถามผู้ใช้ก่อนทุกครั้ง
- **AI-CHANGELOG.md เป็น append-only** — ห้ามแก้ไข entries เก่า
- **เพิ่มเสมอที่ท้ายไฟล์** — ห้ามแทรกระหว่างกลาง

---

## 8. Scope Control Rules

- เปลี่ยนเฉพาะสิ่งที่ถูกขอ — ห้าม refactor ไฟล์อื่น
- ห้าม revert งานของผู้ใช้
- ห้ามเพิ่ม dependency ใหม่โดยไม่ตรวจ `.npmrc` / registry

---

## 9. Environment Constraints

| Constraint | Detail |
|---|---|
| Dev Machine | ASUS ROG — หลีกเลี่ยง GPU-intensive ops |
| Docker RAM | >= 4GB (แนะนำ 8GB), WSL 2 Backend |
| .NET | 8 เท่านั้น — ห้ามใช้ .NET 9 |
| Ports ใช้งาน | 80, 5000, 5432, 6379, 8000, 5001, 8080, 8081, 8082, 9090, 3000 |
| Secrets | ห้าม commit ลง git — ใช้ `.env` / user secrets |
| OSRM Data | ต้องสร้าง `osrm_data/` ก่อนรัน osrm container |

---

## 10. Current Scope Restrictions (Phase 6)

> [!CAUTION]
> ห้ามแตก scope ในเฟสปัจจุบัน:
> - Kubernetes
> - Real ML training / GPU ML
> - Multi-region deployment
> - Payment gateway
> - Cloud deployment จริง
> - Native production mobile polish (ยังเป็น prototype)
