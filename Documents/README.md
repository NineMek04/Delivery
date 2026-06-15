# Global Documents Index & Manuals (Documents/README.md)

โฟลเดอร์นี้รวบรวมเอกสารอ้างอิงและคู่มือการดูแลรักษาระบบระดับสูง (Global Management Manuals) สำหรับทีมวิศวกรซอฟต์แวร์, ทีม DevOps, และแอดมิน เพื่อใช้ประเมินและตั้งค่าระบบจัดส่งอัจฉริยะ (Smart Delivery Routing System) โดยถูกจัดสรรแยกประเภทหมวดหมู่ย่อยอย่างชัดเจนดังนี้:

---

## 📚 รายชื่อคู่มือแยกตามหมวดหมู่ (Documentation Directory Catalog)

### ⚙️ 1. คู่มือการตั้งค่าติดตั้งบริการ (Service Setup Guides)
*   👉 [PostgreSQL & PgBouncer Database Manual](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/setup/DATABASE-SETUP.md)  
    *(การตั้งค่าตาราง GIS PostGIS, สระจำลองธุรกรรม PgBouncer Transaction Mode และข้อพึงระวัง ORM)*
*   👉 [Redis Cache & Locking Manual](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/setup/REDIS-SETUP.md)  
    *(สเปกคีย์ไรเดอร์ TTL, Lua Script ของระบบล็อก RedLock และนโยบายความเสถียร volatile-lru)*
*   👉 [Seq Centralized Logging Manual](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/setup/SEQ-SETUP.md)  
    *(การตั้งค่า Serilog Ingestion, การสืบจับรอย Correlation ID และการคิวรีสืบค้นล็อก)*
*   👉 [OSRM Map Data & Setup Reference](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/setup/OSRM-SETUP.md)  
    *(ข้อมูล assets แผนที่, คำสั่งดาวน์โหลด/คอมไพล์โครงข่ายถนนจังหวัดอุดรธานี และ Docker volumes)*

### 🛡️ 2. คู่มือความปลอดภัยและโครงสร้างพื้นฐาน (Infrastructure & Operations Manuals)
*   👉 [Production Deployment & Security Configuration](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/infrastructure/PRODUCTION-DEPLOYMENT.md)  
    *(แนวทางการทำ Port Isolation, การตั้ง SSL/TLS บน Nginx Proxy และ Cloudflare WAF Settings)*
*   👉 [Scale Guide & Performance Tuning Manual](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/infrastructure/SCALE-GUIDE.md)  
    *(การจูน CPU/RAM คอนเทนเนอร์, Connection Pooling limits และ Redis Eviction)*
*   👉 [OWASP Top 10 Security Standard Reference](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/infrastructure/OWASP_Standard.md)  
    *(มาตรฐานเช็คลิสต์ตรวจสอบความปลอดภัยระบบ AI Agent ตามกรอบปฏิบัติ OWASP)*
*   👉 [PowerShell Run Command Manual](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/infrastructure/RUN-COMMANDS.md)  
    *(ชุดคีย์ลัดคำสั่งสตาร์ตและรันระบบ รวมถึงการรันบอททดสอบ Sandbox และโหลดสตรีมมิ่ง)*

### 💻 3. ข้อมูลสเปกและสถาปัตยกรรมเชิงลึก (Development Specs & Codebase Guides)
*   👉 [System Overview & Architecture Diagram](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/development/SYSTEM-OVERVIEW.md)  
    *(แผนผังการเชื่อมโยงระบบ พอร์ตเชื่อมต่อ และ GPS Telemetry Stream Pipeline)*
*   👉 [VRP Job Queue Architecture Design (Phase 2)](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/development/AI-QUEUE-DESIGN.md)  
    *(การออกแบบ Asynchronous Queue คำนวณเส้นทาง VRP ด้วย RabbitMQ + Python Worker)*
*   👉 [Custom Database Migration Service Tech Guide](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/development/MIGRATION-SERVICE.md)  
    *(รายละเอียดการทำงานของ PostgresAdvancedConfigurator.cs ในการ Partition, Cluster และ Index ตาราง)*
*   👉 [Core Codebase Patterns & Architecture Technical Manual Directory](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/development/CODEBASE_PATTERNS/README.md)  
    *(คู่มืออธิบายรูปแบบโครงสร้างโค้ดแยกย่อย: Base Controllers, Base Services, Middlewares/Headers, Validation & GIS index, Mapster, FastAPI Threading, Auto migrations, ThreadPool starvation, Idempotency, Concurrency locks, Frontend memory leaks และ trace logging)*
*   👉 [Project Specification Master Sheet (PROJECT-SPEC.md)](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/development/PROJECT-SPEC.md)  
    *(ข้อกำหนดเชิงเทคนิคและสัญญาเชื่อมต่อระบบทั้งหมดของโปรเจกต์เดิม)*
*   👉 [AI Architecture Blueprint (AI-BLUEPRINT.md)](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/development/AI-BLUEPRINT.md)  
    *(พิมพ์เขียวความเข้ากันได้ของระบบ AI VRP Solver และสูตร Dijkstra)*
*   👉 [Project context reference (project-context.md)](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/development/project-context.md)  
    *(รายละเอียดบริบทของโครงการในการจับคู่ออเดอร์แบบเรียลไทม์)*

---

## 🏛️ เอกสารสัญญาการเชื่อมต่อระบบย่อยเดิม (Contracts Spec)
เอกสารเหล่านี้ยังถูกเก็บรักษาไว้อย่างมั่นคงภายในโฟลเดอร์ต้นทาง และใช้ลิงก์อ้างอิงในการทำงานโดยไม่ต้องย้ายตำแหน่งไฟล์:
*   [Backend API Specification](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-backend.md)
*   [AI Routing Engine Specification](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-ai-engine.md)
*   [Admin Angular Dashboard Specification](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-frontend.md)
*   [Rider Flutter Mobile Specification](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-mobile-rider.md)
*   [Infrastructure, Telemetry & SLO Specification](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-infra-devops.md)
