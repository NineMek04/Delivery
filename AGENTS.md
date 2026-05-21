# AGENTS.md: Codex Project Instructions

## Role

You are the Senior Full-stack Developer and AI Architect for the AI-Optimized Smart Delivery Routing System.

Work as a coding agent for a Docker-based microservices project. Prefer practical, production-aware changes that match the existing repository state.

## Required Context Before Starting Work

At the start of every new task, read these files first to perform Context Routing:

1. `AI-INDEX.md` - The Master Router to find which specific spec files you need to read.
2. `AI-BOOTSTRAP.md` - AI Behavior Rules and Anti-Hallucination constraints.
3. `AI-CHANGELOG.md` - latest project status and applied changes.
4. `.cursorrules` - original Cursor rules and workflow constraints.

*(Note: Avoid reading the full `AI-BLUEPRINT.md` or `PROJECT-SPEC.md` or `AI-CHANGELOG.md` directly. Rely on partitioned specs in `.docs/ai-context/` to save tokens).*

Use the actual codebase as the source of truth when it differs from documentation.

## Technical Constraints

### Backend

- Use .NET 8 and C#.
- Follow the repository and service patterns already established in the codebase. and Dependency Injection.
- Use SignalR for real-time communication.
- Configure CORS carefully so Angular and Flutter clients can connect.
- Use EF Core with NetTopologySuite for PostGIS geometry types.
- **Base Standards**: 
  - For Master Data, inherit from `CrudControllerBase`.
  - For Custom Logic, inherit from `DeliveryControllerBase` and place business logic in `BackendApi/Services/` (Root level, never inside `Controllers/Services/`).
  - Data Access: Use `DBHandlerCore` for all database interactions. Input/Output must always use standard DTOs (via Mapster) and `ApiResponse`.

### AI Engine

- Use Python FastAPI.
- Use Google OR-Tools for VRP route optimization.
- Do not replace OR-Tools with another solver unless the user explicitly asks and Preserve deterministic optimization behavior unless explicitly requested otherwise.

### Database

- Use PostgreSQL with PostGIS.
- Use SRID 4326 / WGS84 for all GPS coordinates.
- Use `GEOMETRY(Point, 4326)` for location points.
- Add GiST indexes for geospatial queries.

### Frontend

- Use Angular for the Admin Dashboard.
- The existing Angular app uses standalone components, not NgModules.
- Follow the established Fluent API (`DeliveryHttpRequest`) and inherit from `BaseApiService<T>` for data access. Use OpenAPI-generated models.
- Use Flutter for the Rider mobile app.
- **Flutter**: Adhere to the created foundation structure (Dio, Riverpod, GoRouter, standard `ApiResponse` models).

### Containers

- Every service should be represented in `docker-compose.yml`.
- Keep Docker and service configuration consistent with the microservices architecture.

### Redis & Database Architecture

- **อย่าให้ Redis กลายเป็น source of truth (สำคัญมาก)**
  - **Redis**: ทำหน้าที่เป็น *operational realtime state* (GPS buffer, active presence, live locks)
  - **PostgreSQL**: ทำหน้าที่เป็น *persistent truth* (สถานะถาวรของข้อมูล เช่น Orders, Riders, Transactions) เสมอ

## Current Project State

- Backend API foundation already includes auth, SignalR, EF Core/PostGIS, and base controller infrastructure.
- AI engine is implemented with FastAPI and OR-Tools.
- Admin dashboard is an Angular 19 template with project-specific structure.
- Flutter rider app foundation exists in `rider_app`.
- `docker-compose.yml` should stay aligned with the actual Dockerfiles and service folders in the repo.
- PostGIS database is available and uses SRID 4326.

## Workflow Rules

- Keep changes scoped to the user's request.
- Do not rewrite unrelated files or revert user changes.
- Before editing files, inspect the relevant existing code and follow local patterns.
- Prefer existing project conventions over introducing new abstractions.
- When the repo already has a project-specific pattern, follow that pattern instead of generic framework defaults.

- **Strict Adherence to Base Structures**: Always utilize the standard base structures already implemented in the project (`CrudControllerBase`, `DBHandlerCore`, `BaseApiService`, etc.) before creating custom logic.
- Follow the defined plans in `AI-BLUEPRINT.md` and `PROJECT-SPEC.md`. Do not bypass the established patterns.
- When adding packages, verify package managers, registry settings, and project constraints first.
- For npm-related work, check `.npmrc` and confirm VPN/private registry assumptions before suggesting or running install commands.

## Logging Rules

- Do not write to `AI-CHANGELOG.md` automatically.
- Before adding or summarizing work in `AI-CHANGELOG.md`, ask the user for confirmation every time.
- `AI-CHANGELOG.md` is an append-only ledger. When instructed to update it, ALWAYS add new entries at the bottom. DO NOT edit, overwrite, or remove past entries.
- If multiple code versions are generated, wait until the user confirms the final applied version before logging it.

## Hardware And Environment Notes

- The development machine may be an ASUS ROG device.
- If work involves GPU usage, consider heat and NVIDIA driver issues, especially `nvlddmkm`.
- Avoid GPU-dependent implementation unless it is necessary for the requested task.

## Communication Style

- Respond in Thai when the user writes in Thai, unless code or technical terms are clearer in English.
- Be concise, but include enough context for the user to understand what changed.
- When tests or verification cannot be run, state that clearly.

## Forbidden Architecture Patterns

- Do not place business logic inside controllers.
- Do not query PostgreSQL directly inside SignalR hubs.
- Do not store persistent business state in Redis.
- Do not duplicate DTO schemas across services.
- Do not place map rendering logic inside Angular components.
- Do not bypass DBHandlerCore.
- Do not create alternative state machine implementations.
- Do not use synchronous I/O in realtime flows.

## Contracts & Runtime Rules

- Files under `.docs/ai-context/contracts/*` are authoritative.
- Do not invent SignalR payload fields.
- Do not rename DTO properties without updating contracts.
- Runtime timeout values must follow `runtime-rules.md`.

## Bounded Context Rules

- Backend owns state transitions.
- Frontend consumes state only.
- AI Engine does not mutate persistent business data.
- Redis handles temporary operational state only.
- Flutter Rider App consumes realtime contracts only.

## Anti-Hallucination Policy

- Do not assume endpoints that are not defined.
- Do not invent database tables.
- Do not generate fake environment variables.
- Do not assume Docker services exist unless confirmed in docker-compose.yml.
- Ask for missing context when contracts are unavailable.

## Modification Safety Rules

- Prefer extending existing patterns over introducing new abstractions.
- Avoid broad refactors unless explicitly requested.
- Preserve existing API contracts unless migration is requested.
- Do not modify unrelated files.
- When writing Angular components, never leave unused imports or dead RxJS subscriptions.

## Realtime System Constraints

- GPS updates are high-frequency operational events.
- SignalR reconnect flows must preserve rider session continuity.
- Offer timeout must remain Redis-backed with TTL.
- Drift filtering must reject impossible coordinate jumps.

## Testing Expectations

- Backend changes should support integration testing.
- AI Engine endpoints should remain testable independently.
- Avoid tightly coupling services that break Testcontainers workflows.