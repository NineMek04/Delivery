from pydantic import BaseModel
from typing import List, Dict, Any

# --- Models สำหรับ Dispatch Ranking ---
class DispatchContext(BaseModel):
    timestamp: str
    city: str = ""

class DispatchOrder(BaseModel):
    id: str
    pickup: List[float]  # [lat, lng]
    dropoff: List[float] # [lat, lng]
    sla_limit_minutes: int = 30

class DispatchCandidate(BaseModel):
    rider_id: str
    lat: float
    lng: float
    speed_kmh: float = 20.0  # ค่าเริ่มต้น 20 km/h ถ้าไม่มีข้อมูล
    current_tasks: List[Dict[str, Any]] = []

class DispatchRankRequest(BaseModel):
    context: DispatchContext
    order: DispatchOrder
    candidates: List[DispatchCandidate]
