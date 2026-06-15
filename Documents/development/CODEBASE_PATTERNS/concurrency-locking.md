# 🔒 การจัดการสภาวะแข่งขันและการจำกัดสิทธิ์เขียนข้อมูล (Race Condition & Concurrency Locking)

ระบบต้องเผชิญหน้ากับคำร้องขอสูงมาก (High Concurrency) โดยเฉพาะจากไรเดอร์นับร้อยคนที่รุมแย่งกันกดรับออเดอร์เดียวกันพร้อม ๆ กัน ซึ่งระบบป้องกันการเกิด Race Condition และทับซ้อนข้อมูลด้วยระบบล็อก 3 ชั้น:

### 🟢 1. ระบบล็อกแบบกระจายข้ามเซิร์ฟเวอร์ (Distributed Lock via Redis)
- จัดการผ่าน [RedisLockService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Infrastructure/Redis/RedisLockService.cs)
- ใช้คำสั่งแบบอะตอมมิก `SETNX` พร้อมกำหนดระยะเวลากดจองไรเดอร์/ออเดอร์ (TTL 30 วินาที)
- **Safe Release (Lua script):** การปลดล็อกจะสแกนและรันด้วย Lua Script เพื่อเช็คว่าคีย์ดังกล่าวถือครองโดย Offer ID เดิมจริงหรือไม่ ป้องกันผู้ใช้รายอื่นเผลอไปปลดล็อกแทนกัน
  ```lua
  if redis.call('get', KEYS[1]) == ARGV[1] then
      return redis.call('del', KEYS[1])
  else
      return 0
  end
  ```

### 🟠 2. ระบบล็อกสำรองเมื่อ Redis ล่ม (PostgreSQL Failover Lock)
- หากมีเหตุขัดข้องไม่สามารถเชื่อมต่อ Redis ได้ ตัวระบบ `RedisLockService` จะทำงานโหมด Failover สลับมาใช้ PostgreSQL โดยอัตโนมัติ ผ่านตาราง `DistributedLocks` ด้วยคำสั่ง **Atomic UPSERT Query**:
  ```sql
  INSERT INTO "DistributedLocks" ("LockKey", "Value", "ExpiresAt")
  VALUES ({0}, {1}, {2})
  ON CONFLICT ("LockKey")
  DO UPDATE SET 
      "Value" = EXCLUDED."Value",
      "ExpiresAt" = EXCLUDED."ExpiresAt"
  WHERE "DistributedLocks"."ExpiresAt" <= {3}
     OR "DistributedLocks"."Value" = EXCLUDED."Value";
  ```

### 🔴 3. การตรวจสอบสภาวะเขียนทับซ้อน (PostgreSQL xmin Shadow Property Concurrency Control)
- **กลไกการทำงาน:**  
  ระบบใช้นโยบายการทำงานร่วมกับระบบแถวฐานข้อมูล PostgreSQL ผ่านตัวระบุระบบ **`xmin`** (System Column ที่เก็บ ID ทรานแซกชันล่าสุดที่แก้ไขแถวดังกล่าว) ในฐานะ **EF Core Shadow Property**
- **การตั้งค่าระดับ DbContext ([ApplicationDbContext.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Data/ApplicationDbContext.cs#L246-L248)):**
  ```csharp
  modelBuilder.Entity(entityType.ClrType)
      .Property<uint>("xmin")
      .HasColumnName("xmin")
      .IsRowVersion();
  ```
  *คำอธิบาย:* สำหรับทุกตารางหลักที่สืบทอดมาจาก `BaseEntity<>` ระบบจะทำการผูกคีย์ shadow `"xmin"` และผูกใช้งานเป็น Concurrency Token ผ่านคำสั่ง `.IsRowVersion()` โดยตรง ส่วนคอลัมน์สาธารณะ `RowVersion` ชนิดข้อมูล `bytea` (ซึ่งบังคับใส่ค่าปริยายเป็น `\x` ใน [PostgresAdvancedConfigurator.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/ServiceMigration/PostgresAdvancedConfigurator.cs#L181)) จะคงไว้เพื่อการทำงานประสานกับตัวแปรส่งต่อข้ามระบบ (Public API compatibility) เท่านั้น
- **การดักจับข้อผิดพลาด:**  
  หากมีการแย่งกันอัปเดตข้อมูลแถวเดียวกันพร้อมกันจนส่งผลให้ค่า `xmin` เปลี่ยนแปลงไปก่อนหน้า ตัว EF Core จะสั่งยุติและพ่นข้อผิดพลาด `DbUpdateConcurrencyException` ออกมาทันที (เช่นตัวจัดการใน [DispatchOfferHandler.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Features/DispatchManagement/DispatchOfferHandler.cs#L124)) ระบบจะดักจับตัวเออเรอร์เพื่อเรียกทำการ rollback ทรานแซกชันอย่างเสถียรและส่งกลับสถานะ `false` เพื่อความปลอดภัยข้อมูล


### 🔵 4. ข้อควรระวังกับ PgBouncer ใน Transaction Pooling Mode
- ในการจำลองสเกลฐานข้อมูล ระบบปิด Prepared Statements ที่ Connection String ของฝั่ง .NET:
  `No Reset On Close=true;Max Auto Prepare=0;`
  เนื่องจากบอท PgBouncer ในโหมด transaction pooling จะปันสายเชื่อมต่ออย่างรวดเร็ว หากมี prepared statement ค้างอยู่บน session เก่าจะส่งผลให้เซสชันอื่นทำงานไม่ได้ทันที
