# GitHub Copilot Instructions

# Auto-loaded by GitHub Copilot in VS Code

## Project: AI-Optimized Smart Delivery Routing System

### Architecture

Microservices on Docker Compose with 4 containers:

- **BackendApi/** → .NET 8 (ASP.NET Core) + SignalR — Gateway & Business Logic
- **ai-engine/** → Python 3.11 FastAPI + Google OR-Tools — VRP Route Solver
- **admin-dashboard/** → Angular 19 (Standalone Components) — Admin Web Dashboard
- **Flutter App** → Rider Mobile (GPS Tracking) — not yet created in repo

### Technical Constraints

- Backend: Use Repository Pattern + Dependency Injection. Real-time via SignalR only.
- Database: PostgreSQL + PostGIS. All coordinates use SRID 4326 (WGS84). Use GiST Index.
- AI: Google OR-Tools for VRP solving. Python async endpoints.
- Frontend: Angular standalone components (no NgModules). TypeScript 5.7.
- All services must be defined in docker-compose.yml.

### Key Files to Reference

- `AI-BLUEPRINT.md` — Full project context & architecture
- `AI-CHANGELOG.md` — Change history & current status
- `PROJECT-SPEC.md` — Team specification document
- `docker-compose.yml` — Infrastructure configuration

### Code Style

- .NET: Follow ASP.NET Core conventions, async/await everywhere
- Python: FastAPI style, type hints, Pydantic models
- Angular: Standalone components, RxJS observables, signals where appropriate
- Git: Conventional Commits (feat/fix/docs/refactor/chore)
