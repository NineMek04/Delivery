# AI Routing Engine (Python FastAPI)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือการพัฒนาสำหรับทีม **AI / Data Engineer (Python)** อธิบายโครงสร้าง อัลกอริทึมการแก้ปัญหา VRP (Vehicle Routing Problem) และการวิเคราะห์คำนวณตำแหน่งระยะทาง

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
AI Routing Engine ทำหน้าที่เป็นผู้ช่วยอัจฉริยะประมวลผลอัลกอริทึม (Computation Engine) ให้กับระบบหลังบ้าน:
1.  **VRP Optimization:** จัดลำดับและจับคู่การรับส่งของ Rider หลายจุดแวะ (Multi-Drop Routes) ด้วยข้อจำกัดด้านความจุและเวลา
2.  **Rider Scoring & Ranking:** จัดลำดับความเหมาะสมของพนักงานขับรถรอบตัวร้านค้า โดยประเมินจากระยะทางจริง ทิศทางการวิ่ง และระยะเวลาเดินทาง (ETA)
3.  **Graceful Degraded Routing:** ทำหน้าที่เป็นตัวสำรองคำนวณระยะพิกัดเชิงคณิตศาสตร์ (Haversine) ในกรณีที่ OSRM ล่ม

---

## 2. ข้อกำหนดเบื้องต้นและการติดตั้ง (Prerequisites & Setup)

### ข้อกำหนดทางเทคนิค (Prerequisites)
*   **Python:** เวอร์ชัน 3.11.x (แนะนำเพื่อความเสถียรของ OR-Tools)
*   **Libraries:** ดูรายละเอียดใน [requirements.txt](requirements.txt) (`fastapi`, `uvicorn`, `ortools` v9.8+, `pydantic` v2+)
*   **ความปลอดภัย:** คอนฟิกโหลดค่าลับผ่าน AppRole ของ HashiCorp Vault ขาเริ่มระบบ (ดูที่ฟังก์ชัน `load_vault_config()` ใน [main.py](main.py#L8))

### วิธีการรันโปรเจกต์ภายในเครื่อง (Local Run)
1.  สร้างและเปิดใช้งาน Virtual Environment:
    ```bash
    cd c:\Users\ASUS\Desktop\Project\Delivery\ai-engine
    python -m venv venv
    
    # Windows PowerShell:
    .\venv\Scripts\Activate.ps1
    # Linux/Mac:
    source venv/bin/activate
    ```
2.  ติดตั้ง Dependencies ทั้งหมด:
    ```bash
    pip install -r requirements.txt
    ```
3.  รันเซิร์ฟเวอร์โหมดพัฒนา (Development mode):
    ```bash
    uvicorn main:app --host 0.0.0.0 --port 8000 --reload
    ```
    *(สามารถทดสอบและเรียกดู Spec เอกสาร API ได้ที่ `http://localhost:8000/docs`)*

---

## 3. อัลกอริทึมและการหาค่าดีที่สุด (Algorithm Logic)

### 3.1 การจัดคิวเส้นทาง (Google OR-Tools VRP Solver)
ประมวลผลผ่านโมดูลหลัก [vrp_solver.py](app/core/vrp_solver.py):
*   **Vehicle Routing Problem (VRP):** แก้ไขสมการคำนวณเส้นทางพนักงานขนส่งหลายคนโดยใช้ `pywrapcp.RoutingModel`
*   **Precedence Constraints (กฎการหยิบ/ส่งสินค้า):** กำหนดเงื่อนไขเด็ดขาดผ่าน `routing.AddPickupAndDelivery(...)` เพื่อสั่งให้ **"ตำแหน่งหยิบสินค้า (Pickup Node) ต้องได้รับการเยี่ยมเยือนก่อนตำแหน่งส่งสินค้า (Delivery Node) ของออเดอร์นั้นๆ เสมอ"**
*   **Search Parameters (ฟังก์ชันค้นหา):** 
    - เริ่มค้นหาเส้นทางด่วนด้วยกลยุทธ์ **`FirstSolutionStrategy.PATH_CHEAPEST_ARC`** (หาแนวเส้นทางที่มีค่าใช้จ่ายต่ำที่สุดก่อน)
    - **Model DoS Prevention:** กำหนดขีดจำกัดเวลาคำนวณสูงสุด (`search_parameters.time_limit.seconds = 5`) เพื่อป้องกันสภาวะสมการประมวลผลไม่รู้จบมาถล่ม CPU ของเซิร์ฟเวอร์ (OWASP LLM04)

### 3.2 การหา Matrix ระยะทาง (Distance Matrix Calculation)
*   **OSRM Integration:** ในโหมดเสถียรปกติ ระบบจะส่งรายชื่อชุดพิกัดละติจูด/ลองจิจูดไปยัง local OSRM Service (`http://osrm:5000/table/v1/driving/...`) เพื่อคำนวณหา Dijkstra Distance Matrix ระหว่างทุกหมุดพิกัดบนโครงข่ายถนนจริงจังหวัดอุดรธานี
*   **Redis Cache:** มีการบันทึกค่า Matrix ระยะทางที่เพิ่งคำนวณลง Redis เพื่อประหยัด CPU ในคำร้องขอรอบถัดไป
*   **Haversine Fallback (ระบบป้องกันล่ม):**  
    หาก OSRM ล่มหรือมีปัญหาล่าช้า ระบบจะสลับมาใช้สูตรคำนวณทรงกลมโลก [haversine_distance](app/core/geo_utils.py#L8) เพื่อหาพิกัดระยะทางตรง (Straight-line distance in meters) และหารด้วยค่าความเร็วเฉลี่ยคงที่ (Rule-based average velocity) เป็นค่าประมาณการ ETA แทนทันที

---

## 4. โครงสร้างข้อมูลขาเข้า/ขาออก (API Interfaces)

ระบบมี 3 Endpoints หลักที่เปิดรับใช้งานผ่าน [api.py](app/api/v1/api.py):

### 4.1 `/api/v1/dispatch/rank` (Rider Selection)
*   **เป้าหมาย:** จัดอันดับ Rider Candidates ที่พร้อมวิ่งงานในพื้นที่รอบร้านค้า (สูงสุด 200 รายการ)
*   **Input ([DispatchRankRequest](app/models/dispatch_models.py)):**
    - `order`: ข้อมูลจุดรับ (Pickup) และจุดส่ง (Dropoff)
    - `candidates`: รายชื่อคนขับว่างงาน (`id`, `lat`, `lng`, `bearing`, `status`)
*   **Output:** ลำดับและรายชื่อคนขับว่าง เรียงตามคะแนนความเหมาะสมสูงสุดลงไป (Scoring Score คำนวณจากระยะทางทาบถนน, ทิศทางหัวรถ `is_same_direction`, และพฤติกรรมปฏิเสธงานสะสม)

### 4.2 `/api/optimize-route` (VRP Route Optimization)
*   **เป้าหมาย:** จัดคิวเส้นทางหยิบและส่งสินค้าหลายจุดสำหรับ Rider (Multi-Drop sequence)
*   **Input ([Location](app/models/routing_models.py)):** รายชื่อของพิกัดแวะพักทั้งหมด, จำนวนรถที่มี และจุดจอดปล่อยรถ (Depot index)
*   **Output:** ลำดับลำดับการเดินทางในอาร์เรย์ `optimized_route` พร้อมค่าระยะทางรวมเมตร `total_distance_meters`

### 4.3 `/api/v1/predict-eta` (ETA Estimator)
*   **เป้าหมาย:** ทำนายระยะเวลาจัดส่งและเดินทางรวมของ Rider
*   **Input:** พิกัดต้นทางและปลายทาง
*   **Output:** ระยะเวลาวินาทีโดยประมาณ

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Contracts)
*   [AI Routing Engine Core Specification](../.docs/ai-context/spec-ai-engine.md)
*   [API Endpoints JSON Payloads Specification](../.docs/ai-context/contracts/api-contracts.md)
*   [GeoJSON Coordinate Standard Rules](../.docs/ai-context/contracts/geojson-contracts.md)
