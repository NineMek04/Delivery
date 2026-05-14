from typing import List, Dict, Any
from .geo_utils import haversine_distance, calculate_bearing, is_same_direction

def rank_candidates(order: Dict[str, Any], candidates: List[Dict[str, Any]], max_radius_km: float = 10.0) -> List[Dict[str, Any]]:
    """
    Phase A: Fast Heuristic Scoring
    Scores and ranks candidates based on distance, workload, and direction.
    """
    pickup_lat = order.get("pickup", [0, 0])[0]
    pickup_lng = order.get("pickup", [0, 0])[1]
    dropoff_lat = order.get("dropoff", [0, 0])[0]
    dropoff_lng = order.get("dropoff", [0, 0])[1]

    # Calculate the bearing of the new order
    order_bearing = calculate_bearing(pickup_lat, pickup_lng, dropoff_lat, dropoff_lng)

    ranked = []

    for candidate in candidates:
        rider_lat = candidate.get("lat", 0)
        rider_lng = candidate.get("lng", 0)

        # 1. Distance Filter (Haversine)
        distance_to_pickup = haversine_distance(rider_lat, rider_lng, pickup_lat, pickup_lng)
        
        # If rider is too far, skip them immediately
        if distance_to_pickup > max_radius_km:
            continue

        # 2. Workload Penalty
        current_tasks = candidate.get("current_tasks", [])
        workload_score = len(current_tasks) * 10 # Arbitrary penalty for each task

        # 3. Direction Check (if rider is busy, are they going in the same direction?)
        direction_penalty = 0
        if len(current_tasks) > 0:
            last_task = current_tasks[-1]
            task_lat = last_task.get("loc", [0, 0])[0]
            task_lng = last_task.get("loc", [0, 0])[1]
            
            rider_bearing = calculate_bearing(rider_lat, rider_lng, task_lat, task_lng)
            
            if not is_same_direction(rider_bearing, order_bearing, tolerance_degrees=60):
                direction_penalty = 20 # Penalty for going opposite ways

        # 4. Final Score Calculation (Lower is better)
        # Distance is in km. Let's say 1 km = 5 points.
        distance_score = distance_to_pickup * 5
        
        total_score = distance_score + workload_score + direction_penalty

        # Estimate ETA (assume 30 km/h average city speed -> 0.5 km/min)
        eta_minutes = int(distance_to_pickup / 0.5)

        ranked.append({
            "rider_id": candidate["rider_id"],
            "score": total_score,
            "distance_km": round(distance_to_pickup, 2),
            "eta_minutes": eta_minutes,
            "breakdown": {
                "distance_score": round(distance_score, 2),
                "workload_penalty": workload_score,
                "direction_penalty": direction_penalty
            }
        })

    # Sort by lowest score
    ranked.sort(key=lambda x: x["score"])
    return ranked
