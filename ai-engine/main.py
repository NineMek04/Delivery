from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Dict, Any
import math
from ortools.constraint_solver import routing_enums_pb2
from ortools.constraint_solver import pywrapcp

# --- 1. กำหนด Data Models (Schema) สำหรับรับข้อมูลจาก .NET ---
class Location(BaseModel):
    id: str
    lat: float
    lng: float

class RoutingRequest(BaseModel):
    locations: List[Location]  # จุดแรก [0] คือตำแหน่งไรเดอร์/ร้านค้า จุดต่อไปคือลูกค้า
    num_vehicles: int = 1      # จำนวนรถที่ใช้ (เริ่มต้นที่ 1 คันสำหรับออเดอร์พ่วง)
    depot: int = 0             # จุดเริ่มต้น (index 0)

# สร้างแอป FastAPI
app = FastAPI(title="AI Delivery Routing Optimization API")

# --- 2. ฟังก์ชันจำลองการหาระยะทาง (Distance Matrix) ---
def compute_distance_matrix(locations: List[Location]):
    matrix = []
    for from_node in locations:
        row = []
        for to_node in locations:
            # คำนวณระยะทางแบบเส้นตรง (แปลงหน่วย Lat/Lng เป็นเมตรแบบคร่าวๆ)
            # ของจริงควรใช้ OSRM หรือ Google Maps API
            dist = math.hypot(from_node.lat - to_node.lat, from_node.lng - to_node.lng) * 111000
            row.append(int(dist))
        matrix.append(row)
    return matrix

# --- 3. ฟังก์ชันหลักสำหรับแก้ปัญหา VRP ด้วย OR-Tools ---
@app.post("/api/optimize-route")
async def optimize_route(request: RoutingRequest):
    if len(request.locations) < 2:
        raise HTTPException(status_code=400, detail="ต้องมีจุดพิกัดอย่างน้อย 2 จุดขึ้นไป")

    # 1. สร้าง Distance Matrix
    distance_matrix = compute_distance_matrix(request.locations)

    # 2. ตั้งค่า Data Model ให้ OR-Tools
    data = {
        'distance_matrix': distance_matrix,
        'num_vehicles': request.num_vehicles,
        'depot': request.depot
    }

    # 3. สร้าง Routing Index Manager และ Routing Model
    manager = pywrapcp.RoutingIndexManager(len(data['distance_matrix']), data['num_vehicles'], data['depot'])
    routing = pywrapcp.RoutingModel(manager)

    # 4. ฟังก์ชันดึงระยะทางให้ OR-Tools ใช้คำนวณ
    def distance_callback(from_index, to_index):
        from_node = manager.IndexToNode(from_index)
        to_node = manager.IndexToNode(to_index)
        return data['distance_matrix'][from_node][to_node]

    transit_callback_index = routing.RegisterTransitCallback(distance_callback)
    routing.SetArcCostEvaluatorOfAllVehicles(transit_callback_index)

    # 5. ตั้งค่าพารามิเตอร์การค้นหา (Heuristics)
    search_parameters = pywrapcp.DefaultRoutingSearchParameters()
    search_parameters.first_solution_strategy = (routing_enums_pb2.FirstSolutionStrategy.PATH_CHEAPEST_ARC)

    # 6. รันอัลกอริทึมแก้สมการ
    solution = routing.SolveWithParameters(search_parameters)

    # 7. จัดรูปแบบผลลัพธ์ (Route Sequence) กลับไปให้ .NET
    if solution:
        route_sequence = []
        index = routing.Start(0) # เริ่มจากรถคันที่ 0
        total_distance = 0
        
        while not routing.IsEnd(index):
            node_index = manager.IndexToNode(index)
            route_sequence.append({
                "sequence": len(route_sequence) + 1,
                "location_id": request.locations[node_index].id,
                "lat": request.locations[node_index].lat,
                "lng": request.locations[node_index].lng
            })
            previous_index = index
            index = solution.Value(routing.NextVar(index))
            total_distance += routing.GetArcCostForVehicle(previous_index, index, 0)

        # เพิ่มจุดสุดท้าย (กลับมาที่เดิม หรือจุดจบ)
        node_index = manager.IndexToNode(index)
        route_sequence.append({
            "sequence": len(route_sequence) + 1,
            "location_id": request.locations[node_index].id,
            "lat": request.locations[node_index].lat,
            "lng": request.locations[node_index].lng
        })

        return {
            "status": "SUCCESS",
            "total_distance_meters": total_distance,
            "optimized_route": route_sequence
        }
    else:
        return {"status": "FAILED", "message": "ไม่สามารถหาเส้นทางได้"}