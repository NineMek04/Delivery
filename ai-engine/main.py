from fastapi import FastAPI
from app.api.v1.api import v1_router
from app.api.v1.endpoints import optimize

# Initialize FastAPI App
app = FastAPI(
    title="AI Delivery Routing Optimization API",
    description="AI-Optimized Route Calculation Service (VRP Solver) and Real-time Dispatch Scorer",
    version="0.2.1",
)

# Register API Routers
# /api/v1/dispatch/rank
app.include_router(v1_router, prefix="/api/v1")

# /api/optimize-route
app.include_router(optimize.router, prefix="/api", tags=["routing"])

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
