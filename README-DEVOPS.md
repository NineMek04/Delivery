# DevOps & Infrastructure Manual (README-DEVOPS.md)

> [!NOTE]
> เอกสารฉบับนี้จัดทำขึ้นสำหรับทีม **DevOps Engineer** และ **System Administrator** เพื่อใช้ดูแลรักษาความปลอดภัยของระบบโครงสร้างพื้นฐาน (Infrastructure), การจัดการเน็ตเวิร์ก (Nginx Gateway), ระบบมอนิเตอร์ (Observability Stack) และการกำหนดนโยบายแจ้งเตือนเหตุฉุกเฉิน (Alerting Rules)

---

## 1. ผังและสถาปัตยกรรมตู้คอนเทนเนอร์ (Docker Compose Topology)

ระบบรันผ่านสภาพแวดล้อม Docker Compose ควบคุมและจำลองระบบด้วย 15 บริการ (Services) ดังนี้:

### 1.1 Gateway & Proxy Layer
*   **`nginx-proxy` (delivery-nginx):** เกตเวย์ด่านหน้า ปล่อยพอร์ต `8081` ขาเข้าสู่ภายนอก ทำหน้าที่กระจายเส้นทางคำขอ (Reverse Proxy) ไปยังหน้าบ้านและหลังบ้าน

### 1.2 User Clients (Frontend)
*   **`frontend` (delivery-frontend):** คอนเทนเนอร์แผงควบคุมแอดมิน รันผ่าน Angular 19 (พอร์ตภายใน 80)
*   **`rider-app` (delivery-rider-app):** คอนเทนเนอร์แผงจำลองคนขับ รันผ่าน Flutter Web (พอร์ตภายใน 80)

### 1.3 Application & AI Layer
*   **`backend` (delivery-backend):** คอร์เซิร์ฟเวอร์ .NET 8 Web API และ SignalR Hub (พอร์ตภายใน 80)
*   **`ai-service` (delivery-ai):** FastAPI คำนวณ VRP Google OR-Tools และจับคู่พนักงาน (พอร์ตภายใน 8000)
*   **`osrm` (delivery-osrm):** บริการหาเส้นทางและ snap พิกัดเข้าโครงข่ายถนนเมืองอุดรธานี (พอร์ตภายใน 5000)

### 1.4 Data Storage & Queue Layer
*   **`db` (delivery-db):** PostgreSQL 15 + PostGIS ส่วนขยายจัดการภูมิศาสตร์ (พอร์ตภายใน 5432)
*   **`pgbouncer` (delivery-pgbouncer):** ตัวจัดการ Connection Pool เพื่อเซฟการต่อฐานข้อมูล (พอร์ตภายใน 5432)
*   **`redis` (delivery-redis):** แคชความเร็วสูงระดับแรมสำหรับจัดเก็บพิกัดสดและ Lock ป้องกันแย่งงาน (พอร์ตภายใน 6379)
*   **`rabbitmq` (delivery-rabbitmq):** Event Broker สื่อสารข้ามเซิร์ฟเวอร์แบบ Asynchronous (พอร์ตภายใน 5672)

### 1.5 Observability & Monitoring Stack
*   **`seq` (delivery-seq):** ศูนย์รวมวิเคราะห์ล็อกโครงสร้าง (Structured JSON Logs) (พอร์ตภายใน 80 และ 5341)
*   **`prometheus` (delivery-prometheus):** จัดเก็บข้อมูล Time-series metrics เชิงระบบและธุรกิจ (พอร์ตภายใน 9090)
*   **`grafana` (delivery-grafana):** วาดหน้าต่างกราฟสรุปตัวชี้วัด (พอร์ตภายใน 3000)
*   **`alertmanager` (delivery-alertmanager):** จัดคิวและส่งสัญญาณเตือนภัยทางแอปพลิเคชัน (พอร์ตภายใน 9093)

### 1.6 Hardware & Software Exporters (ตัวแปลงส่งค่าตัวชี้วัด)
*   **`cadvisor` (delivery-cadvisor):** มอนิเตอร์ทรัพยากรระดับ Container (CPU, Mem, Network I/O)
*   **`node-exporter` (delivery-node-exporter):** มอนิเตอร์ฮาร์ดแวร์ฝั่งเครื่องโฮสต์จริง (Disk, OS RAM, CPU Load)
*   **`postgres-exporter` (delivery-postgres-exporter):** ดึงสถิติจำนวน Active backends connection และคิวรีฐานข้อมูล
*   **`redis-exporter` (delivery-redis-exporter):** ดึงสถิติอัตราใช้เมมโมรี่ และอัตรา Key eviction ของ Redis

---

## 2. เครือข่ายและระบบรักษาความปลอดภัยเกตเวย์ (Networking, Proxy & Port Isolation)

เพื่อความมั่นคงปลอดภัยสูงสุด ระบบบังคับใช้นโยบาย **Port Isolation** อย่างเข้มงวด:

### 2.1 นโยบายสกัดกั้นการเชื่อมต่อตรงจากนอกตู้ (Port Isolation Rule)
1.  ในไฟล์หลัก [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml) จะ **ไม่มีการเปิดเผยพอร์ตสู่สาธารณะ** ของบริการหลักภายใน ได้แก่ `db`, `pgbouncer`, `redis`, `rabbitmq`, `prometheus`, `alertmanager`
2.  ในไฟล์พอร์ตสำหรับการพัฒนา [docker-compose.override.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.override.yml) หากต้องเปิดเผยพอร์ตเพื่อทดสอบระบบด่วน (เช่น `5432` สำหรับ pgAdmin หรือ `6379` สำหรับ RedisInsight) บังคับต้องเขียนจำกัดให้ผูกเฉพาะไอพีภายในเครื่อง **`127.0.0.1`** เท่านั้น:
    - *ตัวอย่างที่ถูกต้อง:* `"127.0.0.1:5432:5432"` (อนุญาตเฉพาะคนรันเครื่องตัวเองเข้าถึง)
    - *ตัวอย่างที่ห้ามทำเด็ดขาด:* `"0.0.0.0:5432:5432"` (จะทำให้คนอื่นในวงเน็ตเวิร์กเดียวกันสแกนเจาะข้อมูลได้)

### 2.2 บทบาทของ Nginx Reverse Proxy
กำหนดสเปกในไฟล์ [nginx.conf](file:///c:/Users/ASUS/Desktop/Project/Delivery/nginx-proxy/nginx.conf):
*   **SSL/TLS Security:** บังคับเปลี่ยนการเชื่อมต่อ HTTP (พอร์ต 80) เป็น HTTPS (พอร์ต 443) และใช้เฉพาะโปรโตคอล TLS v1.2 / v1.3 พร้อม Cipher Suite ที่ปลอดภัย
*   **CORS Management:** กรองและยอมรับเฉพาะ Origin ที่กำหนดไว้ เช่น `AllowedOrigins` ป้องกันสคริปต์หน้าบ้านถูกเบราว์เซอร์สกัดกั้น
*   **Rate Limiting:** จำกัดเฉพาะจุดรับพิกัด GPS เพื่อสกัดกั้นแอปพลิเคชันไรเดอร์จำลองยิงถล่มทราฟฟิก (Telemetry DDOS Protection)
*   **Map Tile Caching:** เก็บแคชของ OpenStreetMap Tiles แผนที่เมืองอุดรธานีไว้ที่ Nginx ท้องถิ่นของคอนเทนเนอร์ เพื่อความรวดเร็วในการโหลดแผนที่ของไรเดอร์และเซฟแบนด์วิดท์ภายนอก

---

## 3. ระบบสังเกตการณ์สุขภาพระบบ (Observability Stack)

ระบบติดตามแบบเรียลไทม์แบ่งท่อข้อมูลเป็น 2 สายหลัก:

### 3.1 ท่อข้อมูลประวัติข้อผิดพลาด (Logging Pipeline via Serilog & Seq)
*   Backend API มีการใช้ **Serilog** และทำการส่งออกล็อกในรูปแบบ **Structured JSON Logs** ไปยัง Seq Centralized Logging พอร์ต `8082` (Web UI) และ `5341` (Ingestion API)
*   นักพัฒนาสามารถใช้คำสั่งค้นหาแบบ Object เช่น `IsDefined(@Exception) or @Level = 'Error'` หรือกรองตาม component เช่น `Component = 'AiOptimizer'` เพื่อแก้ไขบั๊กหน้างานได้ทันที

### 3.2 ท่อข้อมูลตัววัดประสิทธิภาพ (Metrics Pipeline via Prometheus & Grafana)
Prometheus ดึงค่าตัววัด (Scrape Metrics) จากพอร์ตส่งข้อมูลในระบบทุก 15 วินาที ตามที่ระบุไว้ใน [prometheus.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/prometheus.yml) ครอบคลุม:
*   สถิติ CPU/Memory คอนเทนเนอร์ (ผ่าน `cadvisor`)
*   สถิติหน่วยความจำโฮสต์ (ผ่าน `node-exporter`)
*   สถิติฐานข้อมูล PostgreSQL (ผ่าน `postgres-exporter`)
*   สถิติ Redis Cache (ผ่าน `redis-exporter`)
*   สถิติเฉพาะธุรกิจของ Backend API (ผ่าน `/metrics` Endpoint) เช่น `delivery_active_signalr_connections` และ `delivery_gps_updates_total`

---

## 4. นโยบายแจ้งเตือนภัยระดับ NOC และ Infra Alerts (Alerting Rules)

### 4.1 แผงควบคุมบอร์ดสงครามวิกฤต (The Critical 9 NOC Dashboard)
แดชบอร์ดพิเศษสำหรับแอดมินบอร์ด (NOC War Room) คอยตรวจสอบ **9 จุดวิกฤต** หากเกิดความผิดปกติสีไฟจราจรจะสลับเป็นส้มหรือแดงและกระพริบเตือนทันที:
1.  **PostgreSQL Connection Saturation (%):**  
    สูตร: `pg_stat_database_numbackends{datname="delivery_db"} / pg_settings_max_connections * 100`  
    *(ส้ม: >80% / แดง: >95% เจาะลึกความแออัดฐานข้อมูล)*
2.  **Redis Memory Eviction Rate:**  
    สูตร: `rate(redis_evicted_keys_total[5m])`  
    *(แดง: >0 มีการเตะคีย์ distributed lock หรือพิกัดสดของพนักงานทิ้งเนื่องจากแคชเต็ม)*
3.  **Dispatch Queue Backlog (ออเดอร์ตกค้าง):**  
    สูตร: `delivery_dispatch_backlog_orders`  
    *(ส้ม: >0 มีออเดอร์สถานะจับคู่ค้างนานเกิน 2 นาที / แดง: >5)*
4.  **Supply/Demand Ratio (สัดส่วนพนักงานว่างต่องาน):**  
    สูตร: `delivery_idle_riders / delivery_active_orders`  
    *(ส้ม: <1.0 รถเริ่มน้อยกว่าออเดอร์ / แดง: <0.5 วิกฤตรถขาดแคลน)*
5.  **HTTP 5xx Server Error Rate (ต่อนาที):**  
    สูตร: `sum(rate(http_requests_received_total{code=~"5.."}[1m])) * 60`  
    *(ส้ม: >5 / แดง: >20 API เริ่มขัดข้องส่งผลต่อผู้ใช้)*
6.  **P95 VRP AI Dispatch Latency (เวลาตอบสนองจับคู่):**  
    สูตร: `histogram_quantile(0.95, sum(rate(delivery_ai_request_duration_seconds_bucket{operation="rank_dispatch_candidates"}[5m])) by (le))`  
    *(ส้ม: >1.5s / แดง: >2.0s ตัวบ่งชี้โมเดล VRP ประมวลผลช้าเกินเป้า)*
7.  **OSRM Route Unavailability Rate (%):**  
    สูตร: `sum(rate(delivery_routing_requests_total{type="haversine"}[5m])) / (sum(rate(delivery_routing_requests_total[5m])) + 0.0001) * 100`  
    *(ส้ม: >5% / แดง: >20% OSRM ล่ม บีบให้ระบบถอยลงไปใช้พิกัดตรง Haversine)*
8.  **High-Reject Match Rate (%):**  
    สูตร: `sum(rate(delivery_dispatch_matches_total{status=~"rejected|timeout"}[5m])) / (sum(rate(delivery_dispatch_matches_total[5m])) + 0.0001) * 100`  
    *(ส้ม: >30% / แดง: >60% คนขับปฏิเสธงานหรือปล่อยข้อเสนอหมดเวลารัวๆ)*
9.  **Rate Limit Drops / DDoS Drops (ต่อนาที):**  
    สูตร: `sum(rate(delivery_rate_limit_rejections_total[1m])) * 60`  
    *(ส้ม: >50 / แดง: >500 โดนยิงสแปมพิกัด)*

---

### 4.2 เกณฑ์การเตือนระดับระบบ (Infrastructure & App Alerts)
กำหนดเกณฑ์การเตือนภัยฉุกเฉินใน [infrastructure_alerts.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/prometheus/rules/infrastructure_alerts.yml) และ [security_alerts.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/prometheus/rules/security_alerts.yml):

| ชื่อกฎเตือนภัย (Alert Name) | เกณฑ์การทริกเกอร์ (Expression Rule) | ระดับความฉุกเฉิน | การแก้ไขเบื้องต้น (Remediation) |
| :--- | :--- | :---: | :--- |
| **`HostOutOfMemory`** | แรมว่างบนเครื่องโฮสต์ต่ำกว่า 10% ติดต่อกัน 2 นาที | Warning | ขยายขนาด RAM หรือจำกัด Docker Memory usage |
| **`DatabaseConnectionsHigh`** | Active database connections เกิน 85% ของลิมิตฐานข้อมูล | Critical | ตรวจสอบ PgBouncer connection leaks หรือปิด Session ค้าง |
| **`ContainerOOMKilled`** | ตรวจพบคอนเทนเนอร์โดน OS ปิดตัวเนื่องจากแรมเกินลิมิต | Critical | ตรวจหา Memory leaks ใน Backend (.NET) หรือขยายแรมขีดจำกัดตู้ |
| **`HighDiskUsage`** | เนื้อที่ว่างของฮาร์ดดิสก์เครื่องโฮสต์เหลือน้อยกว่า 15% | Warning | สั่ง `docker system prune -af` หรือเคลียร์โฟลเดอร์ Logs |
| **`RabbitMqDown`** | หลังบ้านเชื่อมต่อกับ RabbitMQ Broker ไม่ได้ | Critical | ตรวจสถานะตู้ RabbitMQ, เคลียร์ดิสก์ที่ RabbitMQ เก็บไฟล์ |
| **`BackendApiDown`** | ไม่สามารถติดต่อ endpoint ตรวจสุขภาพของ .NET API | Critical | คอนเทนเนอร์แครชหรือพอร์ตชน ให้รีสตาร์ทบริการ `backend` |
| **`CriticalFailedLogins`** | มีการล็อกอินล้มเหลวเกิน 50 ครั้งต่อนาที (Brute Force) | Critical | บล็อกไอพีต้นทางชั่วคราวผ่าน Cloudflare CDN/WAF |

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Specs)
*   **คู่มือระบบตรวจสอบกราฟ (Grafana Dashboards):**  
    👉 [Grafana Dashboards Subsystem Manual](file:///c:/Users/ASUS/Desktop/Project/Delivery/grafana/README.md)  
    *(อธิบายการเชื่อมต่อแหล่งข้อมูล, การตั้งบอร์ด และการจัดการค่า Alerts)*
*   **คู่มือแผนที่จราจร OSRM (OSRM Map Guide):**  
    👉 [OSRM Map Data & Setup Reference](file:///c:/Users/ASUS/Desktop/Project/Delivery/osrm_data/README.md)  
    *(รายละเอียดไฟล์แผนที่อุดรธานี, การบิวด์ MLD Dijkstra, และการรัน Sandbox)*
*   [Infrastructure, Telemetry & SLO Specification](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-infra-devops.md)
*   [State Machine & Telemetry Data Consistency Spec](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-consistency.md)
