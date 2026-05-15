from pydantic import BaseModel
from typing import List

# --- 1. กำหนด Data Models (Schema) สำหรับรับข้อมูลจาก .NET ---
class Location(BaseModel):
    id: str
    lat: float
    lng: float

class RoutingRequest(BaseModel):
    locations: List[Location]  # จุดแรก [0] คือตำแหน่งไรเดอร์/ร้านค้า จุดต่อไปคือลูกค้า
    num_vehicles: int = 1      # จำนวนรถที่ใช้ (เริ่มต้นที่ 1 คันสำหรับออเดอร์พ่วง)
    depot: int = 0             # จุดเริ่มต้น (index 0)
