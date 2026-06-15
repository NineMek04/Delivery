# PostgreSQL & PgBouncer Database Manual (Documents/setup/DATABASE-SETUP.md)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือการตั้งค่าสเปกฐานข้อมูลหลักเชิงพื้นที่ (**PostgreSQL + PostGIS Extension**) และสระควบคุมตัวเชื่อมต่อฐานข้อมูล (**PgBouncer Connection Pooler**) สำหรับทีมผู้ดูแลระบบและหลังบ้าน

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
ฐานข้อมูลเชิงพื้นที่ทำหน้าที่หลักในการจัดเก็บข้อมูลถาวรทั้งหมดของธุรกิจ (Spatial Persistent Truth):
1.  **Spatial Persistence:** จัดเก็บตำแหน่งที่ตั้งของร้านค้า (PickupLocation), ตำแหน่งจัดส่งลูกค้า (DropoffLocation) และพิกัดเดินทางจริงของ Rider โดยใช้ชนิดข้อมูลพิกัดภูมิศาสตร์ `Point` (NetTopologySuite.Geometries)
2.  **Coordinate Reference System:** บังคับใช้อ้างอิงพิกัดโลกมาตรฐาน **SRID 4326** (WGS 84 ละติจูด/ลองจิจูด) ในฐานข้อมูลทั้งหมด
3.  **Connection Optimization (PgBouncer):** คอยคัดกรองจัดสระเชื่อมต่อฐานข้อมูลเพื่อแบ่งปัน Sessions ป้องกันแรมฐานข้อมูลเต็มเมื่อ Backend ยิงคำขอนับร้อยพร้อมกัน

---

## 2. โครงสร้างและการตั้งค่าฐานข้อมูลเชิงพื้นที่ (PostgreSQL + PostGIS)

รายละเอียดของสเปกฐานข้อมูลถูกกำหนดใน [ApplicationDbContext.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Data/ApplicationDbContext.cs) และควบคุมโครงสร้างเวอร์ชันผ่าน EF Core:

*   **Database Baseline:** มีการรวบรวมตารางและ Schema เริ่มต้นแบบล้างใหม่ทั้งหมดไว้ในไฟล์สะสม [ConsolidatedBaseline20260614.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Migrations/20260614152246_ConsolidatedBaseline20260614.cs)
*   **Spatial Indexing (GIST Indexes):**  
    เพื่อเพิ่มความเร็วในการคำนวณและค้นหาเชิงพื้นที่รอบตัว (เช่น ค้นหาไรเดอร์ในรัศมีร้านค้า) ระบบบังคับทำดัชนีพิเศษ **GIST (Generalized Search Tree) Indexes** บน Geometry Columns เสมอ:
    ```sql
    CREATE INDEX "IX_Shops_PickupLocation" ON "Shops" USING GIST ("PickupLocation");
    CREATE INDEX "IX_Orders_DropoffLocation" ON "Orders" USING GIST ("DropoffLocation");
    ```
*   **Idempotency Table (`ProcessedEvents`):**  
    ตารางเก็บรหัสประวัติการรันอีเวนต์เพื่อป้องกันปัญหา RabbitMQ ส่งซ้ำ โดยเก็บ `EventId` และ `ProcessedAt` เพื่อทำดัชนีคีย์หลักป้องกันการเขียนซ้ำ

---

## 3. การจูนแต่งประสิทธิภาพ PgBouncer (Connection Pooling)

PgBouncer วางตัวอยู่เกาะหน้าตู้ฐานข้อมูลหลักใน [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml):

```yaml
  pgbouncer:
    image: edoburu/pgbouncer
    container_name: delivery-pgbouncer
    ports:
      - "127.0.0.1:5432:5432" # เปิดเฉพาะ Loopback ป้องกันข้างนอกแฮกเกอร์เจาะพอร์ต
    environment:
      - DB_HOST=db
      - DB_USER=postgres
      - DB_PASSWORD=${POSTGRES_PASSWORD}
      - POOL_MODE=transaction # เปิดใช้โหมด transaction เพื่อแบ่งปัน pool สูงสุด
      - MAX_CLIENT_CONN=10000
      - DEFAULT_POOL_SIZE=100
```

### 3.1 การประยุกต์ใช้โหมด POOL_MODE=transaction

> [!CAUTION]
> **วิกฤต PgBouncer Transaction Mode กับ ORM (Entity Framework Core)**
> การเปิดใช้ `POOL_MODE=transaction` ใน PgBouncer จะทำการแชร์สายเชื่อมต่อ (Connection Pooling) ในระดับ Transaction 
> **ความจริงอันโหดร้าย:** EF Core ใน .NET 8 โดยดีฟอลต์จะมีการใช้ฟีเจอร์ Prepared Statements (คำสั่ง SQL ที่เตรียมล่วงหน้าบน session) 
> หากมีการเรียกใช้คำสั่งเดียวกันซ้ำในสายเชื่อมต่ออื่นที่แชร์กัน PgBouncer จะพ่นข้อผิดพลาดออกมาทันทีเนื่องจาก Prepared Statements ค้างอยู่ในเซสชันเก่าหรือไม่มีบนเซสชันใหม่
> 
> **แนวทางปฏิบัติเชิงวิศวกรรมที่บังคับสำหรับผู้ดูแลระบบและ Developer:**
> 1. **ปิด Prepared Statements ใน Connection String ของ .NET 8:** 
>    ระบุพารามิเตอร์ `No Reset On Close=true` ร่วมกับ `Max Auto Prepare=0;` ใน Connection String เสมอ เช่น:
>    `Host=localhost;Port=6432;Database=delivery_db;Username=postgres;Password=xxx;No Reset On Close=true;Max Auto Prepare=0;`
> 2. **หลีกเลี่ยงการเปิดใช้ Session-level Features:** ห้ามรันคำสั่ง SQL ที่เกี่ยวข้องกับ Session state (เช่น `SET timezone`, `LISTEN/NOTIFY` หรือ Temp Tables) ภายใต้สายเชื่อมต่อของ PgBouncer Transaction Mode หากจำเป็นต้องใช้ ให้แยกเปิดสายเชื่อมต่อตรงไปยัง PostgreSQL Port 5432 แทน

*   **ทำไมต้องเป็น Transaction Mode?**  
    ในระบบ API ทั่วไป คำขอจะเปิดฐานข้อมูล อ่านเขียนสั้นๆ และปิดลง โหมดนี้จะคืนสายเชื่อมต่อ (Connection) กลับมาเข้าสระทันทีที่ SQL Statement หรือ Transaction นั้นๆ ประมวลผลจบ โดยไม่ต้องรอให้ Client ตัดการเชื่อมต่อ HTTP ช่วยประหยัดตัวเชื่อมต่อได้เกือบ 10 เท่า
*   **ข้อจำกัด:** ห้ามเปิดใช้ Prepared Statements ใน Backend API เนื่องจาก SQL จะหาแผนคำนวณไม่พบบน Sessions อื่นที่แชร์กัน (Backend ได้รับการคอนฟิกปิด Prepared Statements เรียบร้อยใน Connection String)

---

## 4. วิธีการขึ้นระบบและตรวจสอบฐานข้อมูล (Verification Steps)

1.  สตาร์ตระบบฐานข้อมูลและ PgBouncer:
    ```bash
    docker-compose up -d db pgbouncer
    ```
2.  เข้าใช้งานและสืบค้น (ใช้เครื่องมือเช่น DBeaver / pgAdmin):
    *   **Host:** `localhost` (หรือ `127.0.0.1`)
    *   **Port:** `5432` (ผ่าน PgBouncer)
    *   **Database:** `delivery_db`
    *   **Username:** `postgres` | **Password:** รหัสระบุใน `.env` ของคุณ
3.  ตรวจสอบความถูกต้องของส่วนขยายเชิงพื้นที่:
    เรียกคำสั่ง SQL ต่อไปนี้:
    ```sql
    SELECT PostGIS_Version();
    ```
    *Output ที่แสดงเวอร์ชัน (เช่น 3.4.x) ยืนยันว่าพร้อมวิเคราะห์สูตรภูมิศาสตร์*

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Context)
*   [Backend API Subsystem Specification Sheet](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-backend.md)
*   [Scale Guide & Performance Tuning Manual (SCALE-GUIDE.md)](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/infrastructure/SCALE-GUIDE.md)
