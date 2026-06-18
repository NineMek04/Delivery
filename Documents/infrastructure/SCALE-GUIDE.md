# Scale Guide & Performance Tuning Manual (Documents/infrastructure/SCALE-GUIDE.md)

คู่มือเล่มนี้จัดทำขึ้นสำหรับทีมพัฒนาและ DevOps เพื่อแนะแนวทางการขยายขนาดของระบบ (Upscaling) และการปรับจูนประสิทธิภาพเชิงลึกเมื่อระบบต้องรองรับทราฟฟิกปริมาณมากในโลกจริง (High-Load Operations)

---

## 1. จุดตั้งค่าประสิทธิภาพฐานข้อมูล (Database Connection Pooling & Limits)

เมื่อมีการร้องขอเชื่อมต่อเข้ามาจำนวนมาก PostgreSQL มักจะเป็นคอขวดหลัก ซึ่งระบบของเราได้ตัดตัวเชื่อมต่อที่ไม่จำเป็นและตั้งระดับ Connections ไว้ดังนี้:

### 1.1 การปรับแต่งระดับ EF Core Client (Backend API)
- **ตำแหน่งตั้งค่า:** กำหนดค่าผ่าน Env `ConnectionStrings__DefaultConnection` ในไฟล์ [docker-compose.yml](../../docker-compose.yml)
- **พารามิเตอร์สำคัญ:** `Maximum Pool Size=100`
- **แนวทางจูนแต่ง:** 
  - หากสเกลคอนเทนเนอร์ Backend เพิ่มขึ้น (เช่น 3 Instances) ต้องคำนวณ:
    $$\text{Total Pool Size} = \text{Instances} \times \text{Maximum Pool Size}$$
    ต้องระวังไม่ให้ค่ารวมนี้เกินความจุสูงสุดที่ PgBouncer หรือ PostgreSQL อนุญาต

### 1.2 การปรับแต่ง PgBouncer (Connection Pool Manager)
- **ตำแหน่งตั้งค่า:** เซอร์วิส `pgbouncer` ในไฟล์ [docker-compose.yml](../../docker-compose.yml)
- **พารามิเตอร์สำคัญ:**
  - `POOL_MODE=transaction`: ตั้งค่าโหมดการสระเชื่อมต่อเป็นระดับทรานแซกชัน (แชร์ Pool กันได้อย่างมีประสิทธิภาพสูงสุด เหมาะสำหรับทราฟฟิกอ่านเขียนสั้นๆ)
  - `MAX_CLIENT_CONN=10000`: จำนวนการร้องขอเชื่อมต่อฝั่งไคลเอนต์สูงสุดที่อนุญาตให้มารอคิว
  - `DEFAULT_POOL_SIZE=100`: จำนวนคอนเนกชันจริงที่ PgBouncer จะเปิดทิ้งไว้คุยกับฐานข้อมูล PostgreSQL
- **แนวทางจูนแต่ง:** หากเกิดปัญหาคำขอช้าลง ให้ตรวจสอบคิวค้างของ PgBouncer แล้วขยาย `DEFAULT_POOL_SIZE` ควบคู่กับการขยาย `max_connections` ของ PostgreSQL
- **คำเตือนความเสถียร:** เมื่อรัน PgBouncer ใน Transaction Mode จะต้องมั่นใจว่า Connection String ของ .NET API ได้ตั้งค่าปิด Prepared Statements เสมอ (ปิดด้วย `No Reset On Close=true;Max Auto Prepare=0;`) ตามรายละเอียดเพิ่มเติมใน [DATABASE-SETUP.md](../setup/DATABASE-SETUP.md)

### 1.3 การปรับแต่ง PostgreSQL Server
- **ตำแหน่งตั้งค่า:** เซอร์วิส `db` ในไฟล์ [docker-compose.yml](../../docker-compose.yml)
- **พารามิเตอร์สำคัญ:**
  - `max_connections=1000`
  - `shared_buffers=1GB` (ควรปรับเป็น 25% - 40% ของ RAM ทั้งหมดของเครื่องโฮสต์จริง)
  - `work_mem=32MB` (สำหรับการประมวลผลคิวรีเชิงพื้นที่และ Sort ข้อมูลในแต่ละ Session)

---

## 2. การควบคุมหน่วยความจำและความเสถียรของ Redis Cache

Redis ทำหน้าที่เป็นความเร็วต้นของระบบ (Speed Layer) สำหรับสืบค้นพิกัดล่าสุดและการจัดคิว Distributed Lock

- **ตำแหน่งตั้งค่า:** เซอร์วิส `redis` ในไฟล์ [docker-compose.yml](../../docker-compose.yml)
- **พารามิเตอร์สำคัญ:**
  - `--maxmemory 256mb`: กำหนดขีดจำกัดหน่วยความจำสูงสุดที่ 256 เมกะไบต์
  - `--maxmemory-policy volatile-lru`: ตั้งค่าให้ Redis ขับไล่เฉพาะคีย์ที่มีการตั้งค่าวันหมดอายุ (TTL) เท่านั้น เพื่อป้องกันไม่ให้ระบบลบคีย์สำคัญที่ไม่มี TTL ทิ้ง หรือเตะคีย์ของ RedLock ทิ้งก่อนหมดอายุจริงอันส่งผลให้อาจแจกงานไรเดอร์ซ้ำซ้อน
- **แนวทางจูนแต่ง:**
  - หากจำนวนไรเดอร์ออนไลน์พุ่งทะลุ 10,000 คนพร้อมกัน ข้อมูลประวัติพิกัดสดและ Lock อาจกินแรมเกิน 256MB ให้ขยายค่านี้ขึ้นเป็น `1gb` หรือ `2gb` เพื่อป้องกันสภาวะ **Eviction Spikes** (การเตะคีย์ประวัติพิกัดและ Distributed Lock ทิ้งกลางคัน ซึ่งจะทำให้ระบบประมวลผลจัดส่งรวน)
  - แนะนำเป็นอย่างยิ่งให้แยก Redis Instance ระหว่าง Cache ทั่วไป กับ Distributed Lock (RedLock) ออกจากกันในระดับการรันจริงเพื่อป้องกันผลกระทบซึ่งกันและกัน (ดูเพิ่มเติมใน [REDIS-SETUP.md](../setup/REDIS-SETUP.md))

---

## 3. การจูนแต่งทรัพยากรระดับ Docker Containers (Resource Allocation)

ใน Production แนะนำให้กำหนด Resource Limits บน Docker Compose เพื่อป้องกันไม่ให้เกิดสภาวะ OOM (Out of Memory) หรือมีเซอร์วิสใดเซอร์วิสหนึ่งสูบพลังงาน CPU จนทำให้ตัวอื่นล่มตามไปด้วย

- **ตำแหน่งตั้งค่าการกำหนดลิมิต:** เพิ่ม Block `deploy` เข้าไปในแต่ละบริการของ [docker-compose.yml](../../docker-compose.yml)
- **ตัวอย่างการปรับจูนทรัพยากร:**

```yaml
services:
  backend:
    # ...
    deploy:
      resources:
        limits:
          cpus: '2.0'
          memory: 2G
        reservations:
          cpus: '0.5'
          memory: 512M

  ai-service:
    # ...
    deploy:
      resources:
        limits:
          cpus: '4.0' # OR-Tools มีพฤติกรรมใช้ CPU หลายแกนหนักหน่วงในการหา VRP
          memory: 4G
        reservations:
          cpus: '1.0'
          memory: 1G
```

---

## 4. การจัดการทราฟฟิกและอัตราการส่งข้อมูล (Nginx & API Rate Limiting)

เพื่อป้องกันการโดนยิงถล่ม (DDoS) และควบคุมความถี่การส่งตำแหน่งของไรเดอร์ไม่ให้เกินขีดจำกัด ระบบมีการทำ Rate Limiting 2 จุด:

### 4.1 Nginx Reverse Proxy Rate Limiting
- **ตำแหน่งตั้งค่า:** ไฟล์ `nginx-proxy/nginx.conf` (ถ้ามี) หรือกำหนดใน Gateway
- **แนวทางปฏิบัติ:**
  - ตั้งค่า `limit_req_zone` สำหรับจำกัดคำร้องขอจากไอพีต้นทาง
  - ควบคุมเฉพาะ Endpoint ของพิกัด GPS เช่น `/api/v1/telemetry/gps` ให้อยู่ที่สูงสุด **2 Requests/sec** ต่อหนึ่งไรเดอร์ เพื่อตัดการหน่วงของสคริปต์สแปม

### 4.2 .NET 8 Rate Limiting Middleware
- **ตำแหน่งตั้งค่า:** มีการเรียกใช้ใน [ApplicationSetup.cs](../../BackendApi/Setup/Extensions/ApplicationSetup.cs#L107) และประกาศนโยบายใน [ServiceSetup.cs](../../BackendApi/Setup/Extensions/ServiceSetup.cs)
- **รูปแบบการทำงาน:**
  - การใช้งานสิทธิทั่วไป (Rider/Customer): ตั้งข้อจำกัดแบบ **Fixed Window** หรือ **Sliding Window** ราย User ID
  - ตัวอย่างการตั้งจูนพารามิเตอร์: จำกัดที่ 100 คำขอต่อ 1 นาที หากเกินจะคืนรหัสข้อผิดพลาด HTTP `429 Too Many Requests`

---

## 5. การสเกลแบบ Multi-instance กับความปลอดภัยในการรัน Migration (Auto-Migration Deadlock Risk)

เมื่อทำการขยาย Backend API เป็นหลาย Instances (เช่น สเกลเป็น 5 containers/VM processes) เพื่อรองรับโหลดที่เพิ่มมากขึ้น:

> [!WARNING]
> ### ⚠️ จุดระวัง: ดาบสองคมของ Auto-Migration (Tech Lead's Warning)
> 
> **วิเคราะห์:**  
> ในโค้ดการสตาร์ตระบบของ Development Environment มีการรันคำสั่ง `app.MigrateDatabaseAsync()` เพื่อทำ Baseline check และ Migrate ฐานข้อมูลอัตโนมัติ ซึ่งมีประสิทธิภาพยอดเยี่ยมมากสำหรับการรันเครื่องนักพัฒนาแบบ Single Instance 
> 
> แต่ในการทำ Production Deployment จริง หากเราเปิดใช้ Auto-Migration ในทุกเครื่องพร้อมๆ กัน เมื่อเกิดการทำ Rollout/Scale-up ทั้ง 5 Instances จะถูกบูตขึ้นมาแทบจะพร้อมกัน และรันคำสั่ง `Database.MigrateAsync()` แย่งกันรันคิวรีสร้างตารางหรือเปลี่ยนคอลัมน์ (Race Condition ระดับฐานข้อมูล) ส่งผลให้เกิด **Deadlock** และตารางล็อกขึ้นในระบบ PostgreSQL ซึ่งทำให้อินสแตนซ์ขัดข้องบูตไม่ขึ้น
> 
> **แนวทางปฏิบัติ (Action Items):**
> 1. **ปิดการทำงานของ Auto-Migration บน Startup ของ App:** ควบคุมการรัน `app.MigrateDatabaseAsync()` ด้วยค่าสภาพแวดล้อม เช่น `RUN_MIGRATIONS_ON_STARTUP=false` บน Production
> 2. **แยกการรัน Migration ไปอยู่นอกตัวแอปหลัก:**
>    - รันผ่าน **CI/CD Pipeline (Single Runner Job)** ก่อนเริ่มกระบวนการ Deploy ตัวแอปใหม่
>    - ใช้ **one-shot migration runner** ที่ผูกอยู่กับ Database Migration CLI (เช่นใช้ `dotnet ef bundle` หรือคอนเทนเนอร์ตัวเดียวที่ตั้งค่ารัน Migration) รันให้เสร็จเรียบร้อยก่อนแอปพลิเคชันเวอร์ชันใหม่ใน instances อื่นจะเริ่มบูต

