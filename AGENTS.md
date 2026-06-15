# AGENTS.md: Codex Project Instructions (v0.9.6)

## 1. Required Context Before Work (Context Routing)
ก่อนเริ่มทำภารกิจใดๆ ห้ามอ่านไฟล์ผังงานภาพรวมตรงๆ ให้เปิดอ่าน `AI-INDEX.md` เป็นประตูด่านแรกเพื่อหาทิศทางของ Spec ไฟล์ย่อยที่เกี่ยวข้องเท่านั้น เพื่อประหยัด Token Window และให้อ่าน `.docs/AI-CHANGELOG/` เฉพาะไฟล์ของวันที่เกี่ยวข้อง

## 2. Event Classification & Naming Conventions
ระบบมีการจัดหมวดหมู่อีเวนต์ออกเป็น 3 ประเภทอย่างเด็ดขาด ห้ามเขียนปนกันในโค้ดและโครงสร้างเอกสาร:

1. **Domain Events (ภายใน):** เกิดขึ้นภายใน Bounded Context เดียวกัน (คลาส .NET 8 ภายใน) ใช้สำหรับสื่อสารใน Process 
2. **Integration Events (ข้ามระบบ):** ใช้สื่อสารข้าม Microservices ผ่าน RabbitMQ บังคับตั้งมาตรฐานชื่อฟอร์แมต: `<Domain><Action>IntegrationEvent` เท่านั้น (เช่น `OrderCreatedIntegrationEvent`, `OrderStatusChangedIntegrationEvent`)
3. **Telemetry Events (เรียลไทม์ความถี่สูง):** ใช้สตรีมข้อมูลดิบความถี่สูงผ่าน SignalR/WebSocket (เช่น `RiderLocationUpdatedTelemetryEvent`, `TelemetryUpdatedTelemetryEvent`)

## 3. Strict Realtime & Architectural Constraints
- **SignalR Hub Core:** คลาส `TrackingHub` (และไฟล์ย่อยพาร์ท Partial Class) ทำหน้าที่เป็น **Pure Transport Layer** เท่านั้น (Validate, Authenticate, Route) ห้ามมี Business Logic หรือลอจิกเปลี่ยน State ฝังด้านในเด็ดขาด ให้ยิงส่งต่อเข้า Service Layer ทันที
- **Anti-Overengineering:** ใช้สแต็กดั้งเดิม PostgreSQL (PostGIS) + Redis + RabbitMQ + SignalR เท่านั้น ห้ามเพิ่ม Kafka, CQRS เต็มรูปแบบ หรือ Saga Coordinator ซับซ้อน ให้ใช้เพียง Lightweight Compensating Action
- **Idempotency Rule:** Consumer ทุกตัวบน RabbitMQ ต้องเช็คตาราง `ProcessedEvents` บน PostgreSQL ก่อนรัน Logic เสมอ เพื่อป้องกันข้อความซ้ำ
- **Anti-Event-Loop-Blocking:** ห้ามใช้ `async def` กับ FastAPI endpoint ที่ทำงาน CPU-bound ล้วนๆ (เช่น OR-Tools, Haversine matrix) → ใช้ `def` ให้ FastAPI จัดการ Thread Pool
- **Anti-XSS-Interpolation:** ห้ามทำ Raw String Interpolation ลง DOM ใน Angular/Leaflet popup → ต้อง escape input ก่อนเสมอ และใช้ Programmatic Event Binding แทน inline `onclick`
- **Reactive UI & Memory Leak Prevention:** สำหรับหน้าจอ UI เมื่อมีการเปลี่ยนแปลงสถานะหรือมีข้อมูล/พิกัดเพิ่มเข้ามาในแผนที่ ระบบต้องอัปเดตและรีเฟรชเฉพาะจุด/ส่วนที่เกี่ยวข้องแบบเรียลไทม์โดยอัตโนมัติ (Reactive Refresh) โดยไม่ต้องกดรีเฟรชหน้าจอใหม่เอง และต้องกำจัดความเสี่ยงเรื่อง Memory Leak อย่างเข้มงวด เช่น การทำ Unsubscribe หรือ Teardown ของ SignalR subscriptions, Leaflet markers/layers, และ DOM event listeners เมื่อ Component ถูกทำลาย

## 4. Trace Correlation Rules
All logs must include:
- CorrelationId
- OrderId
- RiderId (if available)

## 5. Forbidden Stack (Predictability > Complexity)
❌ ห้ามเพิ่มเครื่องมือเหล่านี้เด็ดขาด:
- Kafka
- Kubernetes
- CQRS เต็มระบบ
- Event Store
- Saga Orchestrator
- gRPC mesh
- Redis Cluster
- Elasticsearch

## 6. Testing Rules & Directories
1. **Single Test Hub Rule**: โฟลเดอร์รันการทดสอบทั้งหมด (C# Integration Tests, Python PyTest และ E2E Simulation/Load Tests) ต้องถูกรวบรวมไว้ภายใต้โฟลเดอร์เดียวคือ `RootScripts/scripts.test/test/` เท่านั้น (เช่น `RootScripts/scripts.test/BackendApi.IntegrationTests`, `RootScripts/scripts.test/ai-engine.tests`)
2. **Exception**: สำหรับแอปพลิเคชันหน้าบ้าน Angular ยูสเคสไฟล์สเปกทดสอบระดับยูนิต (`*.spec.ts`) ให้สามารถวางไว้ควบคู่กับ Component นั้นๆ ตามมาตรฐานระบบ Angular CLI เพื่อรักษาโครงสร้างการทำงานและการ compile pipeline ของ Angular โครงการ
3. **No Test Files in Core Directories**: ห้ามเขียนหรือสร้างโฟลเดอร์ทดสอบ (เช่น `tests/` หรือ `__tests__/`) ปนเปื้อนภายใน Context ไดเรกทอรีหลักของโปรเจค (เช่น `ai-engine/tests`) ให้ย้ายไปไว้ที่ `RootScripts/scripts.test/<component>.tests/` เท่านั้น
4. **Load & Stress Test Log Rule**: เมื่อรัน Load/Stress Test ในห้องแล็บ `Test_Breaking-Point` ห้ามเขียนไฟล์ Log หรือ CSV ทิ้งไว้ในโฟลเดอร์รูทของ `LogsTest` ตรงๆ แต่ต้องจัดเก็บแยกโฟลเดอร์ตามวันที่ปัจจุบันในฟอร์แมต `LogsTest/YYYY-MM-DD/` และใช้รูปแบบการตั้งชื่อไฟล์ที่เหมือนกันเสมอ ได้แก่:
   - `stage5_stats.csv` (สถิติตัววัด CPU/Mem ของด็อกเกอร์)
   - `stage5_run.log` (ผลการรันหรือ stdout ของสคริปต์ทดสอบ k6/Stress test)
   - `stage5_final_report.md` (รายงานสรุปประสิทธิภาพและการวิเคราะห์คอขวด)

## 7. Critical Code Protection Rules (Mandatory)

> **⚠️ ไฟล์และโค้ดบล็อกที่จัดอยู่ใน Critical Code Registry → ดูรายละเอียดใน [`CRITICAL-CODE-PROTECTION.md`](CRITICAL-CODE-PROTECTION.md)**

### 7.1 ห้ามกระทำ (Absolute Prohibitions)
1. ❌ **ห้ามลบไฟล์ Critical ทั้งไฟล์** — เด็ดขาด ไม่มีข้อยกเว้น
2. ❌ **ห้ามคอมเม้นต์ business logic ออก** — ห้ามเปลี่ยน active code ให้กลายเป็น comment (comment-out)
3. ❌ **ห้ามลบหรือเปลี่ยน function signature** ของ public interface/contract (เช่น `IAiService`, `solve_vrp`, `rank_candidates`)
4. ❌ **ห้ามลบ fallback mechanism ออก** — ทุก AI call ต้องมี fallback เสมอ (Haversine, Nearest-Neighbor, Rule-based ETA)
5. ❌ **ห้ามลบ State Machine transition rules** (`OrderState`, `RiderState`) — เพิ่มได้ ลบไม่ได้
6. ❌ **ห้ามเปลี่ยน router registration** ของ AI endpoints ใน `main.py` / `ServiceSetup.cs` ที่ทำให้ endpoint หายไป

### 7.2 อนุญาตโดยมีเงื่อนไข (Conditional Allowed)
1. ✅ แก้ไข implementation ภายในฟังก์ชัน Critical ได้ — แต่ต้องรักษา input/output contract เดิม
2. ✅ เพิ่ม transition rule ใหม่ใน State Machine ได้ — แต่ห้ามลบของเก่า
3. ✅ Refactor ได้ — แต่ต้อง verify ว่าทุก endpoint/function ยังทำงานครบถ้วนหลัง refactor
4. ✅ เพิ่ม Polly/retry/timeout config ได้ — แต่ห้ามลบ circuit breaker ที่มีอยู่

### 7.3 Verification Checklist (ก่อน commit ใดๆ ที่แตะไฟล์ Critical)
AI Agent หรือ Developer ต้องตรวจสอบก่อน commit:
- [ ] ทุก endpoint ที่อยู่ใน Critical list ยังถูก register อยู่หรือไม่?
- [ ] ทุก fallback path ยังทำงานหรือไม่?
- [ ] ทุก State transition rule ของเดิมยังครบอยู่หรือไม่?
- [ ] `IAiService` interface ยัง expose method เดิมครบหรือไม่?
- [ ] `main.py` ยัง include ทุก router (`v1_router`, `optimize.router`) หรือไม่?

## 8. Reference Documents
| Document | Purpose |
|---|---|
| `AI-INDEX.md` | Master Context Router — อ่านก่อนทุกงาน |
| `CRITICAL-CODE-PROTECTION.md` | Critical Code Registry — ไฟล์ที่ห้ามลบ/แก้ไขโดยไม่มีเหตุผล |
| `.docs/ai-context/runtime-rules.md` | Runtime coding constraints |
| `.docs/AI-CHANGELOG/` | History of changes (append-only) |