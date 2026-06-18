# AI-INDEX.md - Master Context Router

> อ่านไฟล์นี้ก่อนทุกงาน แล้วเปิดเฉพาะเอกสารที่ตรงกับขอบเขตงาน

**Version:** 1.0.0 | **Last Updated:** 2026-06-16

## 1. Required Reading Order

1. `AI-INDEX.md`
2. `AI-BOOTSTRAP.md`
3. Spec/contract ที่เกี่ยวข้องด้านล่าง
4. `.docs/AI-CHANGELOG/` เฉพาะวันที่เกี่ยวข้อง เมื่อจำเป็นต้องดูประวัติ

ห้ามโหลด historical archive ทั้งชุดโดยไม่มีเหตุผล และห้ามแก้ changelog อัตโนมัติ

## 2. Context Routing

| งาน | เอกสารหลัก | เนื้อหา |
|---|---|---|
| Architecture | [.docs/ai-context/spec-blueprint.md](.docs/ai-context/spec-blueprint.md) | topology, lifecycle, event boundaries |
| Backend .NET 8 | [.docs/ai-context/spec-backend.md](.docs/ai-context/spec-backend.md) | API, service boundaries, PostGIS, migrations |
| Angular admin | [.docs/ai-context/spec-frontend.md](.docs/ai-context/spec-frontend.md) | routes, HTTP, SignalR, Leaflet |
| AI engine | [.docs/ai-context/spec-ai-engine.md](.docs/ai-context/spec-ai-engine.md) | FastAPI, OR-Tools, scoring, fallbacks |
| Infrastructure | [.docs/ai-context/spec-infra-devops.md](.docs/ai-context/spec-infra-devops.md) | Compose, ports, telemetry, SLO |
| Flutter app | [.docs/ai-context/spec-mobile-rider.md](.docs/ai-context/spec-mobile-rider.md) | Rider, Customer, Store flows, offline queue |
| Coding rules | [.docs/ai-context/runtime-rules.md](.docs/ai-context/runtime-rules.md) | mandatory runtime constraints |
| Critical registry | [CRITICAL-CODE-PROTECTION.md](CRITICAL-CODE-PROTECTION.md) | protected files, endpoints, fallback paths |

## 3. Contracts

| Contract | ใช้เมื่อ |
|---|---|
| [.docs/ai-context/contracts/signalr-contracts.md](.docs/ai-context/contracts/signalr-contracts.md) | Hub methods, events, groups, payloads |
| [.docs/ai-context/contracts/state-machine.md](.docs/ai-context/contracts/state-machine.md) | Order/Rider states and transitions |
| [.docs/ai-context/contracts/api-contracts.md](.docs/ai-context/contracts/api-contracts.md) | REST endpoints, wrappers, DTO rules |
| [.docs/ai-context/contracts/redis-keys.md](.docs/ai-context/contracts/redis-keys.md) | Redis keys, types, TTL, fallback |
| [.docs/ai-context/contracts/geojson-contracts.md](.docs/ai-context/contracts/geojson-contracts.md) | SRID, coordinate order, polyline |

## 4. Historical Archives

`.docs/AI-CHANGELOG/`, older setup notes, and historical migration notes ใช้เพื่อ trace/recovery เท่านั้น เมื่อข้อมูลขัดกันให้ยึด:

1. กฎความปลอดภัยและข้อห้ามใน `AGENTS.md`
2. Contract ที่ active ใน index นี้
3. Implementation และ automated tests ปัจจุบัน
4. Historical archive

`Documents/development/PROJECT-SPEC.md` และ `Documents/development/AI-BLUEPRINT.md`
เป็น human-readable reference สำหรับส่งต่อทีม โดยสรุปจาก active specs และ codebase ปัจจุบัน
ส่วน `.docs/ai-context/` ยังเป็น canonical source สำหรับงาน implementation แบบเจาะจุด

หาก implementation อ่อนกว่ากฎด้านความปลอดภัย ความถูกต้อง หรือ data integrity
ห้ามลดกฎตาม implementation ให้รายงานหรือแก้ implementation ใน task ที่ได้รับอนุญาตแทน

## 5. Quick Routing

- เพิ่ม REST endpoint: `api-contracts.md` + `spec-backend.md`
- เพิ่ม SignalR event: `signalr-contracts.md` + `spec-backend.md`
- เพิ่ม Redis key: `redis-keys.md`
- แก้ state transition: `state-machine.md` + critical registry
- แก้แผนที่/พิกัด: `geojson-contracts.md` + spec ของ client/server
- แก้ migration: `spec-backend.md` หัวข้อ Database Evolution
