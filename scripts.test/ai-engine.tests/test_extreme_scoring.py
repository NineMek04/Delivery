import time
from fastapi.testclient import TestClient
from main import app

client = TestClient(app)

def test_extreme_scoring_10000_candidates_performance():
    # Arrange: Generate 10,000 riders in a close circle around the pickup point
    # Pickup location: Bangkok Center (13.7563, 100.5018)
    pickup_lat = 13.7563
    pickup_lng = 100.5018

    candidates = []
    for i in range(10000):
        # Place riders in very small increments (within ~1-2km)
        lat_offset = (i % 100) * 0.0001
        lng_offset = (i // 100) * 0.0001
        candidates.append({
            "rider_id": f"rider_{i}",
            "lat": pickup_lat + lat_offset,
            "lng": pickup_lng + lng_offset,
            "speed_kmh": 25.0,
            "current_tasks": []
        })

    payload = {
        "context": {
            "timestamp": "2026-05-30T00:00:00Z",
            "city": "Bangkok"
        },
        "order": {
            "id": "ORD-EXTREME-100K",
            "pickup": [pickup_lat, pickup_lng],
            "dropoff": [13.8000, 100.5500],
            "sla_limit_minutes": 30
        },
        "candidates": candidates
    }

    # Act: Measure exact performance of scoring and ranking engine
    start_time = time.perf_counter()
    response = client.post("/api/v1/dispatch/rank", json=payload)
    end_time = time.perf_counter()
    
    elapsed_ms = (end_time - start_time) * 1000

    # Assert
    assert response.status_code == 200
    ranked = response.json()["ranked_candidates"]
    
    # We should have all 10,000 candidates scored and returned
    assert len(ranked) == 10000
    
    # Ensure it's ordered by score ascending (lowest score first, as lower is better)
    # Check that first candidate has a lower score than the last candidate
    assert ranked[0]["score"] <= ranked[-1]["score"]
    
    # Performance assertion: ASUS ROG i9 should score 10,000 candidates in less than 300ms (accounting for VM/Docker-on-Windows overhead)
    print(f"Scored 10,000 candidates in {elapsed_ms:.2f} ms")
    assert elapsed_ms < 300.0
