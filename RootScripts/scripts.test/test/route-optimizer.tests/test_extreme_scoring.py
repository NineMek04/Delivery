import time
from app.core.scoring import rank_candidates

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

    order_dict = {
        "id": "ORD-EXTREME-100K",
        "pickup": (pickup_lat, pickup_lng),
        "dropoff": (13.8000, 100.5500),
        "sla_limit_minutes": 30
    }

    # Act: Measure exact performance of scoring and ranking engine directly
    start_time = time.perf_counter()
    ranked = rank_candidates(order_dict, candidates)
    end_time = time.perf_counter()
    
    elapsed_ms = (end_time - start_time) * 1000

    # Assert
    # We should have all 10,000 candidates scored and returned
    assert len(ranked) == 10000
    
    # Ensure it's ordered by score ascending (lowest score first, as lower is better)
    assert ranked[0]["score"] <= ranked[-1]["score"]
    
    print(f"Scored 10,000 candidates in {elapsed_ms:.2f} ms")
    assert elapsed_ms < 300.0

