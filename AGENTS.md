# AGENTS.md: Codex Project Instructions (v0.9.5)

## 1. Required Context Before Work (Context Routing)
ก่อนเริ่มทำภารกิจใดๆ ห้ามอ่านไฟล์ผังงานภาพรวมตรงๆ ให้เปิดอ่าน `AI-INDEX.md` เป็นประตูด่านแรกเพื่อหาทิศทางของ Spec ไฟล์ย่อยที่เกี่ยวข้องเท่านั้น เพื่อประหยัด Token Window

## 2. Event Classification & Naming Conventions
ระบบมีการจัดหมวดหมู่อีเวนต์ออกเป็น 3 ประเภทอย่างเด็ดขาด ห้ามเขียนปนกันในโค้ดและโครงสร้างเอกสาร:

1. **Domain Events (ภายใน):** เกิดขึ้นภายใน Bounded Context เดียวกัน (คลาส .NET 8 ภายใน) ใช้สำหรับสื่อสารใน Process 
2. **Integration Events (ข้ามระบบ):** ใช้สื่อสารข้าม Microservices ผ่าน RabbitMQ บังคับตั้งมาตรฐานชื่อฟอร์แมต: `<Domain><Action>IntegrationEvent` เท่านั้น (เช่น `OrderCreatedIntegrationEvent`, `OrderStatusChangedIntegrationEvent`)
3. **Telemetry Events (เรียลไทม์ความถี่สูง):** ใช้สตรีมข้อมูลดิบความถี่สูงผ่าน SignalR/WebSocket (เช่น `RiderLocationUpdatedTelemetryEvent`, `TelemetryUpdatedTelemetryEvent`)

## 3. Strict Realtime & Architectural Constraints
- **SignalR Hub Core:** คลาส `TrackingHub` (และไฟล์ย่อยพาร์ท Partial Class) ทำหน้าที่เป็น **Pure Transport Layer** เท่านั้น (Validate, Authenticate, Route) ห้ามมี Business Logic หรือลอจิกเปลี่ยน State ฝังด้านในเด็ดขาด ให้ยิงส่งต่อเข้า Service Layer ทันที
- **Anti-Overengineering:** ใช้สแต็กดั้งเดิม PostgreSQL (PostGIS) + Redis + RabbitMQ + SignalR เท่านั้น ห้ามเพิ่ม Kafka, CQRS เต็มรูปแบบ หรือ Saga Coordinator ซับซ้อน ให้ใช้เพียง Lightweight Compensating Action
- **Idempotency Rule:** Consumer ทุกตัวบน RabbitMQ ต้องเช็คตาราง `ProcessedEvents` บน PostgreSQL ก่อนรัน Logic เสมอ เพื่อป้องกันข้อความซ้ำ

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