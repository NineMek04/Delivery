from fastapi.testclient import TestClient

from main import app


client = TestClient(app)


def test_health_endpoint_returns_versioned_status():
    response = client.get("/health")

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "ok"
    assert body["service"] == "route-optimizer"
    assert body["version"]


def test_optimize_route_endpoint_returns_successful_route(monkeypatch):
    monkeypatch.setenv("ROUTE_OPTIMIZER_DISABLE_OSRM_TABLE", "true")
    response = client.post(
        "/api/optimize-route",
        json={
            "locations": [
                {"id": "rider", "lat": 17.4138, "lng": 102.7872},
                {"id": "shop", "lat": 17.4150, "lng": 102.7900},
                {"id": "customer", "lat": 17.4185, "lng": 102.7935},
            ],
            "num_vehicles": 1,
            "depot": 0,
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "SUCCESS"
    assert body["matrix_source"] == "HAVERSINE_FALLBACK"
    assert body["optimized_route"][0]["location_id"] == "rider"


def test_optimize_route_endpoint_rejects_too_few_locations():
    response = client.post(
        "/api/optimize-route",
        json={
            "locations": [{"id": "rider", "lat": 17.4138, "lng": 102.7872}],
            "num_vehicles": 1,
            "depot": 0,
        },
    )

    assert response.status_code == 400
