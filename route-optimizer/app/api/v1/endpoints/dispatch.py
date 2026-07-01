from fastapi import APIRouter, Depends, HTTPException
from app.models.dispatch_models import DispatchRankRequest
from app.core.scoring import rank_candidates
from app.core.security import verify_api_key

router = APIRouter()

@router.post("/rank", dependencies=[Depends(verify_api_key)])
def rank_dispatch_candidates(request: DispatchRankRequest):
    """
    Phase A: Receive a list of idle Riders and return them ranked by suitability.
    """
    if len(request.candidates) > 2000:
        raise HTTPException(status_code=422, detail="Too many candidates. Maximum allowed is 2000.")

    if not request.candidates:
        return {"ranked_candidates": []}

    # Convert Pydantic models to dict for the scoring function
    order_dict = request.order.model_dump()
    candidates_list = [c.model_dump() for c in request.candidates]

    ranked = rank_candidates(order_dict, candidates_list)
    
    return {"ranked_candidates": ranked}
