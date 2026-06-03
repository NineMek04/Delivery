from fastapi import APIRouter, HTTPException
from app.models.routing_models import RoutingRequest
from app.core.vrp_solver import solve_vrp

router = APIRouter()

@router.post("/optimize-route")
async def optimize_route(request: RoutingRequest):
    """
    VRP Optimization endpoint. Calculates the most efficient route sequence.
    """
    if len(request.locations) < 2:
        raise HTTPException(status_code=400, detail="ต้องมีจุดพิกัดอย่างน้อย 2 จุดขึ้นไป")

    result = solve_vrp(request.locations, request.num_vehicles, request.depot)
    
    if result["status"] == "FAILED":
        return result # Or raise HTTPException
        
    return result
