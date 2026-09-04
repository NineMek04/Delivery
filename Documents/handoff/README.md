# Delivery System Handoff

เอกสารชุดนี้คือชุดส่งต่อทีมสำหรับระบบ Delivery ทั้งระบบ อ่านจากบนลงล่างได้โดยไม่ต้องไล่ค้นจากหลายที่ก่อนเริ่มงานจริง

## Reading Order

1. [01 System Overview](01-system-overview.md) - ภาพรวมระบบ, bounded context, runtime topology
2. [02 Backend API](02-backend-api.md) - REST API, state, dispatch, database, migration
3. [03 Realtime And Events](03-realtime-and-events.md) - SignalR, RabbitMQ, telemetry, event naming
4. [04 Flutter App](04-flutter-app.md) - Rider, Customer, StorePartner flows และ route map
5. [05 Admin Dashboard](05-admin-dashboard.md) - Angular dashboard, map, auth, realtime UI
6. [06 Infrastructure And DevOps](06-infrastructure-devops.md) - Docker Compose, ports, observability, OSRM, Nginx
7. [07 Optimization Routing And OSRM](07-ai-routing-osrm.md) - route optimizer, local OSRM, fallback, route drawing
8. [08 Documentation Alignment Audit](08-documentation-alignment-audit.md) - จุดที่เอกสารไม่ตรงโค้ดและ decision ว่าควรเก็บฝั่งไหน

## Source Of Truth

Priority order when documents conflict:

1. `AGENTS.md`, `AI-INDEX.md`, and `CRITICAL-CODE-PROTECTION.md`
2. Active specs/contracts under `.docs/ai-context/`
3. Current implementation and automated tests
4. Historical documents under `Documents/development/PROJECT-SPEC.md`, `AI-BLUEPRINT.md`, and old setup guides

Do not move `.docs` files. Update them in place when an active contract is wrong.
