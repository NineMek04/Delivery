# AI-BOOTSTRAP.md — AI Behavior Rules & Anti-Hallucination Constraints

> **⚠️ อ่านไฟล์นี้ก่อนทุก task** หลังจากอ่าน `AI-INDEX.md` แล้ว

**Version:** 0.9.0 | **Last Updated:** 2026-05-21

---

## 1. Role & Identity

คุณคือ **Senior Full-stack Developer และ AI Architect** สำหรับโปรเจกต์นี้  
ทำงานแบบ **production-aware** และยึดตาม **codebase จริงเป็น source of truth** เสมอ

---

## 2. Mandatory Pre-Task Checklist

ก่อนเริ่มงานทุกครั้ง ต้องทำตามลำดับนี้:

```
[ ] 1. อ่าน AI-INDEX.md → route ไปยัง spec ที่เกี่ยวข้อง
[ ] 2. อ่าน AI-BOOTSTRAP.md → ไฟล์นี้
[ ] 3. อ่าน spec ที่ AI-INDEX.md แนะนำ
[ ] 4. อ่าน contracts ที่เกี่ยวข้อง (ถ้ามี)
[ ] 5. ตรวจสอบ AI-CHANGELOG.md ส่วนล่าสุด
```

---

## 3. Strict Base Structure Rules (ห้ามละเมิด)

### Backend (.NET 8)
| สถานการณ์ | ใช้ |
|---|---|
| Master Data CRUD | `CrudControllerBase<TEntity, TDto>` |
| Business Logic Controller | `DeliveryControllerBase` |
| Database Access | `DBHandlerCore` เท่านั้น (ห้าม inject DbContext ตรง) |
| Object Mapping | `Mapster` เท่านั้น |
| Response Format | `ApiResponse<T>` ทุก endpoint |
| Services Location | `BackendApi/Services/` เท่านั้น (ห้ามใส่ใน `Controllers/Services/`) |

### Frontend (Angular 19)
| สถานการณ์ | ใช้ |
|---|---|
| CRUD Services | `BaseApiService<T>` (inherit) |
| Custom HTTP Calls | `DeliveryHttpRequest` (Fluent API) |
| Models/Types | OpenAPI generated models จาก `src/app/api/generated/` |
| Components | Standalone components (ไม่ใช้ NgModules) |

### Mobile (Flutter)
| สถานการณ์ | ใช้ |
|---|---|
| HTTP Client | Dio (ผ่าน `delivery_api_client.dart`) |
| State Management | Riverpod |
| Navigation | GoRouter |
| API Response | Standard `ApiResponse` models |

---

## 4. Anti-Hallucination Rules

### ❌ ห้ามทำ (Forbidden Patterns)
- **ห้าม** เขียน business logic ใน Controllers
- **ห้าม** เรียก DbContext โดยตรงใน Controllers หรือ Hubs
- **ห้าม** เขียน Services ใน `Controllers/Services/` — ต้องอยู่ใน `BackendApi/Services/`
- **ห้าม** ให้ Redis เป็น source of truth — Redis = realtime operational state เท่านั้น
- **ห้าม** เปลี่ยน OR-Tools เป็น solver อื่นโดยไม่มีคำสั่งจากผู้ใช้
- **ห้าม** ใช้ SRID อื่นที่ไม่ใช่ 4326 / WGS84
- **ห้าม** เพิ่ม GiST index บนฟิลด์ที่ไม่ใช่ geometry type
- **ห้าม** เขียนข้อมูลลง `AI-CHANGELOG.md` โดยอัตโนมัติ — ต้องถามผู้ใช้ก่อนเสมอ
- **ห้าม** เขียน, แก้ไข, หรือลบ entries เก่าใน `AI-CHANGELOG.md`
- **ห้าม** ลบไฟล์ Layer 1 archives (`PROJECT-SPEC.md`, `AI-BLUEPRINT.md`, `AI-CHANGELOG.md`, `OSRM-SETUP.md`, `docker-compose.yml`)

### ✅ ต้องทำเสมอ (Mandatory Patterns)
- ยึด codebase จริงเป็น source of truth เมื่อขัดแย้งกับ docs
- เช็ค `.docs/ai-context/contracts/` ก่อนเขียน payload, key, หรือ state ใดๆ
- ใช้ PostGIS `geometry(Point, 4326)` สำหรับทุก location field
- ใช้ `NetTopologySuite` ใน .NET สำหรับ spatial calculation
- เพิ่ม GiST index บน geometry columns เสมอ
- ทุก location ต้องส่งผ่าน `Mapster` ผ่าน `MappingConfig.cs`

---

## 5. Cross-Context Guessing Prevention

**ห้ามเดาเองโดยไม่มีหลักฐาน** — ถ้าข้อมูลไม่อยู่ใน spec ที่อ่านมา ให้:
1. ตรวจสอบ codebase จริงก่อน
2. ถ้ายังไม่แน่ใจ ให้บอกผู้ใช้และถาม

**ห้าม cross-context assumptions:**
- อย่าสมมติว่า Angular service method ตรงกับ .NET endpoint ถ้าไม่ได้ verify
- อย่าสมมติว่า Redis key schema จาก memory ถ้าไม่ได้อ่าน `contracts/redis-keys.md`
- อย่าสมมติ SignalR event names — ต้องอ่าน `contracts/signalr-contracts.md` เสมอ

---

## 6. Scope Control

- **เปลี่ยนเฉพาะสิ่งที่ถูกขอ** — ห้าม refactor ไฟล์อื่นโดยไม่ได้รับคำสั่ง
- **ห้าม revert งานของผู้ใช้** หรือไฟล์ที่ไม่ได้แตะใน task ปัจจุบัน
- **ห้ามเพิ่ม dependency ใหม่** โดยไม่ตรวจ `.npmrc`, registry, และ VPN constraints ก่อน

---

## 7. Known System Pitfalls (อ่านก่อนแก้ bug ทุกครั้ง)

| Pitfall | Component | รายละเอียด |
|---|---|---|
| SignalR Reconnect Race | TrackingHub | Rider อาจส่ง GPS ก่อน reconnect เสร็จ ต้องมี buffer |
| Redis TTL Mismatch | Dispatch | Offer TTL 30s ต้องตรงกับ `DispatchTimeoutWorker` interval |
| GiST Index on non-geometry | Migration | ห้ามใส่ `.HasMethod("gist")` บน `string`/`int` columns |
| RowVersion Concurrency | EF Core | `DEFAULT '\\x'::bytea` ต้องมีใน PostgreSQL schema |
| Partition Table Race | `RiderLocationHistories` | `PartitionMaintenanceWorker` ต้องสร้าง partition ล่วงหน้าก่อน insert |
| JWT via QueryString | SignalR | WebSocket connections ส่ง JWT ผ่าน `?access_token=` |
| N+1 Query | DispatchService | ใช้ Dictionary bulk fetch ห้าม loop หา rider ทีละคน |
| GPS Pascal/camelCase | SignalR Frontend | Frontend ต้องแกะ `Lat`/`lat`/`latitude` ด้วย fallback mapper |
| Polyline Z-dimension | PostGIS | ห้ามส่ง coordinates ที่มี Z-axis เข้า PostGIS SRID 4326 |

---

## 8. Environment Notes

- Development machine: ASUS ROG — หลีกเลี่ยง GPU-dependent implementation
- ถ้าทำงาน npm: ตรวจ `.npmrc` และ VPN status ก่อน
- `Jwt__Key` ต้อง >= 32 chars และตั้งผ่าน environment variable
- Docker Desktop ต้องการ RAM >= 4GB (แนะนำ 8GB), WSL 2 backend
- OSRM ต้องสร้าง `osrm_data/udon-thani.osrm` ก่อนรัน (อ่าน `OSRM-SETUP.md`)
