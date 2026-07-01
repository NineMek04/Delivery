# 07 Optimization Routing And OSRM

## Optimization Engine

The optimization engine handles weighted heuristic rider ranking, OR-Tools
route optimization, and ETA estimation. It is not a trained ML model. FastAPI
endpoints that run CPU-bound logic must use normal `def`, not `async def`, so
FastAPI can run them in the thread pool.

## Backend Optimization Boundary

Backend calls the route optimizer through `IAiService` and must keep
deterministic fallback behavior. Public interface signatures are protected by
`CRITICAL-CODE-PROTECTION.md`.

Current VRP matrix source is local OSRM `/table` in
`route-optimizer/app/core/vrp_solver.py`. Haversine remains the deterministic
fallback if local OSRM is unavailable, incomplete, or invalid.

## Local OSRM

Backend route service uses local OSRM:

```text
Routing__LocalOsrmUrl=http://osrm:5000
Host dev port: http://localhost:5001
```

Production must not use public OSRM fallback for GPS/route requests because it can leak sensitive location data. When local OSRM fails, backend returns local Haversine/raw coordinate fallback.

## Route Resolution For Rider App

Flutter app calls:

```text
POST /api/v1/rider-routes/resolve
```

Backend verifies assigned-rider ownership, calls local OSRM, and returns:

```json
{
  "encodedPolyline": "...",
  "coordinates": [[102.78732, 17.41401], [102.78748, 17.41412]],
  "distanceMeters": 1200,
  "durationSeconds": 300,
  "source": "LOCAL_OSRM"
}
```

`coordinates` uses OSRM/GeoJSON order `[lng, lat]`. Flutter should decode the
encoded polyline first, then fall back to coordinates if decoding fails. If
OSRM is unavailable, source can be `HAVERSINE_FALLBACK`; Flutter reports
fallback telemetry and enters route-unavailable/degraded navigation state.

## Batch Dispatch Route Ordering

For OSRM trip sequence, `waypoint_index` is a visit-order value per input waypoint. Callers must compare `seq[inputIndex]`, not `IndexOf(inputIndex)`.
