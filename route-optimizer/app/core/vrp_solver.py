import os
from typing import List, Dict, Any, Tuple
import requests
from ortools.constraint_solver import routing_enums_pb2
from ortools.constraint_solver import pywrapcp
from app.models.routing_models import Location
from app.core.geo_utils import haversine_distance

OSRM_MATRIX_SOURCE = "LOCAL_OSRM_TABLE"
HAVERSINE_MATRIX_SOURCE = "HAVERSINE_FALLBACK"


def compute_distance_matrix(locations: List[Location]) -> List[List[int]]:
    """
    Compute the VRP cost matrix.

    Primary source: local OSRM /table road-network distance matrix.
    Fallback source: local deterministic Haversine matrix when OSRM is
    unavailable, incomplete, or returns invalid values.
    """
    matrix, _ = compute_distance_matrix_with_source(locations)
    return matrix


def compute_distance_matrix_with_source(locations: List[Location]) -> Tuple[List[List[int]], str]:
    osrm_matrix = _try_compute_osrm_distance_matrix(locations)
    if osrm_matrix is not None:
        return osrm_matrix, OSRM_MATRIX_SOURCE

    return _compute_haversine_distance_matrix(locations), HAVERSINE_MATRIX_SOURCE


def _compute_haversine_distance_matrix(locations: List[Location]) -> List[List[int]]:
    matrix = []
    for from_node in locations:
        row = []
        for to_node in locations:
            # Distance in meters using Haversine
            dist = int(haversine_distance(from_node.lat, from_node.lng, to_node.lat, to_node.lng) * 1000)
            row.append(dist)
        matrix.append(row)
    return matrix


def _try_compute_osrm_distance_matrix(locations: List[Location]) -> List[List[int]] | None:
    if len(locations) == 0:
        return []

    if os.getenv("ROUTE_OPTIMIZER_DISABLE_OSRM_TABLE", "false").lower() == "true":
        return None

    base_url = (
        os.getenv("ROUTE_OPTIMIZER_OSRM_URL")
        or os.getenv("OSRM_URL")
        or "http://osrm:5000"
    ).rstrip("/")
    try:
        timeout_seconds = float(os.getenv("ROUTE_OPTIMIZER_OSRM_TIMEOUT_SECONDS", "2.0"))
    except ValueError:
        timeout_seconds = 2.0

    coordinates = ";".join(f"{location.lng},{location.lat}" for location in locations)
    url = f"{base_url}/table/v1/driving/{coordinates}"
    params = {"annotations": "distance,duration"}

    try:
        response = requests.get(url, params=params, timeout=timeout_seconds)
        response.raise_for_status()
        payload = response.json()
    except (requests.RequestException, ValueError):
        return None

    if payload.get("code") != "Ok":
        return None

    distances = payload.get("distances")
    if not _is_valid_osrm_matrix(distances, len(locations)):
        return None

    return [
        [0 if row_index == col_index else max(0, int(round(value))) for col_index, value in enumerate(row)]
        for row_index, row in enumerate(distances)
    ]


def _is_valid_osrm_matrix(matrix: Any, size: int) -> bool:
    if not isinstance(matrix, list) or len(matrix) != size:
        return False

    for row in matrix:
        if not isinstance(row, list) or len(row) != size:
            return False
        for value in row:
            if not isinstance(value, (int, float)) or value < 0:
                return False

    return True


def solve_vrp(locations: List[Location], num_vehicles: int, depot: int, pickups_deliveries: List[List[int]] = None) -> Dict[str, Any]:
    """
    Solves the Vehicle Routing Problem using Google OR-Tools.
    OR-Tools is mathematical optimization, not a trained AI/ML model.
    """
    if len(locations) < 2:
        return {"status": "FAILED", "message": "ต้องมีจุดพิกัดอย่างน้อย 2 จุดขึ้นไป"}

    if depot < 0 or depot >= len(locations):
        return {"status": "FAILED", "message": "Invalid depot parameter"}

    if pickups_deliveries:
        for pair in pickups_deliveries:
            if len(pair) != 2:
                return {"status": "FAILED", "message": "Invalid pickups_deliveries format"}
            if not (0 <= pair[0] < len(locations)) or not (0 <= pair[1] < len(locations)):
                return {"status": "FAILED", "message": "Invalid index in pickups_deliveries parameters"}

    # 1. Create Distance Matrix
    distance_matrix, matrix_source = compute_distance_matrix_with_source(locations)

    # 2. Setup Data Model
    data = {
        'distance_matrix': distance_matrix,
        'num_vehicles': num_vehicles,
        'depot': depot,
        'pickups_deliveries': pickups_deliveries or []
    }

    # 3. Create Routing Index Manager and Routing Model
    manager = pywrapcp.RoutingIndexManager(len(data['distance_matrix']), data['num_vehicles'], data['depot'])
    routing = pywrapcp.RoutingModel(manager)

    # 4. Define distance callback
    def distance_callback(from_index, to_index):
        from_node = manager.IndexToNode(from_index)
        to_node = manager.IndexToNode(to_index)
        return data['distance_matrix'][from_node][to_node]

    transit_callback_index = routing.RegisterTransitCallback(distance_callback)
    routing.SetArcCostEvaluatorOfAllVehicles(transit_callback_index)

    # Add Distance dimension
    dimension_name = 'Distance'
    routing.AddDimension(
        transit_callback_index,
        0,  # no slack
        3000000,  # vehicle maximum travel distance
        True,  # start cumul to zero
        dimension_name)
    distance_dimension = routing.GetDimensionOrDie(dimension_name)

    # Define Transportation Requests (Pickup & Delivery)
    for request_pair in data['pickups_deliveries']:
        if len(request_pair) == 2:
            pickup_index = manager.NodeToIndex(request_pair[0])
            delivery_index = manager.NodeToIndex(request_pair[1])
            routing.AddPickupAndDelivery(pickup_index, delivery_index)
            routing.solver().Add(
                routing.VehicleVar(pickup_index) == routing.VehicleVar(delivery_index))
            routing.solver().Add(
                distance_dimension.CumulVar(pickup_index) <= distance_dimension.CumulVar(delivery_index))

    # 5. Set search parameters
    search_parameters = pywrapcp.DefaultRoutingSearchParameters()
    search_parameters.first_solution_strategy = (routing_enums_pb2.FirstSolutionStrategy.PATH_CHEAPEST_ARC)
    # Set a 5-second search time limit to prevent Denial of Service (OWASP LLM04 Model DoS)
    search_parameters.time_limit.seconds = 5

    # 6. Solve
    solution = routing.SolveWithParameters(search_parameters)

    # 7. Format results
    if solution:
        route_sequence = []
        total_distance = 0
        
        for vehicle_id in range(num_vehicles):
            index = routing.Start(vehicle_id)
            
            # Skip empty vehicle routes (directly goes to end depot)
            if routing.IsEnd(solution.Value(routing.NextVar(index))):
                continue
                
            while not routing.IsEnd(index):
                node_index = manager.IndexToNode(index)
                route_sequence.append({
                    "sequence": len(route_sequence) + 1,
                    "location_id": locations[node_index].id,
                    "lat": locations[node_index].lat,
                    "lng": locations[node_index].lng,
                    "vehicle_id": vehicle_id
                })
                previous_index = index
                index = solution.Value(routing.NextVar(index))
                total_distance += routing.GetArcCostForVehicle(previous_index, index, vehicle_id)

            # Add last node
            node_index = manager.IndexToNode(index)
            route_sequence.append({
                "sequence": len(route_sequence) + 1,
                "location_id": locations[node_index].id,
                "lat": locations[node_index].lat,
                "lng": locations[node_index].lng,
                "vehicle_id": vehicle_id
            })

        return {
            "status": "SUCCESS",
            "matrix_source": matrix_source,
            "total_distance_meters": total_distance,
            "optimized_route": route_sequence
        }
    else:
        return {"status": "FAILED", "message": "ไม่สามารถหาเส้นทางได้"}
