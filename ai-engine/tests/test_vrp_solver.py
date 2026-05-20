from app.core.vrp_solver import compute_distance_matrix, solve_vrp
from app.models.routing_models import Location


def test_compute_distance_matrix_is_square_and_zero_diagonal():
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


def test_solve_vrp_returns_route_starting_at_depot():
    locations = [
        Location(id="rider", lat=17.4138, lng=102.7872),
        Location(id="shop", lat=17.4150, lng=102.7900),
        Location(id="customer", lat=17.4185, lng=102.7935),
    ]

    result = solve_vrp(locations=locations, num_vehicles=1, depot=0)

    assert result["status"] == "SUCCESS"
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
