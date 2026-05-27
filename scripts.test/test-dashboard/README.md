# Test Dashboard Hybrid Runtime

Internal test dashboard runs as local dev processes, while test execution still uses short-lived Docker containers.

## Architecture

- Local: Angular dashboard, Node API, BullMQ worker
- Docker: Redis, PostgreSQL/PostGIS, OSRM, Backend API, AI Engine
- Ephemeral Docker: pytest, dotnet test, load/stress runners

## Start

1. Start infra from repo root:

```powershell
docker compose up -d
```

2. Start dashboard processes:

```powershell
.\scripts.test\test-dashboard\scripts\start-local.ps1
```

Or run them manually:

```powershell
cd scripts.test\test-dashboard\backend
npm.cmd run dev:api
npm.cmd run dev:worker

cd ..\frontend
npm.cmd start
```

## URLs

- Frontend: http://localhost:4200
- API: http://localhost:3001

## Security

The frontend sends only a suite key such as `python`, `csharp`, `load`, or `simulator`.
The worker reads the allowlist in `backend/config/sandbox-policy.json` and launches only approved Docker images/commands.
