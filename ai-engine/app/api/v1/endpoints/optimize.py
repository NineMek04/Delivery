from fastapi import APIRouter, HTTPException, Depends
from app.models.routing_models import RoutingRequest
from app.core.vrp_solver import solve_vrp
from app.core.security import verify_api_key

router = APIRouter()

@router.post("/optimize-route", dependencies=[Depends(verify_api_key)])
async def optimize_route(request: RoutingRequest):
    """
    VRP Optimization endpoint. Calculates the most efficient route sequence.
    """
    if len(request.locations) < 2:
        raise HTTPException(status_code=400, detail="ต้องมีจุดพิกัดอย่างน้อย 2 จุดขึ้นไป")
    if len(request.locations) > 100:
        raise HTTPException(status_code=400, detail="รองรับพิกัดสูงสุด 100 จุดต่อ VRP request")

    result = solve_vrp(request.locations, request.num_vehicles, request.depot, request.pickups_deliveries)
    
    if result["status"] == "FAILED":
        return result # Or raise HTTPException
        
    return result
