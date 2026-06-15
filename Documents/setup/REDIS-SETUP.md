# Redis Cache & Lock Database Manual (Documents/setup/REDIS-SETUP.md)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือการตั้งค่า กำหนดคีย์ข้อมูล (Key Design) และการจัดการระบบล็อกจำหน่ายงาน (**Redis Cache & Distributed Lock**) สำหรับนักพัฒนาและผู้ดูแลระบบ

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
`redis` ทำหน้าที่เป็นผู้ช่วยความเร็วระดับ RAM (Operational State Cache):
1.  **High-Frequency Buffering:** รับพิกัด GPS สดของไรเดอร์ที่มีการยิงเข้ามาถี่ๆ มาเก็บไว้ในแรม ก่อนจะนำส่งไปจัดเก็บลงดิสก์ PostgreSQL
2.  **Rider Presence Tracker:** ตรวจสอบความตื่นตัว (Heartbeats) ของ Rider เพื่อตัดสินระดับความสามารถในการพร้อมวิ่งงานแบบเรียลไทม์
3.  **Distributed Locking (RedLock):** ควบคุมการยอมรับสิทธิ์เสนอออเดอร์งานของ Rider ไม่ให้เกิดสถานการณ์คนขับสองคนกดยยอมรับงานเดียวกันในเสี้ยววินาทีเดียวกัน (Race Conditions)

---

## 2. โครงสร้างคีย์ข้อมูลหลัก (Core Keys Specification)

รายละเอียดของสัญญากำหนดการสร้างคีย์ถูกระบุใน [.docs/ai-context/contracts/redis-keys.md](../../.docs/ai-context/contracts/redis-keys.md):

*   **Rider Presence Key:** `rider:presence:<rider_id>`  
    -   *ชนิดข้อมูล:* String  
    -   *ค่าที่เก็บ:* `"ONLINE"`  
    -   *อายุคีย์ (TTL):* 30 วินาที (คนขับต้องส่ง Heartbeat มาอัปเดตทุกๆ 10-15 วินาที หากหายไปเกิน 30 วินาที คีย์จะหายไป และ `HeartbeatMonitor.cs` จะเปลี่ยนสถานะคนขับเป็น OFFLINE ในฐานข้อมูลหลัก)
*   **Rider Location Cache:** `rider:location:<rider_id>`  
    -   *ชนิดข้อมูล:* Hash (ละติจูด, ลองจิจูด, ความแม่นยำ, ทิศทางรถ) หรือใช้ Redis Geospatial Data (`GEOADD`)  
    -   *อายุคีย์ (TTL):* 1 วัน (24 ชั่วโมง) เพื่อคอยตรวจสอบพิกัดล่าสุด
*   **Order Offering Lock:** `lock:order:<order_id>`  
    -   *ชนิดข้อมูล:* String  
    -   *ค่าที่เก็บ:* รหัสพนักงานขับรถ (`rider_id`)  
    -   *อายุคีย์ (TTL):* 15 วินาที (สอดคล้องกับรอบเวลาตัดสินใจจัดส่ง หากคนขับยอมรับงานสำเร็จ ล็อกจะค้างอยู่จนจบงาน หากหมดเวลาล็อกจะเคลียร์ออกให้อัตโนมัติเพื่อเปิดโอกาสเสนอคิว Rider คนถัดไป)

---

## 3. ระบบล็อกกระจายงานประสิทธิภาพสูง (Distributed Locking / RedLock)

> [!CAUTION]
> **วิกฤต Redis Eviction ทับซ้อน (Cache vs. Locks)**
> ในสภาพแวดล้อมที่จำกัดหน่วยความจำ Redis (เช่น 256MB ในระดับการพัฒนา) และเปิดใช้ LRU Eviction Policy (เช่น `allkeys-lru`):
> **ความจริงอันโหดร้าย:** หากเราแชร์ Redis Instance เดียวกันเพื่อทำหน้าที่เก็บ Cache/Buffer ทั่วไป และเป็นที่จัดเก็บ Distributed Lock (RedLock) ของระบบเสนอออเดอร์ เมื่อแรมเต็ม 256MB นโยบาย LRU จะสุ่มลบคีย์ที่ไม่ได้ใช้งานล่าสุด ซึ่งอาจ **"เผลอลบคีย์ล็อกที่สำคัญ (`lock:order:<order_id>`) ทิ้งก่อนหมดอายุ TTL"** ผลคืออาจทำให้มีไรเดอร์กดยอมรับงานเดียวกันได้พร้อมกันและระบบเกิดการแจกงานซ้ำซ้อนทันที!
> 
> **แนวทางปฏิบัติเชิงวิศวกรรมที่บังคับ:**
> 1. **ปรับเปลี่ยน Eviction Policy เป็น `volatile-lru`:**
>    ต้องตั้งค่า `maxmemory-policy volatile-lru` ใน `redis.conf` เสมอ เพื่อบังคับให้ Redis ขับไล่เฉพาะคีย์ที่มีการตั้งค่าวันหมดอายุ (TTL) เท่านั้น และห้ามลบคีย์สำคัญที่ไม่ได้ระบุ TTL (ยกเว้นระบบ Lock ซึ่งมี TTL แต่จะได้รับการบริหารจัดการสระแรมอย่างใกล้ชิด)
> 2. **แนวทางการผลิตจริง (Production Recommendation):**
>    แยก Redis Instance ออกเป็น 2 วงอย่างเด็ดขาด:
>    - **Instance 1: Cache & GPS Buffer** (สามารถใช้นโยบาย `allkeys-lru` หรือ `volatile-lru` ได้ตามความเหมาะสมและตั้งค่า Max Memory สูงๆ)
>    - **Instance 2: Distributed Lock / Shared State** (ห้ามเปิดใช้ Eviction Policy หรือเลือกใช้ `noeviction` เพื่อรับประกันความแน่นอนของข้อมูล โดยหากแรมเต็มระบบจะพ่น Error แต่ล็อกจะไม่ถูกถอดออกก่อนหมดอายุ)

ประมวลผลผ่านโมดูลหลังบ้าน [RedisLockService.cs](../../BackendApi/Infrastructure/Redis/RedisLockService.cs):
*   **Mutual Exclusion:** เมื่อไรเดอร์กดยอมรับออเดอร์ (`AcceptOffer`), API จะสั่งยิง `AcquireLockAsync` เพื่อล๊อกคีย์ `lock:order:<order_id>` ทันที
*   **Race Conditions Prevention:** การเช็คและเขียนล๊อกรันผ่านคำสั่ง Lua Script อะตอมมิกระดับเดี่ยวของ Redis เพื่อรับประกันความแน่นอนของการเขียนสถานะ แม้ว่าคำขอ Accept จะวิ่งเข้ามาชนกันที่ระดับไมโครวินาที
*   **Emergency Local Fallback:** หากตู้ Redis เกิดขัดข้องออฟไลน์ไปกระทันหัน หลังบ้านจะทำการเปลี่ยนโหมด (Fallback Mode) ไปใช้ระบบล็อกฐานข้อมูล PostgreSQL (`SELECT FOR UPDATE`) ชั่วคราวอัตโนมัติ เพื่อรักษาความถูกต้องของธุรกรรม (Data integrity)

---

## 4. วิธีการขึ้นระบบและสั่งตรวจสอบแคช (Verification Steps)

1.  สตาร์ตระบบ Redis:
    ```bash
    docker-compose up -d redis
    ```
2.  เปิดหน้าต่างคำสั่งควบคุม (Redis CLI):
    ```bash
    docker exec -it delivery-redis redis-cli
    ```
3.  ตรวจสอบสภาพแวดล้อมและความปลอดภัยพอร์ต:
    -   รันคำสั่ง: `PING` $\rightarrow$ ระบบต้องตอบกลับว่า `PONG`
    -   รันคำสั่งสืบค้นคีย์ออนไลน์ของไรเดอร์: `KEYS rider:presence:*`
    -   รันคำสั่งดึงพิกัดภูมิศาสตร์: `GEOPOS riders 102.7872 17.4138` (กรณีเก็บพิกัดในโครงสร้าง Geospatial Index)

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Context)
*   [Redis Keys Specification and TTL Contracts](../../.docs/ai-context/contracts/redis-keys.md)
*   [Scale Guide & Performance Tuning Manual (SCALE-GUIDE.md)](../infrastructure/SCALE-GUIDE.md)
*   [Redis Distributed Lock Registry](../../CRITICAL-CODE-PROTECTION.md#L41)
