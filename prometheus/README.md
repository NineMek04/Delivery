# Prometheus Metrics Subsystem (prometheus/README.md)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือการตั้งค่ามอนิเตอร์และการแจ้งเตือนสำหรับเครื่องยนต์จัดเก็บข้อมูลตัวชี้วัดความถี่สูง **Prometheus Server**

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
`prometheus` ทำหน้าที่เป็นตู้ฐานข้อมูลตัวชี้วัดอนุกรมเวลา (Time-series Metrics Database):
1.  **Metrics Scraper:** คอยดึงข้อมูล (Pull/Scrape) จากตู้เป้าหมายย่อยตามระยะรอบที่กำหนด (15 วินาที)
2.  **Rule Evaluator:** ประเมินค่าตัวเลขเปรียบเทียบกับกฎแจ้งเตือนความผิดปกติ (Alerting Rules) ทุกๆ 15 วินาที
3.  **Alert Dispatcher:** ยิงคำสั่งเตือนไปยัง `alertmanager` ทันทีที่พบเงื่อนไขผิดพลาดข้ามช่วงเวลา

---

## 2. โครงสร้างและการแมปดึงข้อมูล (Scrape Job Setup)

รายละเอียดระบุไว้ในไฟล์กำหนดค่าหลัก [prometheus.yml](../prometheus.yml):

*   **Scrape Interval:** ดึงข้อมูลทุกๆ 15 วินาที
*   **Alerting Target:** ส่งต่อการแจ้งเตือนไปยังตู้ Alertmanager ที่ปลายทาง `alertmanager:9093`
*   **รายชื่อเป้าหมายการ Scraping (Targets):**
    1.  `prometheus`: ตัวเองของ Prometheus (`localhost:9090`)
    2.  `backend`: ตัววัดจากหลังบ้าน .NET (`backend:80/metrics`)
    3.  `rabbitmq`: ตัววัดสถิติ Queue ของ Rabbit (`rabbitmq:15692/metrics`)
    4.  `cadvisor`: สเปกแรม/CPU ของตู้ Docker (`cadvisor:8080`)
    5.  `node-exporter`: ฮาร์ดแวร์ฝั่ง Windows/Linux Host (`node-exporter:9100`)
    6.  `postgres-exporter`: สถิติ PostgreSQL Server (`postgres-exporter:9187`)
    7.  `redis-exporter`: สถิติ Redis Cache Engine (`redis-exporter:9121`)

---

## 3. กฎแจ้งเตือนแอปพลิเคชัน (Evaluation Alert Rules)

Prometheus ประเมินกฎไฟล์แจ้งเตือนที่ติดตั้งไว้ในโฟลเดอร์ [rules/](rules/) ดังนี้:

### 3.1 [infrastructure_alerts.yml](rules/infrastructure_alerts.yml) (เตือนฮาร์ดแวร์)
*   **HostOutOfMemory:** แรมเครื่องจริงเหลือน้อยกว่า 10% ติดต่อกัน 2 นาที
*   **DatabaseConnectionsHigh:** ยอดเชื่อมต่อฐานข้อมูลของ Postgres พุ่งทะลุ 85% ของขีดจำกัด
*   **ContainerOOMKilled:** ตรวจพบคอนเทนเนอร์โดนเชือดดับเนื่องจากแรมหมดค้าง (Rate > 0)
*   **HighDiskUsage:** เนื้อที่เก็บไฟล์ในฮาร์ดดิสก์โฮสต์ถูกใช้ไปเกิน 85%

### 3.2 [security_alerts.yml](rules/security_alerts.yml) (เตือนภัยความปลอดภัย)
*   **HighFailedLogins & CriticalFailedLogins:** ยอดล็อกอินล้มเหลวพุ่งสูงผิดปกติ (สุ่มเสี่ยง Brute Force)
*   **HighLockoutRate:** ยอดระงับสิทธิ์บัญชีผู้ใช้รวดเร็วเกินกำหนด
*   **HighCsrfViolations:** การตรวจสอบ Cross-Site Request Forgery ไม่ผ่านถี่เกินไป
*   **HighRateLimitReached:** คำขอพิกัด GPS หรือ API ถูก Rate Limit ปัดทิ้งถล่มทลาย
*   **DispatchQueueBacklog:** ออเดอร์จัดส่งคั่งค้างใน Queue ระบบจัดสรรนานเกินเป้า
*   **RabbitMqDown:** การเชื่อมต่อระบบส่งข้อมูล Event ล่มลง
*   **BackendApiDown:** ตัวคอร์ API Gateway ออฟไลน์ล่มลง

---

## 4. วิธีการตรวจสอบระบบผ่าน Dashboard (Verification Steps)

1.  เริ่มรันตู้ Prometheus:
    ```bash
    docker-compose up -d prometheus
    ```
2.  เข้าใช้งานหน้าต่างควบคุม:
    *   **URL:** `http://localhost:9090`
3.  ตรวจสอบเป้าหมาย Scraper:
    *   ไปที่เมนู **Status** $\rightarrow$ **Targets** เพื่อเช็คให้มั่นใจว่าสถานะของ Exporters ทุกตัวเป็นสีเขียวขึ้นคำว่า **`UP`**
4.  ตรวจสอบการประเมินกฎแจ้งเตือน:
    *   ไปที่เมนู **Alerts** เพื่อสังเกตการณ์กฎเตือนภัย หากตัวไหนเข้าเงื่อนไข จะเปลี่ยนสถานะเป็น **`PENDING`** (กำลังรอนับเวลา) หรือ **`FIRING`** (ส่งข้อมูลเตือนภัยสำเร็จ)

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Context)
*   [Infrastructure, Telemetry & SLO Specification](../.docs/ai-context/spec-infra-devops.md)
*   [DevOps Deployment Manual (README-DEVOPS.md)](../README-DEVOPS.md)
