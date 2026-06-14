# AI-BOOTSTRAP.md - Agent Behavior Rules

**Version:** 1.0.0 | **Last Updated:** 2026-06-14

## 1. Pre-Task Protocol

1. อ่าน `AI-INDEX.md`
2. อ่านไฟล์นี้
3. เปิดเฉพาะ active spec/contract ที่ index route
4. ตรวจ implementation และ tests จริง
5. อ่าน changelog เฉพาะวันที่เกี่ยวข้องเมื่อจำเป็น

ห้ามโหลด archive ทั้งชุด และห้ามแก้ `.docs/AI-CHANGELOG/` อัตโนมัติ

## 2. Source Precedence

เมื่อข้อมูลขัดกันให้เรียง:

1. `AGENTS.md` และ critical protection
2. active contracts/specs ที่ `AI-INDEX.md` ชี้
3. implementation + automated tests ปัจจุบัน
4. historical archives/changelog

ถ้า implementation อ่อนกว่ากฎด้าน security, correctness, data integrity หรือ
resilience ห้ามลดกฎให้ตามบัค ให้แก้ implementation เมื่อ task อนุญาตหรือรายงาน
ความขัดแย้ง

## 3. Mandatory Boundaries

- Controller/Hub เป็น transport layer; business logic อยู่ service/feature
- ห้าม inject/use DbContext โดยตรงใน Controller/Hub
- Redis ไม่ใช่ source of truth
- RabbitMQ consumer ต้อง idempotent ผ่าน ProcessedEvents
- Integration event ใช้ชื่อ `<Domain><Action>IntegrationEvent`
- SignalR high-frequency event เป็น telemetry ไม่ใช่ integration event
- location ใช้ SRID 4326; persisted proximity query ใช้ PostGIS/GiST
- AI/OSRM ต้องมี deterministic fallback ตาม critical registry
- ห้ามลบ state transition, fallback, protected endpoint หรือ router เดิม
- Logs ต้องมี CorrelationId, OrderId, RiderId เมื่อมีค่า

รายละเอียด component-specific ให้ยึด `runtime-rules.md`.

## 4. Anti-Hallucination

- ห้ามเดา endpoint, payload, Redis key, SignalR event หรือ state
- ตรวจ contract แล้วตรวจ producer/consumer จริงก่อนแก้
- ห้ามสมมติว่า Angular/Flutter field casing ตรง backend โดยไม่ verify
- ห้ามอ้าง path หรือ class ที่ไม่มีใน repository
- ถ้าหลักฐานยังไม่พอให้ค้น codebase ก่อนถามผู้ใช้

## 5. Scope And Change Safety

- เปลี่ยนเฉพาะสิ่งที่ task ขอ
- ห้าม revert งานผู้ใช้หรือ unrelated changes
- ห้ามเพิ่ม dependency/stack โดยไม่จำเป็น
- ห้ามลบ Layer 1 archives
- migration ที่ใช้งานแล้วห้าม squash โดยไม่มี compatibility bridge และ fresh/
  existing database verification

## 6. Known Critical Pitfalls

- SignalR reconnect: ห้ามส่งก่อน connected; queue GPS/status offline
- Offer accept/reject: ตรวจ offer version และ lock ownership
- OSRM trip sequence: ใช้ `sequence[inputIndex]`, ไม่ใช้ `IndexOf(inputIndex)`
- Partial shop update: nullable fields ป้องกัน `IsOpen`/prep time ถูกเขียนทับ
- ProcessedEvents cleanup: ต้องมี index ที่ `ProcessedAt`
- PostGIS Point order: X=lng, Y=lat
- Public OSRM: ห้ามใช้กับ production coordinates
- Dashboard auth: HttpOnly cookie + XSRF; ห้ามเก็บ access/refresh tokenใน localStorage
- API error: HTTP status และ `ApiResponse.status` ต้องตรงกันและมี JSON body

## 7. Verification

เลือก verification ตาม blast radius: build, focused tests, integration tests,
contract search, link/path checks และ `git diff --check`. หากไม่ได้รันต้องแจ้งผู้ใช้.
