from fastapi import APIRouter
from app.models.dispatch_models import DispatchRankRequest
from app.core.scoring import rank_candidates

router = APIRouter()

@router.post("/rank")
async def rank_dispatch_candidates(request: DispatchRankRequest):
    """
    Phase A: Receive a list of idle Riders and return them ranked by suitability.
    """
    if not request.candidates:
        return {"ranked_candidates": []}

    # Convert Pydantic models to dict for the scoring function
    order_dict = request.order.model_dump()
    candidates_list = [c.model_dump() for c in request.candidates]

    ranked = rank_candidates(order_dict, candidates_list)
    
    return {"ranked_candidates": ranked}
