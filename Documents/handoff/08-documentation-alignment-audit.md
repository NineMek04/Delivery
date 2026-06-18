# 08 Documentation Alignment Audit

Audit date: 2026-06-18

## Decisions

| Area | Mismatch | Better Side | Decision |
|---|---|---|---|
| Telemetry mobile config endpoint | Some docs used `GET /telemetry/mobile-config`; code exposes `GET /api/v1/telemetry/config/mobile`. | Codebase | Updated active API contract to the real route. |
| SignalR implementation path | Contract pointed to `BackendApi/Hubs/TrackingHub*.cs`; actual path is `BackendApi/Hubs/Tracking/TrackingHub*.cs`. | Codebase | Updated contract path. |
| Chat hub | Code maps `/hubs/chat`; SignalR contract described only tracking hub. | Codebase | Added chat hub note while keeping telemetry on tracking hub. |
| Compose services | Infra spec omitted `cadvisor`, `node-exporter`, `postgres-exporter`, `redis-exporter`; compose and Prometheus use them. | Codebase | Keep exporters; they improve observability. Updated infra spec and handoff docs. |
| OSRM fallback | Old OSRM setup doc described public OSRM fallback; current code uses local OSRM and Haversine fallback. | Codebase and security spec | Keep local-only behavior; updated setup doc. |
| Customer order clear | Contract says soft-delete; older `DBHandlerCore` logic only checked `DelFlag`/`DEL_FLAG` before falling back to EF delete state. | Document/security contract | Fixed in `DBHandlerCore`: current `ISoftDeletableEntity`/`IsDeleted` entities are marked soft-deleted directly, with legacy `DelFlag` fallback retained. |
| Backend layout | Older subsystem READMEs describe flatter folders and some stale paths. | Codebase | Handoff docs document actual layout; old docs remain historical unless updated. |
| Migration policy | Backend spec says baseline should stay minimal; current migrations include `20260615045328_AddDispatchAttemptsToOrder`. | Policy is better | Do not add more hand-written routine migrations; future cleanup should squash carefully after verification. |
| Forbidden stack wording | Old migration guide mentioned Kubernetes Init Container. | Active rules | Reworded to CI/CD or one-shot runner; Kubernetes remains forbidden. |
| Flutter route drawing | Some old docs imply admin route drawing as central behavior. | Current architecture | Rider navigation belongs in Flutter; admin map is operations visibility only. Handoff clarifies this boundary. |
| Store analytics | Flutter store summary code still contains mock sales/reviews notes. | Documented product goal is better | Keep as known implementation gap; do not document mock data as production-complete. |
| Mojibake/encoding in old READMEs | Several old files have corrupted letters such as `eubsystem`/`aackendApi`. | New handoff docs | Created clean handoff docs instead of treating corrupted text as source of truth. |

## Follow-Up Code Gaps

These are not documentation edits; they are implementation tasks found during alignment:

1. Decide whether `20260615045328_AddDispatchAttemptsToOrder` should be folded into a new verified baseline later.
2. Replace remaining mock data in StorePartner summary if that screen is expected to be production-grade.
