from fastapi import FastAPI, Depends
from app.api.v1.api import v1_router
from app.api.v1.endpoints import optimize
from app.core.security import verify_api_key

# Initialize FastAPI App
app = FastAPI(
    title="AI Delivery Routing Optimization API",
    description="AI-Optimized Route Calculation Service (VRP Solver) and Real-time Dispatch Scorer",
    version="0.2.1",
)

# Register API Routers
# /api/v1/dispatch/rank (protected by API key)
app.include_router(v1_router, prefix="/api/v1", dependencies=[Depends(verify_api_key)])

# /api/optimize-route (protected by API key)
app.include_router(optimize.router, prefix="/api", tags=["routing"], dependencies=[Depends(verify_api_key)])

@app.get("/health")
def health_check():
    """Health check endpoint for Docker / load balancer"""
    return {
        "status": "ok", 
        "service": "ai-engine",
        "version": app.version
    }

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True)
