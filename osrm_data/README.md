# OSRM Map Data & Setup Reference (osrm_data/README.md)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือสำหรับนักพัฒนาและผู้ดูแลระบบ เพื่อบริหารจัดการข้อมูลดิบแผนที่และขั้นตอนการคอมไพล์แผนที่จราจรออฟไลน์ (**Open Source Routing Machine - OSRM**) สำหรับใช้งานในการหาเส้นทางถนนจริงในจังหวัดอุดรธานี

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
`osrm_data/` ทำหน้าที่เป็นคลังจัดเก็บไฟล์แผนที่ถนน (Map Geometry Assets) ที่ผ่านกระบวนการคัดกรอง แยกถนนย่อย และคำนวณกราฟล่วงหน้า (Pre-computed Dijkstra Graphs) สำหรับตู้บริการนำทาง `delivery-osrm` โดยเอนจิน OSRM จะนำกราฟนี้ไปใช้วิเคราะห์ระยะทาง เวลาเดินทาง (ETA) และ Snap พิกัด Rider เข้าสู่กึ่งกลางถนนที่ความหน่วงต่ำกว่า **200ms**

---

## 2. ดัชนีไฟล์ฐานข้อมูลจราจร (Map Data Assets Index)

เมื่อรันคอมไพล์แผนที่สำเร็จ โฟลเดอร์นี้จะประกอบด้วยไฟล์เชิงภูมิศาสตร์รวม **28 ไฟล์ย่อย** ดังนี้:

*   **`udon-thani.osm.pbf` (ข้อมูลดิบ):** ข้อมูลแผนที่ดิบประเทศไทยจาก Geofabrik ขนาด ~320MB ประกอบด้วยพิกัด GPS แนวเส้นตึก เส้นถนน และจุดแวะพักต่างๆ
*   **`udon-thani.osrm` (คอร์กราฟ):** ไฟล์กราฟหลักที่ใช้สร้างโครงข่าย Dijkstra
*   **`udon-thani.osrm.edges` & `nbg_nodes` (โครงข่ายถนน):** ข้อมูลจุดตัดทางแยก (Nodes) และเส้นถนนเชื่อม (Edges)
*   **`udon-thani.osrm.geometry` (รูปทรงทางภูมิศาสตร์):** รูปร่างแนวโค้งทางเดินถนนจริง (ใช้สำหรับวาดเส้น Polyline บนแผนที่นำทางคนขับ)
*   **`udon-thani.osrm.names` (ชื่อป้ายถนน):** รายละเอียดชื่อซอย ถนนหลัก สะพานข้ามแยก สำหรับถอดความแนวคำสั่งนำทาง
*   **`udon-thani.osrm.turn_penalties_index` (ข้อจำกัดเลี้ยว):** น้ำหนักความหน่วงกรณีเลี้ยวขวา เลี้ยวซ้าย หรือจุดห้ามกลับรถ

---

## 3. วิธีการดาวน์โหลดและบิวด์แผนที่ (Setup & Compile Steps)

เรามีสคริปต์อัตโนมัติจัดเตรียมไว้ให้เพื่อรัน Toolchain ทุกขั้นตอน (ดาวน์โหลด $\rightarrow$ สกัด $\rightarrow$ แบ่งเซลล์ $\rightarrow$ ตกแต่งสเปก):

### 🚀 รันผ่านสคริปต์อัตโนมัติ (แนะนำ)
*   **ระบบ Windows (PowerShell):**
    ```powershell
    # รันสคริปต์ที่รูทโปรเจกต์หลัก
    .\scripts.test\setup-osrm.ps1
    ```
*   **ระบบ Linux / macOS (Bash):**
    ```bash
    chmod +x ./scripts.test/setup-osrm.sh
    ./scripts.test/setup-osrm.sh
    ```

---

## 4. โครงสร้างบริการระดับ Docker Compose (Service Setup)

บริการถูกติดตั้งพอร์ตและการเข้าถึงไว้ใน [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml) ด้วยคำสั่งการรันนี้:

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

*   **Volume Mount:** เชื่อมโยงไดเรกทอรีโฮสต์ `./osrm_data` เข้ากับ `/data` ในคอนเทนเนอร์เพื่อส่งผ่านกราฟ
*   **Routing Command:** เรียกคำสั่งรันระบบนำทางแบบ Multi-Level Dijkstra (MLD) คลุมพอร์ตภายใน `5000` และปล่อยพอร์ตเชื่อมต่อออกภายนอกตู้ที่ **`5001`**

---

## 5. วิธีการทดสอบผลลัพธ์ผ่าน Endpoint (Sandbox Testing)

เมื่อรันตู้บริการ OSRM สำเร็จ คุณสามารถตรวจสอบสุขภาพการตอบกลับของแผนที่จราจรเมืองอุดรธานีได้โดยเรียกทดสอบผ่าน API:

1.  เปิดบราวเซอร์หรือส่งคำขอ GET ไปยังพอร์ต `5001`:
    ```http
    GET http://localhost:5001/route/v1/driving/102.7872,17.4138;102.7932,17.4188?overview=full&geometries=geojson
    ```
2.  **ตัวอย่างคำตอบรับ JSON ที่ถูกต้อง (200 OK):**
    ```json
    {
      "code": "Ok",
      "routes": [
        {
          "geometry": {
            "coordinates": [[102.7872, 17.4138], [102.7885, 17.4149], ...],
            "type": "LineString"
          },
          "legs": [],
          "distance": 850.4,
          "duration": 65.2
        }
      ],
      "waypoints": [...]
    }
    ```
    *(ระยะทาง `distance` จะคืนเป็นหน่วยเมตร และ `duration` คืนเป็นวินาที)*

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Context)
*   [OSRM Offline Map Compiler & Setup Guide (เชิงลึก)](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/OSRM-SETUP.md)
*   [GeoJSON Coordinate Standard Rules](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/contracts/geojson-contracts.md)
