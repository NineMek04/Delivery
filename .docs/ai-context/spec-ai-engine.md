# Optimization Engine (FastAPI + OR-Tools)

**Version:** 1.0.0 | **Updated:** 2026-06-14

## 1. Structure

```text
route-optimizer/
  main.py
  app/
    api/v1/api.py
    api/v1/endpoints/{optimize,dispatch,predict}.py
    core/{vrp_solver,scoring,geo_utils,security}.py
    models/{routing_models,dispatch_models}.py
```

Tests อยู่ใน `RootScripts/scripts.test/test/route-optimizer.tests/`

## 2. Protected Endpoints

- `POST /api/optimize-route`
- `POST /api/v1/dispatch/rank`
- `POST /api/v1/predict-eta`
- `GET /health`

สาม endpoint แรกต้องใช้ route optimizer API key. CPU-bound handlers เป็น synchronous `def`.

## 3. Optimize Contract

```json
{
  "locations": [
    { "id": "depot", "lat": 17.41, "lng": 102.78 },
    { "id": "drop-1", "lat": 17.42, "lng": 102.79 }
  ],
  "num_vehicles": 1,
  "depot": 0,
  "pickups_deliveries": []
}
```

Response:

```json
{
  "status": "SUCCESS",
  "total_distance_meters": 1200,
  "optimized_route": [
    { "sequence": 1, "location_id": "depot", "lat": 17.41, "lng": 102.78, "vehicle_id": 0 }
  ]
}
```

จำกัด locations 100, vehicles 50, solver time 5 วินาที.

## 4. Dispatch Rank Contract

Request มี `context`, `order {id,pickup,dropoff,sla_limit_minutes}` และ candidates
ไม่เกิน 200 ราย แต่ละรายมี `rider_id`, `lat`, `lng`, `speed_kmh`,
`current_tasks`.

Response key คือ `ranked_candidates`; member ใช้ `distance_to_pickup_km`,
`eta_minutes`, `score`, `breakdown`.

## 5. Solver, Ranking, And Fallback

- OR-Tools `PATH_CHEAPEST_ARC`
- Haversine matrix ใน `compute_distance_matrix`
- pickup-delivery precedence ต้องคงอยู่
- ranking เป็น deterministic heuristic: distance, workload, direction, speed
- ห้ามเพิ่ม external routing API, GPU dependency หรือ ML model โดยพลการ
- `solve_vrp`, `rank_candidates` และ geo utility signatures เป็น critical contract

Naming rule: this component is named `route-optimizer`. Legacy configuration
keys such as `AI_SERVICE_URL` may remain as compatibility aliases only. The
implemented algorithms are not trained machine-learning models. `scoring.py`
is weighted heuristic/rule-based ranking. `vrp_solver.py` uses OR-Tools
mathematical optimization over a local OSRM `/table` matrix with Haversine
fallback.

Route matrix rule: `compute_distance_matrix` must call local OSRM `/table`
first for road-network distance values. If OSRM is unavailable, incomplete, or
invalid, fallback to deterministic Haversine matrix. Public OSRM is forbidden.

## 6. Runtime

Docker ใช้ Python 3.11 และ port ภายใน 8000. Compose dev expose
`127.0.0.1:8009`; production ไม่ expose route optimizer service สู่ public network.
