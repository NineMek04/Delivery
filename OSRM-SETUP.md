# 🗺️ OSRM Offline Map Compiler & Setup Guide

เอกสารคู่มือสำหรับการดาวน์โหลด ติดตั้ง และกำหนดค่าเครื่องยนต์ประมวลผลเส้นทางแบบออฟไลน์ (**Open Source Routing Machine - OSRM**) เพื่อใช้งานกับระบบจราจรอัจฉริยะ **Smart Delivery Routing System** บนพื้นที่จังหวัดอุดรธานี และประเทศไทย

---

## 1. บทนำและสถาปัตยกรรม (Overview & Architecture)

ในระบบ **Smart Delivery Routing System** การคำนวณระยะทางและพิกัดถนนจริงสำหรับจัดส่งสินค้าแบบวินาทีต่อวินาที (Real-road Dijkstra Routing) จำเป็นต้องมีประสิทธิภาพสูงมาก (Latency < 200ms) เพื่อหลีกเลี่ยงคอขวดบนฐานข้อมูลและคงประสิทธิภาพของเครื่องยนต์ AI (VRP Solver)

ระบบของเราออกแบบมาโดยใช้สถาปัตยกรรมนำร่องระบบขนส่งออฟไลน์ความเร็วสูงควบคู่ไปกับแผนสำรองออนไลน์แบบยืดหยุ่น:

```
                  ┌───────────────────────────────┐
                  │   API Routing Request         │
                  └──────────────┬────────────────┘
                                 │
                     ┌───────────▼───────────┐
                     │ 1. Check Redis Cache  │ (TTL 24 Hours)
                     └───────────┬───────────┘
                                 │ Cache Miss
                     ┌───────────▼───────────┐
      ┌──────────────┤ 2. Query Local OSRM   ├──────────────┐
      │ Success      └───────────┬───────────┘              │ Fail (Timeout / Offline)
      │ (Offline Engine Port 5001)│                          │
┌─────▼──────────┐               │                          ▼
│ Returns Route  │               │              ┌────────────────────────┐
└────────────────┘               │              │ 3. Public OSRM API     │ (Dijkstra Online Fallback)
                                 │              └──────────┬─────────────┘
                                 │                         │
                                 │       Success           │ Fail (完全 Offline)
                                 │                         ▼
                                 │              ┌────────────────────────┐
                                 │              │ 4. Haversine Straight  │ (Emergency fallback)
                                 │              └────────────────────────┘
                                 │
              ┌──────────────────▼──────────────────┐
              │ 5. Google Polyline Compression (99%)│
              ├─────────────────────────────────────┤
              │ 6. Write Cache & Return coordinates │
              └─────────────────────────────────────┘
```

---

## 2. ขั้นตอนการติดตั้งและบิวต์แผนที่ออฟไลน์ (Step-by-Step Compiling Guide)

ข้อมูลแผนที่ดิบนำมาจาก **Geofabrik (Thailand OpenStreetMap - `.osm.pbf`)** ซึ่งมีขนาดประมาณ 180MB - 320MB และนำเข้าสู่ระบบ Docker OSRM Toolchain เพื่อคอมไพล์แปลงสภาพโครงข่ายทางด่วน ทางหลัก ซอยย่อย เป็นกราฟ Dijkstra ขนาดใหญ่สำหรับนำร่อง

### 📥 1. เตรียมระบบและรันสคริปต์อัตโนมัติ

เรามีสคริปต์อัตโนมัติที่ Stripped UTF-8 Emojis ออกแล้ว (ป้องกันการค้างบน Windows PowerShell) รวมถึงเพิ่มคำสั่ง `--user root` เพื่อหลีกเลี่ยง Permission Block บน Docker Volumes

* **ระบบปฏิบัติการ Windows (PowerShell):**
  เปิด PowerShell ในโฟลเดอร์โปรเจกต์หลัก แล้วรันสคริปต์:
  ```powershell
  .\scripts.test\setup-osrm.ps1
  ```

* **ระบบปฏิบัติการ Linux / macOS (Bash):**
  ให้สิทธิ์การรันแล้วเรียกใช้งาน:
  ```bash
  chmod +x ./scripts.test/setup-osrm.sh
  ./scripts.test/setup-osrm.sh
  ```

---

### ⚙️ 2. รายละเอียดขั้นตอนการทำงานของ OSRM Toolchain (เบื้องหลังการรัน)

หากต้องการรันคำสั่งทีละขั้นตอนด้วยตนเอง สามารถป้อนคำสั่งผ่าน Docker CLI ตามลำดับต่อไปนี้:

#### 📍 Phase A: ดาวน์โหลดข้อมูลแผนที่ประเทศไทยจาก Geofabrik
ดาวน์โหลดแผนที่ถนนของประเทศไทยล่าสุด:
```bash
curl -L -o ./osrm_data/udon-thani.osm.pbf https://download.geofabrik.de/asia/thailand-latest.osm.pbf
```

#### 📍 Phase B: การสกัดข้อมูลโครงข่ายถนน (osrm-extract)
คำสั่งสกัดและแยกประเภทเฉพาะประเภทรถยนต์ส่วนบุคคล (`car.lua`) จากข้อมูลดิบ:
```bash
docker run --rm --user root -v "$(pwd)/osrm_data:/data" osrm/osrm-backend osrm-extract -p /usr/local/share/osrm/profiles/car.lua /data/udon-thani.osm.pbf
```

#### 📍 Phase C: การแบ่งส่วนเซลล์ถนนจราจร (osrm-partition)
แบ่งแผนที่ออกเป็นพื้นที่ย่อยเพื่อเร่งอัลกอริทึมการค้นหา Dijkstra:
```bash
docker run --rm --user root -v "$(pwd)/osrm_data:/data" osrm/osrm-backend osrm-partition /data/udon-thani.osrm
```

#### 📍 Phase D: การนำร่องความเร็วระดับไมโครวินาที (osrm-customize)
คำนวณและเก็บข้อมูลการเดินทางของถนนเลนย่อยแต่ละเส้นทางเพื่อความแม่นยำสูงสุด:
```bash
docker run --rm --user root -v "$(pwd)/osrm_data:/data" osrm/osrm-backend osrm-customize /data/udon-thani.osrm
```

> [!IMPORTANT]
> ขั้นตอนการทำงานทั้งหมดจะสร้างผลลัพธ์เป็นไฟล์ฐานข้อมูลประมวลผลเส้นทางรวม **23 ไฟล์ย่อย** ภายในโฟลเดอร์ `./osrm_data` เช่น `udon-thani.osrm.edges`, `udon-thani.osrm.names`, และ `udon-thani.osrm.geometry` โดยอัตโนมัติ

---

## 3. การรันบริการแผนที่ออฟไลน์บน Docker Compose (Service Deployment)

บริการ OSRM ได้รับการผนวกไว้ในโครงสร้างการปรับใช้ระบบไมโครเซอร์วิสแล้ว ในไฟล์ [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml):

```yaml
  osrm:
    image: osrm/osrm-backend
    container_name: delivery-osrm
    user: root
    restart: always
    ports:
      - "5001:5000"
    volumes:
      - ./osrm_data:/data
    command: osrm-routed --algorithm mld /data/udon-thani.osrm
    networks:
      - delivery-network
```

### 🚀 วิธีรันบริการออฟไลน์:

1. รีสตาร์ตตู้ OSRM เพื่อให้ดึงไฟล์ไบนารีจราจร 23 ไฟล์ที่เพิ่งประมวลผลเสร็จใหม่ขึ้นมาทำงาน:
   ```bash
   docker-compose restart osrm
   ```

2. ตรวจสอบล็อกความพร้อมใช้งานของออฟไลน์เอนจิน:
   ```bash
   docker logs delivery-osrm
   ```
   **Output ที่ถูกต้องเมื่อพร้อมใช้งาน:**
   ```text
   [info] File: /data/udon-thani.osrm.properties, size: 6144 bytes
   [info] File: /data/udon-thani.osrm.edges, size: 128740864 bytes
   [info] running queries using Multi-Level Dijkstra (MLD)
   [info] Listening on 0.0.0.0:5000
   ```

---

## 4. โครงสร้างความน่าเชื่อถือและการบีบอัดข้อมูลฝั่งหลังบ้าน (Backend Implementation)

### 🛡️ 1. อัลกอริทึม Resilience Dijkstra Fallback
คลาส [OsrmRoutingService.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Services/Ai/OsrmRoutingService.cs) ฝั่งหลังบ้านได้รับการติดตั้ง **Polly Policy (2 Retries + 15s Circuit Breaker)** เพื่อตัดการเชื่อมต่อทันทีที่ OSRM ออฟไลน์สะดุดและส่งต่อไปยัง Public OSRM อัตโนมัติ ป้องกันไม่ให้ออเดอร์ค้างหรือ API โหลดช้า:

```csharp
// สลับเรียกออฟไลน์ท้องถิ่นหรือออนไลน์ตามความพร้อม
var url = $"{_localOsrmUrl}/route/v1/driving/{lng1},{lat1};{lng2},{lat2}?overview=full&geometries=geojson";
try
{
    response = await _httpClient.GetAsync(url);
}
catch
{
    var publicUrl = $"http://router.project-osrm.org/route/v1/driving/{lng1},{lat1};{lng2},{lat2}?overview=full&geometries=geojson";
    response = await _httpClient.GetAsync(publicUrl);
}
```

### ⚡ 2. เทคนิคการบีบอัดพิกัดจราจร Google Polyline 99%
เพื่อป้องกันการสิ้นเปลืองแบนด์วิดท์เครือข่ายและขนาดของฐานข้อมูล เส้นทางล้านพิกัดจะถูกบีบอัดด้วยคลาส `PolylineEncoder.cs` ให้เหลือเพียงสตริงสั้นๆ (`_p~iF~ps|U...`) ก่อนบันทึกลงฟิลด์ `EncodedPolyline` ใน PostgreSQL:

* **ก่อนบีบอัด:** JSON ขนาด **22.5 KB** สำหรับอาร์เรย์พิกัด `[[17.41, 102.78], ...]`
* **หลังบีบอัด:** ตัวอักษร ASCII เพียง **412 ไบต์** (ลดพื้นที่เกือบ **99%**)

---

## 5. ขั้นตอนการรันเทสแบบกระชับ (Quick Developer Sandbox Verification)

สำหรับนักพัฒนาในทีมที่ต้องการเริ่มรันเทสระบบแผนที่จราจรและโครงข่ายนำร่องจริง (End-to-End Sandbox) ภายในเวลาอันรวดเร็ว สามารถทำตามลำดับขั้นตอนสั้นๆ ดังนี้:

### ⚙️ Step 1: สตาร์ตฐานระบบและเครื่องยนต์นำร่องออฟไลน์
ตรวจสอบความเรียบร้อยของ Docker Container และสตาร์ต OSRM:
```powershell
# 1. รันระบบ Docker Microservices ทั้งหมด
docker-compose up -d

# 2. บิวด์ข้อมูลถนนจังหวัดอุดรธานี (ทำครั้งแรกครั้งเดียว)
.\scripts.test\setup-osrm.ps1

# 3. รีสตาร์ตตู้แผนที่ออฟไลน์ให้ดึงฐานข้อมูลใหม่ขึ้นมาทำงาน
docker-compose restart osrm
```

### 💻 Step 2: เปิด Cockpit Dashboard ประเมินจราจรสด
เปิดเว็บเบราว์เซอร์แล้วไปที่:
* **Admin Dashboard:** `http://localhost/`
* เข้าสู่ระบบด้วยบัญชีแอดมินจำลอง แล้วไปที่เมนู **"Live Map"**

### 🏍️ Step 3: ทริกเกอร์ Sandbox จำลองอัจฉริยะ
ทริกเกอร์ระบบสร้างออเดอร์และการขนส่งจำลองอัจฉริยะ E2E:
```powershell
node .\scripts.test\e2e-simulator\simulate-e2e.js
```

### 🎬 สิ่งที่ต้องเกิดขึ้นและสังเกตในห้องทดลอง (Verification Checkpoints):
* **[ ]** ตู้ OSRM ได้รับทราฟฟิกรวดเร็วในระดับไมโครวินาที (เช็กด้วย `docker logs delivery-osrm`)
* **[ ]** หมุดของไรเดอร์และร้านค้าประกายไฟกะพริบแจ้งเตือนจับคู่แบบเรียลไทม์ผ่าน **SignalR**
* **[ ]** เส้นทางเดินรถจัดส่งจากร้านไปหาลูกค้าวาดเป็น **"เส้นถนนโค้งจริงตามไหล่ทางภูมิศาสตร์อุดรธานี"**
* **[ ]** ตัวนำร่องขยับหมุดไรเดอร์เกาะเลี้ยวเลาะตามแนวโค้งจราจรจริงแบบ Smooth ตลอดช่วง E2E

---

💡 *เอกสารคู่มือจัดทำโดยทีมพัฒนาระบบขนส่งอัจฉริยะ Smart Delivery Routing System*
