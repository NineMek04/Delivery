# 🐍 FastAPI Anti-Blocking Threading

ใน FastAPI AI Routing Engine ([optimize.py](../../../ai-engine/app/api/v1/endpoints/optimize.py)) คำนวณเส้นทาง VRP:

- **ปัญหา:** คำสั่งคำนวณ VRP (OR-Tools) เป็นการประมวลผลเชิง CPU หนักหน่วง (CPU-bound)
- **สถาปัตยกรรมกันค้าง (Anti-Event-Loop-Blocking):**
  - **ห้ามประกาศใช้ `async def`** ใน Endpoint ที่มีการคำนวณ CPU-bound ล้วน ๆ เนื่องจากจะไปบล็อก Event loop หลักของ Python
  - **แนวทางปฏิบัติ:** ประกาศด้วยฟังก์ชันธรรมดา **`def optimize_route(...)`** เพื่อบังคับให้ FastAPI ส่งผ่านงานนั้นเข้าสู่ระบบ Thread Pool แยกต่างหาก (External Worker Thread Pool) ช่วยให้ API หลักยังสามารถตอบสนองทราฟฟิกพิกัด GPS ได้ปกติในเวลาเดียวกัน
