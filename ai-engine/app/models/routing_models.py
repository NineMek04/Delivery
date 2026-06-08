from pydantic import BaseModel, Field, ConfigDict
from typing import List

# --- 1. กำหนด Data Models (Schema) สำหรับรับข้อมูลจาก .NET ---
class Location(BaseModel):
    model_config = ConfigDict(extra="forbid")
    
    id: str
    lat: float = Field(ge=-90.0, le=90.0)
    lng: float = Field(ge=-180.0, le=180.0)

class RoutingRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    
    locations: List[Location] = Field(max_length=100)  # จุดแรก [0] คือตำแหน่งไรเดอร์/ร้านค้า จุดต่อไปคือลูกค้า
    num_vehicles: int = Field(default=1, ge=1, le=50)      # จำนวนรถที่ใช้ (เริ่มต้นที่ 1 คันสำหรับออเดอร์พ่วง)
    depot: int = Field(default=0, ge=0)             # จุดเริ่มต้น (index 0)
    pickups_deliveries: List[List[int]] = [] # คู่ดัชนีจุดรับ-จุดส่ง เช่น [[1, 2]] หมายถึงรับที่ 1 ส่งที่ 2

