# Road Test Workspace

พื้นที่จัดเก็บการตั้งค่า สคริปต์ และคู่มือสำหรับการทดสอบระบบวิ่งบนถนนจริง (Real Road Test) ด้วยโทรศัพท์ Android จริง พิกัด GPS จริง และโครงข่าย 4G/5G

> ⚠️ **คำเตือนด้านสถาปัตยกรรม (Architectural Rule):**  
> โฟลเดอร์ `road-test/` เป็น **Test Workspace / Tooling Area เท่านั้น** ไม่ใช่โปรเจกต์แยก และ**ห้ามทำสำเนาโค้ด (Duplicate Implementation)** ของ Service/Logic จากแอปพลิเคชันหลักเข้ามาใส่ในโฟลเดอร์นี้เด็ดขาด

---

## โครงสร้างโฟลเดอร์ (Directory Structure)

```text
road-test/
├── README.md                  # คำอธิบายภาพรวมและกฎการใช้งาน
├── docker/
│   └── docker-compose.test.yml # Docker Compose สำหรับรัน Test Server (ไม่มี Mock GPS)
├── config/
│   └── .env.test.example      # ตัวอย่าง Environment Variables สำหรับ Road Test
├── scripts/
│   ├── start-test.sh          # สคริปต์เริ่มการทำงานของ Test Server
│   ├── stop-test.sh           # สคริปต์หยุดการทำงาน
│   ├── health-check.sh        # สคริปต์ตรวจความพร้อมของ Service ทั้งหมด
│   └── reset-test-data.sh     # สคริปต์เคลียร์ข้อมูลพิกัดทดสอบ
└── docs/
    ├── 01-server-setup.md     # คู่มือเตรียม Server & Network Tunnel
    ├── 02-android-setup.md    # คู่มือ Build และติดตั้ง APK บนมือถือ
    ├── 03-gps-test.md         # ขั้นตอนทดสอบพิกัด GPS จริงเบื้องต้น (Stationary & Walking)
    ├── 04-offline-test.md     # ขั้นตอนทดสอบ Offline Buffer & Reconnection
    └── 05-road-test.md        # ขั้นตอนทดสอบวิ่งบนถนนจริง (Driving & Background)
```

---

## ขั้นตอนการเริ่มใช้งานเบื้องต้น (Quick Start)

1. **คัดลอกไฟล์ตั้งค่า Environment:**
   ```bash
   cp road-test/config/.env.test.example .env
   # แก้ไขรหัสผ่านและค่าคอนฟิกให้ตรงกับสภาพแวดล้อมของคุณ
   ```

2. **สั่งรันระบบทดสอบผ่าน Docker:**
   ```bash
   docker compose -f docker-compose.yml -f road-test/docker/docker-compose.test.yml up -d
   ```

3. **ตรวจเช็คความพร้อมของระบบ (Health Check):**
   ```bash
   bash road-test/scripts/health-check.sh
   ```

4. **อ่านคู่มือการทดสอบตามลำดับในโฟลเดอร์ `road-test/docs/`**
