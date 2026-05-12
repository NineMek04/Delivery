# AI-BLUEPRINT: Smart Delivery Routing System

## 1. ข้อมูลโครงการ (Project Overview)

- [cite_start]**ชื่อโครงการ:** ระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์ (AI-Optimized Smart Delivery Routing System) [cite: 2, 6]
- [cite_start]**เป้าหมาย:** แก้ปัญหาการคำนวณเส้นทาง Multi-drop ที่ขาดประสิทธิภาพ เพื่อประหยัดเวลาและเชื้อเพลิง [cite: 8, 23]
- [cite_start]**สถาปัตยกรรม:** Microservices Architecture ทำงานบน Docker Container [cite: 8, 50, 66]

## 2. เทคโนโลยีที่ใช้ (Technology Stack)

- [cite_start]**Main Backend:** .NET 8 ใช้ SignalR สำหรับ Real-time WebSockets [cite: 27, 46, 68]
- [cite_start]**AI & Routing Engine:** Python FastAPI ร่วมกับ Google OR-Tools (VRP Algorithm) [cite: 28, 46, 69]
- [cite_start]**Spatial Database:** PostgreSQL + PostGIS (มาตรฐานพิกัด GEOMETRY 4326) [cite: 29, 47, 56, 71]
- [cite_start]**Admin Dashboard:** Angular (Enterprise structure) [cite: 29, 45, 70]
- [cite_start]**Rider App:** Flutter (Cross-platform) [cite: 26, 45]

## 3. กฎและมาตรฐานการพัฒนา (Development Standards)

- [cite_start]**Database:** ทุกการ Query ข้อมูลเชิงพื้นที่ต้องใช้ Geospatial Index (2dsphere) เพื่อประสิทธิภาพสูงสุด [cite: 57]
- [cite_start]**Communication:** การส่งพิกัด GPS ระหว่าง App และ Server ต้องผ่าน SignalR/WebSockets เท่านั้น [cite: 61, 65]
- [cite_start]**Containerization:** บริการทั้งหมดต้องถูกนิยามไว้ใน `docker-compose.yml` เพื่อให้ติดตั้งได้ในคำสั่งเดียว [cite: 54, 66]
- [cite_start]**Code Pattern:** - Backend: Repository Pattern / Dependency Injection [cite: 45]
  - [cite_start]Frontend: Component-based architecture [cite: 45]

## 4. ข้อกำหนดสภาพแวดล้อม (Environment Notes)

- **Hardware Constraint:** เครื่องที่พัฒนา (ASUS ROG) อาจมีปัญหาความร้อนและ Driver การ์ดจอ (nvlddmkm)
- **GPU Optimization:** หากใช้ Lossless Scaling (LSFG 2.1) ให้จำกัด FPS ในเกม/แบบจำลองไว้ที่ 60 เพื่อป้องกันการ์ดจอแฮงก์
