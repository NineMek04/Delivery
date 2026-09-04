# System Overview & Architecture Diagram (Documents/development/SYSTEM-OVERVIEW.md)

เอกสารนี้แสดงสถาปัตยกรรมระดับสูง (High-Level Architecture) และท่อระบายข้อมูล (Data Flows) ของระบบจัดส่งอัจฉริยะแบบเรียลไทม์ (Smart Delivery Routing System) เพื่อให้ทีมพัฒนาและแอดมินระบบสามารถทำความเข้าใจโครงสร้างและการทำงานร่วมกันของแต่ละโมดูลได้โดยไม่ต้องสืบค้นเพิ่มเติม

---

## 1. System Topology & Communication Diagram

ระบบทั้งหมดทำงานอยู่บนสภาพแวดล้อม Docker-compose โดยมีการตัดขาดพอร์ตของตู้ภายใน (Internal Services) ออกจากเครือข่ายภายนอกเพื่อความปลอดภัย และปล่อยให้ทราฟฟิกขาเข้าผ่านเพียง Nginx Reverse Proxy เท่านั้น

```mermaid
graph TD
    %% Clients
    AdminWeb["Angular Admin Dashboard (Port 4201 / 80)"] -->|HTTPS / SignalR| NginxProxy["Nginx Reverse Proxy (Port 8081)"]
    RiderApp["Flutter Rider App (Port 8083 / 80)"] -->|HTTPS / SignalR / Tiles| NginxProxy

    %% Proxy Routing
    NginxProxy -->|Proxy /api| BackendAPI[".NET 8 Web API (Port 5000)"]
    NginxProxy -->|Proxy /hubs/tracking| BackendAPI
    NginxProxy -->|Proxy /tiles| TileCache["OSM Tile Cache (Rider Nginx)"]

    %% Backend Layer
    BackendAPI -->|1. Fetch GIS Data| PostGIS["PostgreSQL + PostGIS (Port 5432)"]
    BackendAPI -->|2. Ingestion Cache / Lock| Redis["Redis Cache & Buffer (Port 6379)"]
    BackendAPI -->|3. Publish Integration Events| RabbitMQ["RabbitMQ Broker (Port 5672)"]

    %% Route Optimization & Navigation Engine
    BackendAPI -->|4. Ranking / VRP Request| RouteOptimizer["Route Optimizer (FastAPI) (Port 8000)"]
    RouteOptimizer -->|5. OSRM table matrix| OSRM["OSRM Engine (Port 5000)"]
    BackendAPI -->|6. Resolve Route API| OSRM

    %% Observability Stack
    BackendAPI -->|Metrics| Prometheus["Prometheus Server (Port 9090)"]
    Prometheus -->|Visualized Panels| Grafana["Grafana Visualizer (Port 3000)"]
    BackendAPI -->|Logs| SeqLog["Seq Centralized Logging (Port 5341)"]
```

---

## 2. โครงสร้างและการแมปพอร์ต (Service Port Mapping Catalog)

| บริการ (Service) | พอร์ตภายนอก (Dev Bind) | พอร์ตภายในตู้ | บทบาทและขอบเขตหน้าที่ |
| :--- | :---: | :---: | :--- |
| **nginx-proxy** | `8081` | `80` | ทางเข้าหลักของระบบ (Gateway) จัดการเส้นทางทราฟฟิกและบล็อก CORS |
| **backend** | `5000` | `80` | Core API พัฒนาด้วย .NET 8 ควบคุมโมเดลธุรกิจ, สิทธิ์ JWT และ SignalR Hub |
| **frontend** | `4201` | `80` | Angular 19 Admin Web สำหรับเฝ้าสถานการณ์ จัดการไรเดอร์และออเดอร์ |
| **rider-app** | `8083` | `80` | Flutter Web Client สำหรับจำลองพฤติกรรมและการทำงานของคนขับรถ |
| **route-optimizer** | `8009` | `8000` | FastAPI Engine คำนวณ VRP (Vehicle Routing Problem) ร่วมกับ Google OR-Tools |
| **osrm** | `5001` | `5000` | บริการคำนวณระยะทางและ Snap เส้นพิกัดเข้าถนนจริงเมืองอุดรธานี |
| **db** | `5432` | `5432` | ฐานข้อมูลถาวรเชิงพื้นที่ PostgreSQL 15 + PostGIS (SRID 4326) |
| **redis** | `6379` | `6379` | แคชความเร็วสูงระดับ RAM สำหรับเก็บพิกัดสดของไรเดอร์และ Distributed Lock |
| **rabbitmq** | `5672`, `15672` | `5672`, `15672` | ตัวรับส่งข้อความเหตุการณ์ในระบบ (Event Broker) เพื่อทำงานแบบ Async |
| **seq** | `8082`, `5341` | `80`, `5341` | ศูนย์รวมการตรวจและวิเคราะห์ Log โครงสร้างวัตถุ (Structured Logs) |
| **prometheus** | `9090` | `9090` | ตัวเก็บรวบรวมตัวชี้วัดประสิทธิภาพเชิงระบบและธุรกิจ (System Metrics) |
| **grafana** | `3000` | `3000` | หน้าจอแผงแสดงผลและสถิติ (Operations, Infrastructure, NOC) |
| **vault** | `8200` | `8200` | บริการเก็บรักษาค่าความลับและข้อมูลความปลอดภัย (Secrets Management) |

---

## 3. ลำดับเหตุการณ์และขั้นตอนการจัดส่ง (Core Delivery Lifecycle Flow)

กระบวนการตั้งแต่การสั่งอาหารของลูกค้าไปจนถึงการเดินทางจัดส่งของไรเดอร์ มีขั้นตอนการสื่อสารข้อมูลดังภาพจำลองนี้:

```mermaid
sequenceDiagram
    autonumber
    actor Customer as ลูกค้า
    participant Admin as แอดมิน (Angular)
    participant API as Backend API (.NET 8)
    participant RouteOpt as Route Optimizer (FastAPI)
    participant OSRM as OSRM Server
    actor Rider as ไรเดอร์ (Flutter)

    Customer->>API: 1. สร้างออเดอร์ใหม่ (POST /api/v1/orders)
    Note over API: บันทึกลง PostgreSQL (State = CREATED)
    API->>RouteOpt: 2. ร้องขอการคำนวณรอบจัดส่ง (VRP Optimizer)
    RouteOpt->>OSRM: 3. ขอ OSRM /table Distance Matrix ตามถนนจริง
    OSRM-->>RouteOpt: ส่งคืน Matrix ระยะทางตามโครงข่ายถนน
    Note over RouteOpt: ประมวลผล OR-Tools และ weighted heuristic ranking
    RouteOpt-->>API: ส่งคืนรายชื่ออันดับไรเดอร์ (Rider Candidates)
    
    loop Dispatch Offering (ทำงานเบื้องหลัง)
        API->>Rider: 4. ยิงข้อเสนอผ่าน SignalR (OfferReceived)
        Note over Rider: แอปสั่นเตือนและนับถอยหลัง 15 วินาที
        alt Rider ยอมรับงาน (Accept)
            Rider->>API: 5. ส่งคำสั่งยอมรับ (POST /accept-offer)
            Note over API: ตรวจสอบความถูกต้องและเปลี่ยนสถานะ Order เป็น ASSIGNED
            API-->>Rider: ยืนยันการมอบหมายงานสำเร็จ
        else Rider ปฏิเสธ หรือหมดเวลา (Reject / Timeout)
            Rider->>API: ส่งคำสั่งปฏิเสธ (หรือ Worker ทำงานเมื่อหมดเวลา)
            Note over API: เปลี่ยนคิวและวนลูปเสนอรายชื่อไรเดอร์ลำดับถัดไป
        end
    end

    Note over Rider: Rider เดินทางไปยังจุดรับอาหาร (PICKUP)
    Rider->>API: 6. ส่งพิกัด GPS อัปเดตผ่าน SignalR แบบสด (ทุก 5 วินาที)
    API->>OSRM: 7. ดึงเส้นSnapped เพื่อปรับแนวเส้นทางบนแผนที่
    API->>Admin: 8. Broadcast พิกัดและเส้น Snap ไปยัง Admin Dashboard
    Rider->>API: 9. กดยืนยันการรับอาหารสำเร็จ (State = DELIVERING)
    Rider->>API: 10. เดินทางถึงเป้าหมายและยืนยันการจัดส่งสำเร็จ (State = COMPLETED)
```

---

## 4. ท่อส่งข้อมูลติดตามและจัดเก็บตำแหน่งพิกัด (GPS Telemetry Stream Pipeline)

ตำแหน่ง GPS ของไรเดอร์จะถูกจัดส่งด้วยความถี่สูงเพื่อความแม่นยำในการนำทาง จึงมีการแบ่งช่องทางข้อมูลเพื่อป้องกันความเสียหายต่อประสิทธิภาพของฐานข้อมูลหลัก (PostgreSQL DB Starvation):

```mermaid
graph LR
    RiderGPS["Rider App GPS Update"] -->|SignalR Ingestion| API["Backend API (.NET 8)"]
    
    %% Ingest Pipelines
    API -->|1. Read/Write Speed State| Redis["Redis Location Cache"]
    API -->|2. High-Freq Telemetry Queue| RabbitMQ["RabbitMQ (gps.updates.queue)"]
    
    %% Async Processing
    RabbitMQ -->|Consumer Job| SnapWorker["OsrmSnapWorker (Background)"]
    SnapWorker -->|3. Call Road Snap| OSRM["OSRM Snapping"]
    SnapWorker -->|4. Persistent History batch| PostGIS["PostgreSQL (RiderLocationHistory)"]
    
    %% Live View Broadcast
    API -->|5. Realtime Broadcast| AdminHub["Admin Dashboard View (SignalR)"]
```

- **Redis Cache:** เก็บพิกัดละติจูด/ลองจิจูดล่าสุดและประวัติการออนไลน์ของไรเดอร์เพื่อการตอบสนองที่ฉับไว
- **PostgreSQL:** จะถูกบันทึกประวัติการขยับย้อนหลัง (History) ผ่านการทำงานแบบอะซิงโครนัสของ RabbitMQ Consumer เท่านั้น เพื่อป้องกันไม่ให้ทราฟฟิกการยิงพิกัด GPS เป็นจำนวนมากไปกระทบกับธุรกรรมฐานข้อมูลหลัก
- **SignalR Broadcast:** ส่งสัญญาณพิกัด Snapped เข้าสู่หน้าจอแผนที่ของแอดมินโดยตรงที่ความถี่ไม่เกิน 0.5 Hz ต่อไรเดอร์
