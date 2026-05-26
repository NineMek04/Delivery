"""
Test: OSRM + Rider Velocity → ETA Calculation
ทดสอบ AI Engine predict-eta endpoint ที่ปรับปรุงแล้ว
รันจาก root: python scripts/test_eta_velocity.py
"""
import sys
import json
import httpx
from datetime import datetime, timezone

AI_ENGINE_URL = "http://localhost:8000"

def test_predict_eta_with_defaults():
    """ทดสอบ predict-eta โดยไม่ส่ง rider_speed_kmh → ใช้ fallback ค่าเดิม"""
    print("\n=== Test 1: predict-eta with defaults (no rider speed) ===")
    payload = {
        "pickup_lat": 17.4138,
        "pickup_lng": 102.7870,
        "dropoff_lat": 17.4250,
        "dropoff_lng": 102.7950,
        "route_distance_meters": 3500,
        "route_duration_seconds": 420,
        "current_time": datetime.now(timezone.utc).isoformat(),
        "weather_condition": "clear",
        "traffic_level": "normal"
    }

    resp = httpx.post(f"{AI_ENGINE_URL}/api/v1/predict-eta", json=payload, timeout=10)
    assert resp.status_code == 200, f"Expected 200, got {resp.status_code}: {resp.text}"
    data = resp.json()

    print(f"  ETA: {data['eta_minutes']} minutes")
    print(f"  Confidence: {data['confidence']}")
    print(f"  Factors: {json.dumps(data['factors'], indent=2)}")

    assert data["eta_minutes"] > 0
    assert data["confidence"] > 0
    assert data["factors"]["velocity_factor"] == 1.0, "velocity_factor should be 1.0 when no rider speed"
    assert data["factors"]["dispatch_pickup_mins"] == 10.0, "dispatch_pickup should be 10.0 fallback"
    print("  ✅ PASSED")
    return data


def test_predict_eta_with_rider_speed():
    """ทดสอบ predict-eta พร้อม rider_speed_kmh → velocity adjustment"""
    print("\n=== Test 2: predict-eta with rider speed 25 km/h ===")
    payload = {
        "pickup_lat": 17.4138,
        "pickup_lng": 102.7870,
        "dropoff_lat": 17.4250,
        "dropoff_lng": 102.7950,
        "route_distance_meters": 3500,
        "route_duration_seconds": 420,
        "current_time": datetime.now(timezone.utc).isoformat(),
        "weather_condition": "clear",
        "traffic_level": "normal",
        "rider_speed_kmh": 25.0
    }

    resp = httpx.post(f"{AI_ENGINE_URL}/api/v1/predict-eta", json=payload, timeout=10)
    assert resp.status_code == 200, f"Expected 200, got {resp.status_code}: {resp.text}"
    data = resp.json()

    print(f"  ETA: {data['eta_minutes']} minutes")
    print(f"  Velocity Factor: {data['factors']['velocity_factor']}")
    print(f"  Rider Speed: {data['factors']['rider_speed_kmh']} km/h")

    assert data["factors"]["velocity_factor"] != 1.0, "velocity_factor should not be 1.0 when rider speed provided"
    assert data["factors"]["rider_speed_kmh"] == 25.0
    print("  ✅ PASSED")
    return data


def test_predict_eta_with_osrm_pickup():
    """ทดสอบ predict-eta พร้อม osrm_pickup_duration_seconds → แทน 10 นาทีคงที่"""
    print("\n=== Test 3: predict-eta with OSRM pickup duration ===")
    payload = {
        "pickup_lat": 17.4138,
        "pickup_lng": 102.7870,
        "dropoff_lat": 17.4250,
        "dropoff_lng": 102.7950,
        "route_distance_meters": 3500,
        "route_duration_seconds": 420,
        "current_time": datetime.now(timezone.utc).isoformat(),
        "weather_condition": "clear",
        "traffic_level": "normal",
        "rider_speed_kmh": 30.0,
        "osrm_pickup_duration_seconds": 180  # 3 นาที
    }

    resp = httpx.post(f"{AI_ENGINE_URL}/api/v1/predict-eta", json=payload, timeout=10)
    assert resp.status_code == 200, f"Expected 200, got {resp.status_code}: {resp.text}"
    data = resp.json()

    print(f"  ETA: {data['eta_minutes']} minutes")
    print(f"  Dispatch+Pickup: {data['factors']['dispatch_pickup_mins']} mins")
    print(f"  OSRM Pickup Duration: {data['factors']['osrm_pickup_duration_seconds']}s")

    # dispatch_pickup = 180 + 120 = 300s = 5 mins (ไม่ใช่ 10 นาทีคงที่)
    assert data["factors"]["dispatch_pickup_mins"] == 5.0, f"Expected 5.0, got {data['factors']['dispatch_pickup_mins']}"
    assert data["factors"]["osrm_pickup_duration_seconds"] == 180
    print("  ✅ PASSED")
    return data


def test_predict_eta_slow_rider():
    """ทดสอบ rider ที่ช้ามาก (10 km/h) → ETA ต้องมากกว่า rider ปกติ"""
    print("\n=== Test 4: slow rider (10 km/h) vs normal rider (30 km/h) ===")

    base_payload = {
        "pickup_lat": 17.4138,
        "pickup_lng": 102.7870,
        "dropoff_lat": 17.4250,
        "dropoff_lng": 102.7950,
        "route_distance_meters": 5000,
        "route_duration_seconds": 600,
        "current_time": datetime.now(timezone.utc).isoformat(),
        "weather_condition": "clear",
        "traffic_level": "normal",
        "osrm_pickup_duration_seconds": 300
    }

    # Slow rider
    slow_payload = {**base_payload, "rider_speed_kmh": 10.0}
    resp_slow = httpx.post(f"{AI_ENGINE_URL}/api/v1/predict-eta", json=slow_payload, timeout=10)
    data_slow = resp_slow.json()

    # Fast rider
    fast_payload = {**base_payload, "rider_speed_kmh": 30.0}
    resp_fast = httpx.post(f"{AI_ENGINE_URL}/api/v1/predict-eta", json=fast_payload, timeout=10)
    data_fast = resp_fast.json()

    print(f"  Slow rider ETA: {data_slow['eta_minutes']} mins (velocity_factor: {data_slow['factors']['velocity_factor']})")
    print(f"  Fast rider ETA: {data_fast['eta_minutes']} mins (velocity_factor: {data_fast['factors']['velocity_factor']})")

    assert data_slow["eta_minutes"] > data_fast["eta_minutes"], \
        f"Slow rider ETA ({data_slow['eta_minutes']}) should be > fast rider ETA ({data_fast['eta_minutes']})"
    print("  ✅ PASSED")


def test_predict_eta_weather_and_traffic():
    """ทดสอบ ETA ในสภาพอากาศเลวร้าย + จราจรหนาแน่น"""
    print("\n=== Test 5: bad weather + heavy traffic ===")
    payload = {
        "pickup_lat": 17.4138,
        "pickup_lng": 102.7870,
        "dropoff_lat": 17.4250,
        "dropoff_lng": 102.7950,
        "route_distance_meters": 5000,
        "route_duration_seconds": 600,
        "current_time": datetime.now(timezone.utc).isoformat(),
        "weather_condition": "storm",
        "traffic_level": "heavy",
        "rider_speed_kmh": 20.0,
        "osrm_pickup_duration_seconds": 300
    }

    resp = httpx.post(f"{AI_ENGINE_URL}/api/v1/predict-eta", json=payload, timeout=10)
    assert resp.status_code == 200
    data = resp.json()

    print(f"  ETA: {data['eta_minutes']} mins")
    print(f"  Confidence: {data['confidence']}")
    print(f"  Weather multiplier: {data['factors']['weather_multiplier']}")
    print(f"  Traffic multiplier: {data['factors']['traffic_multiplier']}")

    assert data["factors"]["weather_multiplier"] == 1.8
    assert data["factors"]["traffic_multiplier"] == 1.5
    assert data["confidence"] < 0.8, "Confidence should be low in bad conditions"
    print("  ✅ PASSED")


def test_dispatch_rank_with_speed():
    """ทดสอบ dispatch/rank endpoint ที่รับ speed_kmh"""
    print("\n=== Test 6: dispatch/rank with rider speed_kmh ===")
    payload = {
        "context": {"timestamp": datetime.now(timezone.utc).isoformat(), "city": "UdonThani"},
        "order": {
            "id": "test-order-001",
            "pickup": [17.4138, 102.7870],
            "dropoff": [17.4250, 102.7950],
            "sla_limit_minutes": 30
        },
        "candidates": [
            {"rider_id": "rider-fast", "lat": 17.4150, "lng": 102.7880, "speed_kmh": 35.0, "current_tasks": []},
            {"rider_id": "rider-slow", "lat": 17.4150, "lng": 102.7880, "speed_kmh": 10.0, "current_tasks": []},
            {"rider_id": "rider-default", "lat": 17.4150, "lng": 102.7880, "current_tasks": []}
        ]
    }

    resp = httpx.post(f"{AI_ENGINE_URL}/api/v1/dispatch/rank", json=payload, timeout=10)
    assert resp.status_code == 200, f"Expected 200, got {resp.status_code}: {resp.text}"
    data = resp.json()

    ranked = data["ranked_candidates"]
    print(f"  Ranked candidates: {len(ranked)}")
    for r in ranked:
        print(f"    Rider: {r['rider_id']}, ETA: {r['eta_minutes']} mins, Score: {r['score']}")

    # Fast rider ควรมี ETA น้อยกว่า slow rider
    fast_rider = next(r for r in ranked if r["rider_id"] == "rider-fast")
    slow_rider = next(r for r in ranked if r["rider_id"] == "rider-slow")
    assert fast_rider["eta_minutes"] <= slow_rider["eta_minutes"], \
        f"Fast rider ETA ({fast_rider['eta_minutes']}) should be <= slow rider ETA ({slow_rider['eta_minutes']})"
    print("  ✅ PASSED")


if __name__ == "__main__":
    print("=" * 60)
    print("🏍️ ETA Velocity Integration Tests")
    print(f"Target: {AI_ENGINE_URL}")
    print("=" * 60)

    tests = [
        test_predict_eta_with_defaults,
        test_predict_eta_with_rider_speed,
        test_predict_eta_with_osrm_pickup,
        test_predict_eta_slow_rider,
        test_predict_eta_weather_and_traffic,
        test_dispatch_rank_with_speed,
    ]

    passed = 0
    failed = 0

    for test_fn in tests:
        try:
            test_fn()
            passed += 1
        except Exception as e:
            print(f"  ❌ FAILED: {e}")
            failed += 1

    print("\n" + "=" * 60)
    print(f"Results: {passed} passed, {failed} failed out of {len(tests)} tests")
    print("=" * 60)
    sys.exit(1 if failed > 0 else 0)
