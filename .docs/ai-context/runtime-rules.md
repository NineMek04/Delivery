# Runtime Rules & Coding Constraints

**Version:** 1.0.0 | **Updated:** 2026-06-14

## 1. Mandatory Context

อ่าน `AI-INDEX.md` -> `AI-BOOTSTRAP.md` -> spec/contract ที่เกี่ยวข้อง
และอ่าน changelog เฉพาะวันที่จำเป็น ห้ามแก้ changelog โดยอัตโนมัติ

## 2. Backend Rules

- Controller/Hub ต้องบาง: validate, authorize, map transport และส่งต่อ service
- ห้าม inject/use `ApplicationDbContext` โดยตรงใน Controller หรือ Hub
- `DBHandlerCore` ใช้กับ CRUD/query ทั่วไป; service, worker และ infrastructure
  ใช้ scoped `ApplicationDbContext` ได้เมื่อ transaction, spatial query, batching
  หรือ concurrency ต้องใช้ EF โดยตรง
- Business logic อยู่ใน `BackendApi/Services/`, `BackendApi/Features/` หรือ
  infrastructure service ที่มี ownership ชัดเจน ห้ามสร้าง service ใต้ Controllers
- REST application endpoints ต้องคืน `ApiResponse`/`ApiResponse<T>` ซึ่งมี
  `status`, `success`, `message`, `code`, `errors` และ `value` เมื่อมีข้อมูล
- ยกเว้น wrapper ได้เฉพาะ health, metrics, SignalR, file/stream หรือ endpoint
  infrastructure ที่มี `[DisableWrapper]` และมีเหตุผลชัดเจน
- ใช้ Mapster สำหรับ mapping ปกติ; manual mapping อนุญาตสำหรับ spatial,
  security-sensitive, partial update หรือ snapshot ที่ต้องควบคุม field ชัดเจน
- Persisted spatial filtering ใช้ PostGIS/GiST. Haversine ใช้ได้เฉพาะ
  in-memory heuristic/fallback ห้ามแทน indexed database query
- `TrackingHub` เป็น Pure Transport Layer
- RabbitMQ consumer ต้อง idempotent ผ่าน `ProcessedEvents`
- Logs ต้องมี `CorrelationId`, `OrderId`, `RiderId` เมื่อมีค่า

## 3. Database Evolution

- EF schema ปัจจุบันมี consolidated baseline หนึ่ง migration พร้อม Designer และ Snapshot
- PostgreSQL-specific DDL ที่ EF แสดงไม่ได้ดี เช่น partition/index maintenance
  ให้อยู่ใน service migration ที่ idempotent ไม่สร้าง migration ย่อยจำนวนมาก
- ห้ามลบ/ยุบ migration ที่ถูกใช้แล้วโดยไม่ทำ compatibility bridge และตรวจ
  `__EFMigrationsHistory`
- Index `ProcessedEvents.ProcessedAt`, spatial GiST และ dispatch indexes เป็นข้อบังคับ
- Concurrency ใช้ PostgreSQL shadow `xmin` แบบ `IsRowVersion()`; public
  `RowVersion` คงไว้เพื่อ API compatibility เท่านั้น

## 4. Angular Rules

- Standalone components; routes lazy-load ตาม `app.routes.ts`
- CRUD ใช้ `BaseApiService<T>` และ custom calls ใช้ `DeliveryHttpRequest`
- API methods คืน RxJS `Observable`; ห้ามเขียนตัวอย่างเป็น Promise โดยไม่มี conversion
- ใช้ generated OpenAPI model เมื่อ schema มีอยู่; local view model อนุญาตเมื่อ
  ไม่ซ้ำ contract ที่ generate แล้ว
- ห้าม nested subscribe; ใช้ operator composition และ deterministic teardown
  (`takeUntilDestroyed` หรือ aggregate subscription ที่ unsubscribe)
- Leaflet map ที่มี marker/path จำนวนมากต้องตั้ง `preferCanvas: true`
- ห้าม raw string interpolation ของข้อมูลภายนอกลง popup/DOM และห้าม inline `onclick`
- HTTP dashboard ใช้ HttpOnly cookies + `withCredentials` + XSRF header;
  ห้ามเก็บ access/refresh token ใน localStorage
- **Reactive UI & Memory Leak Prevention:** เมื่อมีการเปลี่ยนแปลงสถานะหรือมีพิกัด/ข้อมูลใหม่เพิ่มเข้ามาในแผนที่และ UI ระบบต้องทำการอัปเดตและเรนเดอร์เฉพาะจุดที่เปลี่ยนแปลงโดยอัตโนมัติแบบเรียลไทม์ (ไม่ต้องกดรีเฟรชหน้าจอเอง) และต้องป้องกัน Memory Leak โดยเคร่งครัด ผ่านการทำ Unsubscribe, Teardown สำหรับ SignalR, Leaflet layers และ event listeners ทุกครั้งเมื่อ component ถูกทำลาย

## 5. Flutter Rules

- GPS accuracy `<= 50m` may enter the Core pipeline. Accuracy `> 50m` and
  `<= 300m` is degraded Admin UI telemetry only and must not enter
  dispatch/history. Accuracy `> 300m` must be rejected.

- HTTP ใช้ Dio, state/server data ใช้ `flutter_riverpod`, navigation ใช้ GoRouter
- Token native เก็บใน `flutter_secure_storage`; web fallback ใช้ sessionStorage
- `setState` ใช้ได้เฉพาะ transient widget state เช่น animation/form/countdown;
  domain/server state ต้องอยู่ใน provider/notifier
- 401 refresh ต้อง single-flight และห้าม retry auth endpoint วนซ้ำ
- GPS/status mutation ต้อง buffer ใน SQLite เมื่อ offline และ replay ตามลำดับ
- ส่ง GPS หลัง SignalR connected เท่านั้น; fallback batch ใช้ telemetry REST endpoint

## 6. Route Optimizer Rules

- คง OR-Tools และ `PATH_CHEAPEST_ARC`; solver มี time limit
- Distance matrix/heuristic fallback ใช้ `haversine_distance` ใน `geo_utils.py`
- CPU-bound FastAPI endpoint ต้องเป็น synchronous `def`
- ห้าม break `/api/optimize-route`, `/api/v1/dispatch/rank`,
  `/api/v1/predict-eta`, `/health`
- ห้ามเพิ่ม GPU dependency หรือ external routing API

## 7. Redis Rules

Redis เป็น operational state/cache เท่านั้น ไม่ใช่ source of truth:
presence, GEO, locks, short-lived recipient cache, route cache ใช้ได้ แต่ final
order/rider state, audit, pagination และ search ต้องอาศัย PostgreSQL

## 8. Tests And Logs

- Test hub: `RootScripts/scripts.test/test/`
- Angular `*.spec.ts` วางข้าง component ได้ตาม Angular CLI
- ห้ามสร้าง `tests/` หรือ `__tests__/` ใน core component directories
- Load/stress logs เก็บใน `LogsTest/YYYY-MM-DD/` ตามชื่อมาตรฐานใน `AGENTS.md`
- Integration tests ต้อง hermetic; ใช้ Testcontainers สำหรับ PostgreSQL/Redis/RabbitMQ

## 9. Scope

- แก้เฉพาะ task และห้าม revert งานผู้ใช้
- .NET 8 เท่านั้น
- Secrets ใช้ `.env`, user secrets หรือ Vault และห้าม commit
- Forbidden stack ทั้งหมดให้ยึด `AGENTS.md`
