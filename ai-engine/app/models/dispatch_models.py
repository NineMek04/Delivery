from pydantic import BaseModel, Field, ConfigDict
from typing import List, Dict, Any, Tuple

# --- Models สำหรับ Dispatch Ranking ---
class DispatchContext(BaseModel):
    model_config = ConfigDict(extra="forbid")
    
    timestamp: str
    city: str = ""

class DispatchOrder(BaseModel):
    model_config = ConfigDict(extra="forbid")
    
    id: str
    pickup: Tuple[float, float]  # [lat, lng]
    dropoff: Tuple[float, float] # [lat, lng]
    sla_limit_minutes: int = Field(default=30, ge=1, le=1440)

class DispatchCandidate(BaseModel):
    model_config = ConfigDict(extra="forbid")
    
    rider_id: str
    lat: float = Field(ge=-90.0, le=90.0)
    lng: float = Field(ge=-180.0, le=180.0)
    speed_kmh: float = Field(default=20.0, ge=0.0, le=150.0)  # ค่าเริ่มต้น 20 km/h ถ้าไม่มีข้อมูล
    current_tasks: List[Dict[str, Any]] = []

class DispatchRankRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    
    context: DispatchContext
    order: DispatchOrder
    candidates: List[DispatchCandidate] = Field(max_length=200)

