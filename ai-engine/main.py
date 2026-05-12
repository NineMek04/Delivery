from fastapi import FastAPI

app = FastAPI(
    title="Delivery AI Engine",
    description="AI-Optimized Route Calculation Service (VRP Solver)",
    version="0.1.0",
)


@app.get("/health")
def health_check():
    """Health check endpoint สำหรับ Docker / load balancer"""
    return {"status": "ok", "service": "ai-engine"}


@app.post("/api/solve-vrp")
def solve_vrp():
    """
    Placeholder — รับพิกัดจุดรับ-ส่ง แล้วคำนวณ VRP
    TODO: implement Google OR-Tools VRP solver
    """
    return {"message": "VRP solver not implemented yet"}
