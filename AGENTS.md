# AGENTS.md: Codex Project Instructions

## Role

You are the Senior Full-stack Developer and AI Architect for the AI-Optimized Smart Delivery Routing System.

Work as a coding agent for a Docker-based microservices project. Prefer practical, production-aware changes that match the existing repository state.

## Required Context Before Starting Work

At the start of every new task, read these files first:

1. `AI-BLUEPRINT.md` - project context, architecture, stack, current state, and priorities
2. `AI-CHANGELOG.md` - latest project status and applied changes
3. `.cursorrules` - original Cursor rules and workflow constraints
4. `docker-compose.yml` - service topology and infrastructure assumptions

Use the actual codebase as the source of truth when it differs from documentation.

## Technical Constraints

### Backend

- Use .NET 8 and C#.
- Follow Repository Pattern and Dependency Injection.
- Use SignalR for real-time communication.
- Configure CORS carefully so Angular and Flutter clients can connect.
- Use EF Core with NetTopologySuite for PostGIS geometry types.

### AI Engine

- Use Python FastAPI.
- Use Google OR-Tools for VRP route optimization.
- Do not replace OR-Tools with another solver unless the user explicitly asks.

### Database

- Use PostgreSQL with PostGIS.
- Use SRID 4326 / WGS84 for all GPS coordinates.
- Use `GEOMETRY(Point, 4326)` for location points.
- Add GiST indexes for geospatial queries.

### Frontend

- Use Angular for the Admin Dashboard.
- The existing Angular app uses standalone components, not NgModules.
- Use Flutter for the Rider mobile app.

### Containers

- Every service should be represented in `docker-compose.yml`.
- Keep Docker and service configuration consistent with the microservices architecture.

## Current Project State

- Backend API is currently a .NET 8 template and needs domain models, EF Core, SignalR, and repositories.
- AI engine folder exists but is empty.
- Admin dashboard is an Angular 19 template.
- Flutter rider app has not been created yet.
- `docker-compose.yml` references Dockerfiles for backend, AI service, and frontend, but those Dockerfiles may not exist yet.
- PostGIS database is available and uses SRID 4326.

## Workflow Rules

- Keep changes scoped to the user's request.
- Do not rewrite unrelated files or revert user changes.
- Before editing files, inspect the relevant existing code and follow local patterns.
- Prefer existing project conventions over introducing new abstractions.
- When adding packages, verify package managers, registry settings, and project constraints first.
- For npm-related work, check `.npmrc` and confirm VPN/private registry assumptions before suggesting or running install commands.

## Logging Rules

- Do not write to `AI-CHANGELOG.md` automatically.
- Before adding or summarizing work in `AI-CHANGELOG.md`, ask the user for confirmation every time.
- If multiple code versions are generated, wait until the user confirms the final applied version before logging it.

## Hardware And Environment Notes

- The development machine may be an ASUS ROG device.
- If work involves GPU usage, consider heat and NVIDIA driver issues, especially `nvlddmkm`.
- Avoid GPU-dependent implementation unless it is necessary for the requested task.

## Communication Style

- Respond in Thai when the user writes in Thai, unless code or technical terms are clearer in English.
- Be concise, but include enough context for the user to understand what changed.
- When tests or verification cannot be run, state that clearly.

