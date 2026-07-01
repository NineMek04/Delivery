from app.core.vrp_solver import (
    HAVERSINE_MATRIX_SOURCE,
    OSRM_MATRIX_SOURCE,
    compute_distance_matrix,
    compute_distance_matrix_with_source,
    solve_vrp,
)
from app.models.routing_models import Location


def test_compute_distance_matrix_is_square_and_zero_diagonal(monkeypatch):
    monkeypatch.setenv("ROUTE_OPTIMIZER_DISABLE_OSRM_TABLE", "true")
    locations = [
        Location(id="depot", lat=17.4138, lng=102.7872),
        Location(id="dropoff", lat=17.4150, lng=102.7900),
        Location(id="shop", lat=17.4100, lng=102.7840),
    ]

    matrix = compute_distance_matrix(locations)

    assert len(matrix) == 3
    assert all(len(row) == 3 for row in matrix)
    assert [matrix[i][i] for i in range(3)] == [0, 0, 0]
    assert matrix[0][1] > 0
    assert matrix[0][1] == matrix[1][0]


def test_compute_distance_matrix_uses_osrm_table(monkeypatch):
    class FakeResponse:
        def raise_for_status(self):
            return None

        def json(self):
            return {
                "code": "Ok",
                "distances": [
                    [0, 111.4, 222.6],
                    [120.2, 0, 150.9],
                    [230.1, 151.1, 0],
                ],
            }

    def fake_get(url, params, timeout):
        assert "/table/v1/driving/" in url
        assert params == {"annotations": "distance,duration"}
        assert timeout == 2.0
        return FakeResponse()

    monkeypatch.delenv("ROUTE_OPTIMIZER_DISABLE_OSRM_TABLE", raising=False)
    monkeypatch.setenv("ROUTE_OPTIMIZER_OSRM_URL", "http://osrm:5000")
    monkeypatch.setattr("app.core.vrp_solver.requests.get", fake_get)

    locations = [
        Location(id="depot", lat=17.4138, lng=102.7872),
        Location(id="dropoff", lat=17.4150, lng=102.7900),
        Location(id="shop", lat=17.4100, lng=102.7840),
    ]

    matrix, source = compute_distance_matrix_with_source(locations)

    assert source == OSRM_MATRIX_SOURCE
    assert matrix == [
        [0, 111, 223],
        [120, 0, 151],
        [230, 151, 0],
    ]


def test_compute_distance_matrix_falls_back_to_haversine(monkeypatch):
    def fake_get(url, params, timeout):
        raise RuntimeError("unexpected")

    monkeypatch.setenv("ROUTE_OPTIMIZER_DISABLE_OSRM_TABLE", "true")
    monkeypatch.setattr("app.core.vrp_solver.requests.get", fake_get)

    locations = [
        Location(id="depot", lat=17.4138, lng=102.7872),
        Location(id="dropoff", lat=17.4150, lng=102.7900),
    ]

    matrix, source = compute_distance_matrix_with_source(locations)

    assert source == HAVERSINE_MATRIX_SOURCE
    assert matrix[0][0] == 0
    assert matrix[0][1] > 0


def test_solve_vrp_returns_route_starting_at_depot(monkeypatch):
    monkeypatch.setenv("ROUTE_OPTIMIZER_DISABLE_OSRM_TABLE", "true")
    locations = [
        Location(id="rider", lat=17.4138, lng=102.7872),
        Location(id="shop", lat=17.4150, lng=102.7900),
        Location(id="customer", lat=17.4185, lng=102.7935),
    ]

    result = solve_vrp(locations=locations, num_vehicles=1, depot=0)

    assert result["status"] == "SUCCESS"
    assert result["matrix_source"] == HAVERSINE_MATRIX_SOURCE
    assert result["total_distance_meters"] > 0
    assert result["optimized_route"][0]["location_id"] == "rider"
    assert {stop["location_id"] for stop in result["optimized_route"]} >= {
        "rider",
        "shop",
        "customer",
    }


def test_solve_vrp_rejects_single_location():
    result = solve_vrp(
        locations=[Location(id="only", lat=17.4138, lng=102.7872)],
        num_vehicles=1,
        depot=0,
    )

    assert result["status"] == "FAILED"


def test_solve_vrp_invalid_depot():
    locations = [
        Location(id="rider", lat=17.4138, lng=102.7872),
        Location(id="shop", lat=17.4150, lng=102.7900),
        Location(id="customer", lat=17.4185, lng=102.7935),
    ]
    # Invalid depot out of bounds
    result = solve_vrp(locations=locations, num_vehicles=1, depot=3)
    assert result["status"] == "FAILED"
    assert "Invalid depot" in result["message"]

    result_neg = solve_vrp(locations=locations, num_vehicles=1, depot=-1)
    assert result_neg["status"] == "FAILED"
    assert "Invalid depot" in result_neg["message"]


def test_solve_vrp_invalid_pickups_deliveries():
    locations = [
        Location(id="rider", lat=17.4138, lng=102.7872),
        Location(id="shop", lat=17.4150, lng=102.7900),
        Location(id="customer", lat=17.4185, lng=102.7935),
    ]
    # Invalid pickups/deliveries index out of bounds
    result = solve_vrp(locations=locations, num_vehicles=1, depot=0, pickups_deliveries=[[1, 3]])
    assert result["status"] == "FAILED"
    assert "Invalid index" in result["message"]

    # Invalid pickups/deliveries format
    result_fmt = solve_vrp(locations=locations, num_vehicles=1, depot=0, pickups_deliveries=[[1]])
    assert result_fmt["status"] == "FAILED"
    assert "Invalid pickups_deliveries format" in result_fmt["message"]
