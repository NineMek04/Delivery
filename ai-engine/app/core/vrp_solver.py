import math
from typing import List, Dict, Any
from ortools.constraint_solver import routing_enums_pb2
from ortools.constraint_solver import pywrapcp
from app.models.routing_models import Location
from app.core.geo_utils import haversine_distance

def compute_distance_matrix(locations: List[Location]) -> List[List[int]]:
    """
    Compute a simple Haversine distance matrix.
    In production, this should be replaced by a routing engine like OSRM or Google Maps.
    """
    matrix = []
    for from_node in locations:
        row = []
        for to_node in locations:
            # Distance in meters using Haversine
            dist = int(haversine_distance(from_node.lat, from_node.lng, to_node.lat, to_node.lng) * 1000)
            row.append(dist)
        matrix.append(row)
    return matrix

def solve_vrp(locations: List[Location], num_vehicles: int, depot: int, pickups_deliveries: List[List[int]] = None) -> Dict[str, Any]:
    """
    Solves the Vehicle Routing Problem using Google OR-Tools.
    """
    if len(locations) < 2:
        return {"status": "FAILED", "message": "ต้องมีจุดพิกัดอย่างน้อย 2 จุดขึ้นไป"}

    # 1. Create Distance Matrix
    distance_matrix = compute_distance_matrix(locations)

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
        index = routing.Start(0) # Start with vehicle 0
        total_distance = 0
        
        while not routing.IsEnd(index):
            node_index = manager.IndexToNode(index)
            route_sequence.append({
                "sequence": len(route_sequence) + 1,
                "location_id": locations[node_index].id,
                "lat": locations[node_index].lat,
                "lng": locations[node_index].lng
            })
            previous_index = index
            index = solution.Value(routing.NextVar(index))
            total_distance += routing.GetArcCostForVehicle(previous_index, index, 0)

        # Add last node
        node_index = manager.IndexToNode(index)
        route_sequence.append({
            "sequence": len(route_sequence) + 1,
            "location_id": locations[node_index].id,
            "lat": locations[node_index].lat,
            "lng": locations[node_index].lng
        })

        return {
            "status": "SUCCESS",
            "total_distance_meters": total_distance,
            "optimized_route": route_sequence
        }
    else:
        return {"status": "FAILED", "message": "ไม่สามารถหาเส้นทางได้"}
