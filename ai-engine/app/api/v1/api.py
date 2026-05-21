from fastapi import APIRouter
from app.api.v1.endpoints import dispatch, predict

v1_router = APIRouter()

# Register v1 endpoints
v1_router.include_router(dispatch.router, prefix="/dispatch", tags=["dispatch"])
v1_router.include_router(predict.router, prefix="", tags=["prediction"])
