---
scope: AI Engine (Python FastAPI + OR-Tools VRP)
source_of_truth:
  - PROJECT-SPEC.md (Section 9, AI Engine Specification)
  - AI-BLUEPRINT.md (Technology Stack — AI Engine)
  - AI-CHANGELOG.md (2026-05-14 Scoring Engine, 2026-05-15 AI Refactoring, 2026-05-20 AI Tests)
  - ai-engine/ (codebase)
related_contexts:
  - .docs/ai-context/spec-backend.md
  - .docs/ai-context/spec-blueprint.md
  - .docs/ai-context/contracts/api-contracts.md
forbidden_patterns:
  - เปลี่ยน OR-Tools เป็น solver อื่นโดยไม่ได้รับคำสั่ง
  - ใช้ external routing API ในการคำนวณ distance matrix
  - เพิ่ม GPU dependency โดยไม่จำเป็น
known_pitfalls:
  - OR-Tools PATH_CHEAPEST_ARC อาจไม่ optimal สำหรับ large-scale VRP (ยังไม่ใช้ปัญหาในขนาดนี้)
  - Haversine เป็น approximation — ใช้เป็น distance matrix เท่านั้น (ไม่ใช่ final distance)
  - Candidate ranking ใช้ Heuristic Phase A เท่านั้น (ยังไม่ใช้ ML)
---

# spec-ai-engine.md — AI Engine (Python FastAPI + OR-Tools)

> **Source**: `PROJECT-SPEC.md` Sec 9 + `AI-BLUEPRINT.md` + `AI-CHANGELOG.md`  
> **For API endpoint contracts** → `contracts/api-contracts.md`

---

## 1. Tech Stack

| Layer | Technology | Version |
|---|---|---|
| Framework | FastAPI | Latest |
| Runtime | Python | 3.11-slim (Docker) |
| Solver | Google OR-Tools | Latest |
| Geometry | Haversine (custom) | geo_utils.py |
| Testing | Pytest + httpx | — |

---

## 2. Project Structure

```
ai-engine/
├── main.py                  ← FastAPI app entrypoint
├── requirements.txt
├── Dockerfile
└── app/
    ├── core/
    │   ├── geo_utils.py     ← Haversine distance + Bearing calculation
    │   └── scoring.py       ← Rider ranking heuristic (Phase A)
    ├── vrp/
    │   └── solver.py        ← OR-Tools VRP implementation
    └── tests/
        ├── test_vrp_solver.py    ← Distance matrix + solver behavior
        ├── test_api_optimize.py  ← /health + /api/optimize-route
        └── test_api_dispatch.py  ← /api/v1/dispatch/rank
```

---

## 3. VRP Solver (OR-Tools)

**Algorithm:** `PATH_CHEAPEST_ARC` (First Solution Strategy)

```python
from ortools.constraint_solver import routing_enums_pb2, pywrapcp

def solve_vrp(distance_matrix: list[list[float]], num_vehicles: int, depot: int):
    manager = pywrapcp.RoutingIndexManager(
        len(distance_matrix), num_vehicles, depot
    )
    routing = pywrapcp.RoutingModel(manager)

    def distance_callback(from_index, to_index):
        from_node = manager.IndexToNode(from_index)
        to_node = manager.IndexToNode(to_index)
        return int(distance_matrix[from_node][to_node] * 1000)  # meters

    transit_callback_index = routing.RegisterTransitCallback(distance_callback)
    routing.SetArcCostEvaluatorOfAllVehicles(transit_callback_index)

    search_params = pywrapcp.DefaultRoutingSearchParameters()
    search_params.first_solution_strategy = (
        routing_enums_pb2.FirstSolutionStrategy.PATH_CHEAPEST_ARC
    )

    solution = routing.SolveWithParameters(search_params)
    return extract_solution(manager, routing, solution)
```

---

## 4. Distance Matrix (Haversine)

```python
# app/core/geo_utils.py
import math

def haversine(lat1, lng1, lat2, lng2) -> float:
    """Returns distance in kilometers"""
    R = 6371  # Earth radius
    phi1, phi2 = math.radians(lat1), math.radians(lat2)
    dphi = math.radians(lat2 - lat1)
    dlambda = math.radians(lng2 - lng1)
    a = math.sin(dphi/2)**2 + math.cos(phi1)*math.cos(phi2)*math.sin(dlambda/2)**2
    return 2 * R * math.asin(math.sqrt(a))

def bearing(lat1, lng1, lat2, lng2) -> float:
    """Returns bearing in degrees (0-360)"""
    phi1, phi2 = math.radians(lat1), math.radians(lat2)
    dlambda = math.radians(lng2 - lng1)
    x = math.sin(dlambda) * math.cos(phi2)
    y = math.cos(phi1)*math.sin(phi2) - math.sin(phi1)*math.cos(phi2)*math.cos(dlambda)
    return (math.degrees(math.atan2(x, y)) + 360) % 360

def build_distance_matrix(locations: list[tuple]) -> list[list[float]]:
    """Build NxN distance matrix from list of (lat, lng) tuples"""
    n = len(locations)
    matrix = [[0.0] * n for _ in range(n)]
    for i in range(n):
        for j in range(n):
            if i != j:
                matrix[i][j] = haversine(*locations[i], *locations[j])
    return matrix
```

---

## 5. Rider Ranking / Scoring Engine (Phase A Heuristic)

**File:** `app/core/scoring.py`

ระบบ rank riders โดยพิจารณา 3 ปัจจัย:

```python
def score_rider(rider, order_location) -> float:
    """Lower score = better candidate"""
    # Factor 1: Distance (most important)
    distance = haversine(rider['lat'], rider['lng'],
                         order_location['lat'], order_location['lng'])
    distance_score = distance * 10

    # Factor 2: Current workload (active orders)
    workload_score = rider.get('active_orders', 0) * 5

    # Factor 3: Direction alignment (bearing toward order)
    rider_bearing = rider.get('current_bearing', 0)
    order_bearing = bearing(rider['lat'], rider['lng'],
                            order_location['lat'], order_location['lng'])
    bearing_diff = abs(rider_bearing - order_bearing) % 360
    direction_score = min(bearing_diff, 360 - bearing_diff) / 36  # 0-10

    return distance_score + workload_score + direction_score
```

---

## 6. API Endpoints

### `POST /api/optimize-route`

Request:
```json
{
  "depot": { "lat": 17.41, "lng": 102.78 },
  "waypoints": [
    { "lat": 17.42, "lng": 102.79, "orderId": "uuid" },
    { "lat": 17.40, "lng": 102.77, "orderId": "uuid" }
  ],
  "num_vehicles": 1
}
```

Response:
```json
{
  "optimized_order": ["uuid-1", "uuid-2"],
  "total_distance_km": 5.2,
  "route": [[17.41, 102.78], [17.42, 102.79], [17.40, 102.77]]
}
```

### `POST /api/v1/dispatch/rank`

Request:
```json
{
  "order_location": { "lat": 17.41, "lng": 102.78 },
  "radius_km": 5.0,
  "candidates": [
    { "rider_id": "uuid", "lat": 17.42, "lng": 102.79,
      "active_orders": 0, "current_bearing": 180.0 }
  ]
}
```

Response:
```json
{
  "ranked": [
    { "rider_id": "uuid", "score": 2.3, "distance_km": 1.2 }
  ]
}
```

### `GET /health`

Response: `{ "status": "ok" }`

---

## 7. Testing

```bash
# Run AI engine tests
cd ai-engine
pip install pytest httpx
pytest tests/ -v

# Tests coverage
# test_vrp_solver.py   → distance matrix, OR-Tools solver
# test_api_optimize.py → /health, /api/optimize-route
# test_api_dispatch.py → /api/v1/dispatch/rank, radius filtering
```

---

## 8. Docker Configuration

```dockerfile
# ai-engine/Dockerfile
FROM python:3.11-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
```

**Internal URL (Docker network):** `http://ai-service:8000`  
**External URL:** `http://localhost:8000`  
**Docs:** `http://localhost:8000/docs`
