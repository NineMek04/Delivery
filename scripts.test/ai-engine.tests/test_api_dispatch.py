from fastapi.testclient import TestClient

from main import app


client = TestClient(app)


def test_dispatch_rank_endpoint_orders_nearest_idle_rider_first():
    response = client.post(
        "/api/v1/dispatch/rank",
        json={
            "context": {"timestamp": "2026-05-20T10:00:00Z", "city": "Udon Thani"},
            "order": {
                "id": "ORD-TEST-001",
                "pickup": [17.4138, 102.7872],
                "dropoff": [17.4200, 102.7950],
                "sla_limit_minutes": 30,
            },
            "candidates": [
                {
                    "rider_id": "far-rider",
                    "lat": 17.4300,
                    "lng": 102.8100,
                    "current_tasks": [],
                },
                {
                    "rider_id": "near-rider",
                    "lat": 17.4140,
                    "lng": 102.7874,
                    "current_tasks": [],
                },
            ],
        },
    )

    assert response.status_code == 200
    ranked = response.json()["ranked_candidates"]
    assert ranked[0]["rider_id"] == "near-rider"
    assert ranked[0]["distance_to_pickup_km"] < ranked[-1]["distance_to_pickup_km"]


def test_dispatch_rank_endpoint_filters_candidates_outside_radius():
    response = client.post(
        "/api/v1/dispatch/rank",
        json={
            "context": {"timestamp": "2026-05-20T10:00:00Z", "city": "Udon Thani"},
            "order": {
                "id": "ORD-TEST-002",
                "pickup": [17.4138, 102.7872],
                "dropoff": [17.4200, 102.7950],
                "sla_limit_minutes": 30,
            },
            "candidates": [
                {
                    "rider_id": "outside-radius",
                    "lat": 18.7883,
                    "lng": 98.9853,
                    "current_tasks": [],
                }
            ],
        },
    )

    assert response.status_code == 200
    assert response.json()["ranked_candidates"] == []
