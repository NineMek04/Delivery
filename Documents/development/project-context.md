# ข้อมูลโครงการ (Project Meta Data)
* **ชื่อปริญญานิพนธ์ (TH):** ระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์
* **ชื่อปริญญานิพนธ์ (EN):** Intelligent Delivery Routing and Optimization System
* **ผู้จัดทำ:** นายทนงศักดิ์ ผุยบัวค้อ และ นายธีภัทร บุญมามี
* **สาขาวิชา:** วิศวกรรมคอมพิวเตอร์และการสื่อสาร (Computer and Communication Engineering)
* **สถาบัน:** มหาวิทยาลัยราชภัฏอุดรธานี (Udon Thani Rajabhat University)
* **ปีการศึกษา:** 2569 (2026)

## บทคัดย่อ (Abstract)
โครงงานระบบจำลองและเพิ่มประสิทธิภาพเส้นทางการขนส่งแบบเรียลไทม์ พัฒนาขึ้นเพื่อแก้ปัญหาและเพิ่มประสิทธิภาพให้แก่ระบบโลจิสติกส์และการขนส่ง เนื่องจากปัจจุบันแพลตฟอร์มการจัดส่งมักประสบปัญหาการคำนวณเส้นทางที่มีจุดแวะรับ-ส่งหลายจุด (Multi-drop) ที่ขาดประสิทธิภาพ ทำให้สิ้นเปลืองเวลาและเชื้อเพลิง 

ผู้จัดทำจึงพัฒนาระบบสถาปัตยกรรมแบบไมโครเซอร์วิส (Microservices Architecture) ภายใต้การทำงานร่วมกันของเทคโนโลยีที่หลากหลาย ได้แก่:
* **.NET Core:** สำหรับระบบจัดการหลัก
* **Python FastAPI & AI:** สำหรับการหาเส้นทางที่คุ้มค่าที่สุด
* **PostgreSQL & PostGIS:** สำหรับการจัดเก็บข้อมูลเชิงพื้นที่
* **Angular:** สำหรับระบบแสดงผลหน้าเว็บ (Dashboard)
* **Flutter:** สำหรับแอปพลิเคชันพนักงานขับรถ
* **Docker Container:** สำหรับจำลองสภาพแวดล้อมให้ระบบทั้งหมดทำงานร่วมกันได้โดยไม่ต้องพึ่งพาฮาร์ดแวร์ต้นทุนสูง

---

## บทที่ 1: บทนำ (Introduction)

### 1.1 หลักการและเหตุผล
ธุรกิจ Delivery Platform มีการเติบโตอย่างรวดเร็ว แต่ปัญหาหลักที่พบคือการจัดสรรออเดอร์ซ้อน (Batched Orders) หรือการส่งของหลายจุด (Multi-drop) ที่ขาดประสิทธิภาพ โครงงานนี้จึงมีแนวคิดพัฒนาระบบโดยใช้วิธีประหยัดงบประมาณสูงสุด (Zero-Budget Prototype) โดยประยุกต์ใช้สมาร์ทโฟนที่มีอยู่แล้ว (Bring Your Own Device) ให้ทำหน้าที่เป็นเซนเซอร์ติดตามตำแหน่ง ผสานกับซอฟต์แวร์สถาปัตยกรรมระดับองค์กร (Enterprise Architecture) และระบบเพิ่มประสิทธิภาพเส้นทางในการแก้ไขสมการการเดินทาง

### 1.2 บล็อกไดอะแกรมของระบบ
ระบบทำงานร่วมกันหลายส่วนย่อย (Microservices) ประกอบด้วย 4 ส่วนหลัก:
1.  **แอปพลิเคชันฝั่งพนักงานขับรถ (Rider App):** พัฒนาด้วย Flutter รับค่าพิกัด GPS และส่งผ่าน WebSockets
2.  **ระบบประมวลผลหลัก (Main Backend):** พัฒนาด้วย .NET 8 เป็นศูนย์กลางการสื่อสารและจัดการ Business Logic
3.  **ระบบเพิ่มประสิทธิภาพเส้นทาง (Route Optimizer):** พัฒนาด้วย Python FastAPI หาเส้นทางที่สั้นที่สุด
4.  **ระบบจัดการฐานข้อมูลและแดชบอร์ด:** ใช้ PostgreSQL ร่วมกับ PostGIS เก็บข้อมูลเชิงพื้นที่ และ Angular สำหรับหน้าเว็บ

### 1.3 วัตถุประสงค์ของโครงงาน
* เพื่อศึกษาและพัฒนาระบบสถาปัตยกรรมไมโครเซอร์วิส (Microservices) สำหรับการขนส่งอัจฉริยะ
* เพื่อประยุกต์ใช้อัลกอริทึมเพิ่มประสิทธิภาพเชิงคณิตศาสตร์ในการแก้ปัญหาการจัดเส้นทาง (Vehicle Routing Problem)
* เพื่อพัฒนาระบบสื่อสารแบบเรียลไทม์ระหว่างอุปกรณ์เคลื่อนที่และเว็บแอปพลิเคชัน
* เพื่อลดต้นทุนในการสร้างต้นแบบ (Prototype)

---

## บทที่ 2: ทฤษฎีและเทคโนโลยีที่เกี่ยวข้อง (Tech Stack & Theories)

### 2.1 สถาปัตยกรรมไมโครเซอร์วิส (Microservices Architecture)
แนวทางการพัฒนาที่ประกอบไปด้วยบริการย่อยๆ ทำงานเป็นอิสระต่อกัน (Loosely coupled) ช่วยแก้ปัญหาคอขวดในการพัฒนาจากการใช้ Monolithic Architecture องค์ประกอบหลักประกอบด้วย API Gateway, Service Registry & Discovery และ Independent Databases

### 2.2 ปัญหาการจัดเส้นทางยานพาหนะ (Vehicle Routing Problem - VRP)
การหาคำตอบที่ดีที่สุด (Exact Solution) ต้องใช้เวลาคำนวณนาน ระบบจึงใช้อัลกอริทึมแบบฮิวริสติกส์ (Heuristics) หรือเมตาฮิวริสติกส์ (Meta-heuristics) โครงงานนี้เลือกใช้ **Google OR-Tools** ซึ่งเป็นซอฟต์แวร์โอเพนซอร์สสำหรับการหาค่าที่เหมาะสมที่สุด เพื่อใช้แก้สมการในเวลาอันสั้น

### 2.3 เทคโนโลยี Frontend
* **Angular:** ใช้สถาปัตยกรรม Component-based เปลี่ยนแปลงข้อมูลเฉพาะจุดได้ดี
* **Flutter:** ใช้สถาปัตยกรรม BLoC Pattern แยกส่วนระหว่าง UI และ Business Logic (State Management)

### 2.4 เทคโนโลยี Backend & Route Optimization
* **.NET Core & SignalR:** ใช้จัดการช่องทางการสื่อสารมหาศาลและ WebSockets แบบเรียลไทม์ (Broadcasting)
* **Python FastAPI:** รองรับการทำงานร่วมกับไลบรารี optimization อย่าง Google OR-Tools

### 2.5 ระบบฐานข้อมูลเชิงพื้นที่ (Spatial Database)
* **PostgreSQL & PostGIS:** ใช้โครงสร้างดัชนีแบบ R-Tree Indexing และ GiST ช่วยค้นหาข้อมูลเชิงพื้นที่ (เช่น การหารัศมีใกล้เคียง) ได้รวดเร็วระดับเสี้ยววินาที

### 2.6 คอนเทนเนอร์ (Docker)
ใช้ **Docker** เพื่อบรรจุซอฟต์แวร์ลงใน Container ทำให้สามารถจำลองสภาพแวดล้อมได้เหมือนกันทุกเครื่อง ลดปัญหา System Dependency

---

## บทที่ 3: วิธีดำเนินการวิจัย (System Architecture & Flow)

### 3.1 การวิเคราะห์ระบบงาน
* **ข้อดีของ Microservices:** ปรับขนาด (Scale) แยกแต่ละบริการได้, ประหยัดทรัพยากร, ปรับใช้ได้รวดเร็วผ่าน Docker Compose
* **ข้อดีของ Spatial DB:** รองรับ Geospatial Query ระดับมิลลิวินาที, จัดการความถูกต้องของข้อมูล (Data Integrity) ได้ดี

### 3.2 หลักการออกแบบระบบ
**กระบวนการทำงานหลัก (System Flow):**
1.  **Tracking Flow:** Rider App ส่งพิกัด GPS ผ่าน Bi-directional WebSockets ไปที่ SignalR Hub (.NET) เพื่อบันทึกลง PostGIS และ Broadcast ไปที่ Dashboard แอดมินแบบเรียลไทม์
2.  **Optimization Flow:** เมื่อมีคำสั่งซื้อ แอดมินจะสั่งคำนวณเส้นทาง ข้อมูลพิกัดจะถูกส่งผ่าน Internal REST POST ไปที่ FastAPI (Python)
3.  **Response Flow:** Route Optimizer (Google OR-Tools + OSRM) จะคำนวณลำดับจุดแวะพักที่สั้นที่สุด แล้วส่งกลับมาให้ .NET บันทึก
4.  **Dispatch Flow:** SignalR Hub กระจายข้อมูลเส้นทางกลับไปหา Rider App ของพนักงานเพื่อนำทางทันที

---

## 📌 สิ่งที่ควรแก้ไขและเพิ่มเติม (อัปเดตจากระบบจริง)

> [!IMPORTANT]  
> ระบบปัจจุบันได้ถูกพัฒนาเป็นสถาปัตยกรรมระดับ Enterprise ที่มีความซับซ้อนกว่าเนื้อหาในบทก่อนหน้า (รันอยู่ถึง 12 Microservices) จึงเสนอให้มีการอัปเดตเอกสารวิทยานิพนธ์โดย **เพิ่มและแก้ไข** ประเด็นต่อไปนี้ให้สอดคล้องกับระบบจริง

### 1. จุดที่ทำแตกต่างหรือตกหล่นไปจากเอกสาร

| ส่วนประกอบ | ระบุในเอกสารเดิม | **สิ่งที่ระบบจริงใช้งาน (ควรแก้ไข/เพิ่ม)** |
| :--- | :--- | :--- |
| **Flutter State Management** | BLoC Pattern | **Riverpod** (`flutter_riverpod`) คู่กับ **GoRouter** และ **Dio** |
| **Message Broker (สื่อสารข้ามระบบ)** | ไม่ได้ระบุ | **RabbitMQ** สำหรับ Integration Events แบบ Asynchronous |
| **In-Memory Cache & Telemetry** | ไม่ได้ระบุ | **Redis** สำหรับทำ Realtime Speed Layer และแคช GPS ความถี่สูง |
| **Routing Engine (Road Network)** | อ้างถึงแค่ OR-Tools | **OSRM** (Offline Dijkstra) คำนวณระยะทางบนถนนจริงคู่กับ OR-Tools |
| **Resilience & Fault Tolerance** | ไม่ได้ระบุ | แบ็กเอนด์ใช้ **Polly** (Circuit Breaker & Retry) ป้องกัน Route Optimizer service ล่ม |
| **Observability (Logging & Metrics)**| ไม่ได้ระบุ | **Seq** (Centralized Log), **Prometheus** (Metrics), **Grafana** (Dashboard) |
| **API Gateway / Proxy** | ไม่ได้ระบุ | **Nginx** ทำหน้าที่ Reverse Proxy หน้าบ้าน |
| **โครงสร้างสถาปัตยกรรมโดยรวม** | ระบุว่ามี 4 ส่วนหลัก | สถาปัตยกรรมจริงสเกลระดับ **12 Microservices / Containers** |

### 2. รายละเอียดที่ต้องเพิ่มเข้าไปในแต่ละบท

> [!TIP]  
> แนะนำให้นำเนื้อหาเหล่านี้แทรกเข้าไปในบทที่เกี่ยวข้อง เพื่อให้เอกสารมีความสมบูรณ์

**📍 บทคัดย่อ (Abstract) และ บทที่ 1: บทนำ**
- **บล็อกไดอะแกรมของระบบ:** ขยายจาก "4 ส่วนหลัก" ให้ครอบคลุม **Message Broker (RabbitMQ)** ที่ใช้รับส่งงาน, **Speed Layer (Redis)** สำหรับจัดการพิกัดเรียลไทม์ และระบบ **Observability (Seq, Prometheus, Grafana)**

**📍 บทที่ 2: ทฤษฎีและเทคโนโลยีที่เกี่ยวข้อง (จุดที่ต้องแก้เยอะที่สุด)**
- **2.3 เทคโนโลยี Frontend:** **ลบคำว่า BLoC Pattern ออก** แล้วเปลี่ยนเป็น **"Riverpod Pattern & Declarative Routing (GoRouter)"**
- **2.4 เทคโนโลยี Backend & Route Optimization:**
  - **[เพิ่มเนื้อหา] Redis:** รองรับพิกัด GPS ความถี่สูง (Telemetry Events) เพื่อลดภาระ Database
  - **[เพิ่มเนื้อหา] RabbitMQ:** ใช้ส่งข้อความข้ามระบบแบบรับประกันการส่ง
  - **[เพิ่มเนื้อหา] OSRM:** เอนจินคำนวณระยะทางถนนจริงก่อนส่งให้ OR-Tools ประมวลผล
  - **[เพิ่มเนื้อหา] Polly (Resilience Pattern):** การทำ Circuit Breaker เพื่อป้องกันระบบล่มแบบลูกโซ่
- **2.6 คอนเทนเนอร์และระบบโครงสร้างพื้นฐาน (DevOps & Infrastructure):** 
  - **[เพิ่มเนื้อหา] Nginx Reverse Proxy:** ทำหน้าที่เป็น API Gateway รับคำขอและกระจายให้ Backend/Frontend ป้องกันการเข้าถึงเซอร์วิสตรงๆ และแก้ปัญหา CORS
  - **[เพิ่มเนื้อหา] Seq (Centralized Logging):** รวบรวม Log จากทุกเซอร์วิสไว้ที่เดียวผ่าน Serilog
  - **[เพิ่มเนื้อหา] Prometheus & Grafana:** Prometheus เก็บ Metrics ยอดโหลดเซิร์ฟเวอร์ และ Grafana แสดงผลเป็นกราฟเรียลไทม์

**📍 บทที่ 3: วิธีดำเนินการวิจัย**
- **3.2 หลักการออกแบบระบบ (Event-Driven Data Flow):** ระบบขับเคลื่อนด้วยเหตุการณ์ (Event-Driven) อ้างอิงจากกฎในระบบ แบ่งเป็น 3 กระแสข้อมูล:
  1. **Telemetry Events Flow:** Rider App ส่งพิกัดความถี่สูงเข้า **SignalR** -> พักข้อมูลชั่วคราวที่ **Redis** -> Broadcast ให้ Dashboard ทันทีโดยไม่รบกวนฐานข้อมูลหลัก
  2. **Integration Events Flow:** Backend สร้าง Event (เช่น `OrderCreated`) -> ส่งลง **RabbitMQ** -> Route Optimizer รับไปประมวลผล (โดยมีการป้องกันทำลอจิกซ้ำด้วยตาราง `ProcessedEvents` ใน Postgres)
  3. **Domain Events Flow:** อีเวนต์ที่เกิดขึ้นและใช้สื่อสารกันเอง **ภายใน** .NET Backend เท่านั้น (Internal Context)

### 3. AI Prompt สำหรับสร้าง Data Flow Diagram (Gemini Canvas)

> [!NOTE]  
> นำ Prompt ด้านล่างนี้ไปวางใน Gemini หรือ ChatGPT (โหมด Canvas หรือทั่วไป) เพื่อให้ AI ช่วยวาดแผนภาพสถาปัตยกรรมด้วย Mermaid.js ทันที

```text
ช่วยสร้างแผนภาพ Data Flow Diagram แบบ Mermaid.js (หรือวาดบน Canvas) สำหรับระบบ Delivery Microservices แบบ Event-Driven Architecture ให้หน่อย โดยต้องแสดงกล่ององค์ประกอบ (Microservices) และทิศทางการไหลของข้อมูล 3 รูปแบบหลักดังต่อไปนี้ให้ชัดเจน:

องค์ประกอบของระบบ (Nodes):
1. Rider App (Flutter)
2. Admin Dashboard (Angular)
3. Main Backend (.NET 8 SignalR)
4. Route Optimizer (Python FastAPI)
5. Message Broker (RabbitMQ)
6. Speed Layer Cache (Redis)
7. Spatial Database (PostgreSQL + PostGIS)
8. Road Network Engine (OSRM)

เส้นทางการไหลของข้อมูล (Edges/Flows):
1. Telemetry Flow (สีฟ้า): Rider App ยิงพิกัด GPS -> เข้า Backend (SignalR Hub) -> เก็บสถานะลง Redis -> Broadcast ต่อไปแสดงผลที่ Admin Dashboard ทันที
2. Integration Flow (สีส้ม): Backend -> สร้าง OrderCreatedIntegrationEvent -> ส่งเข้า RabbitMQ -> Route Optimizer อ่าน Event จาก RabbitMQ เพื่อเริ่มทำงาน
3. Sync/Optimize Flow (สีเขียว): Route Optimizer ส่งพิกัดไปถามระยะทางจาก OSRM -> OSRM คืนค่า Matrix กลับมา -> Route Optimizer คำนวณ Route เสร็จ -> ส่งเส้นทางบันทึกลง Database (PostgreSQL) และแจ้งกลับไปหา Backend

ขอให้แสดงแผนภาพออกมาในรูปแบบที่สวยงาม มองเห็นแยกโซน (Client-side, Middleware/Broker, Backend Services, Databases) ได้อย่างชัดเจน
```
