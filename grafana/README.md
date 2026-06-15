# Grafana Dashboards Subsystem (grafana/README.md)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือการตั้งค่าและใช้งาน **Grafana Dashboards** เพื่อมอนิเตอร์สถานะระบบ สถิติทางธุรกิจ ความมั่นคงปลอดภัย และประสิทธิภาพของโครงสร้างพื้นฐาน

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
Grafana ทำหน้าที่เป็นระบบจำลองและวาดภาพข้อมูล (Data Visualization Engine) โดยดึงค่า Time-series metrics ทั้งหมดมาจาก Prometheus Database และแปลงสภาพเป็นแผงควบคุมอัจฉริยะ (Dashboards) ให้กับผู้ดูแลระบบ (SRE/SysAdmin) และผู้บริหารโครงการ (Project Manager) ได้ประเมินผลสุขภาพโปรแกรมตลอดเวลา

---

## 2. โครงสร้างการจัดหาบอร์ดอัตโนมัติ (Automated Provisioning Setup)

เพื่อความเป็นมาตรฐานและสามารถสร้างระบบขึ้นใหม่ได้ทันทีบน Production โดยไม่ต้องกด Import หน้าจอด้วยมือ Grafana ของเราได้รับการตั้งค่าโครงสร้างแบบ **Automated Provisioning** ผ่านการ Mount Volumes ของ Docker:

```
grafana/
└── provisioning/
    ├── datasources/
    │   └── datasource.yml     # ตั้งค่าแหล่งข้อมูล (Prometheus Ingest) อัตโนมัติ
    └── dashboards/
        ├── dashboard.yml      # กำหนดผู้จัดหา (Providers) และที่อยู่เก็บไฟล์บอร์ด
        ├── noc/
        │   └── noc_dashboard.json    # ไฟล์บอร์ด NOC & War Room
        └── standard/
            ├── ai_dispatch.json      # ไฟล์บอร์ด AI Dispatch
            ├── apm.json              # ไฟล์บอร์ด Performance APM
            ├── client_experience.json # ไฟล์บอร์ด Client Experience
            ├── infrastructure.json   # ไฟล์บอร์ด Hardware Resources
            ├── operations.json       # ไฟล์บอร์ด Business Operations
            └── security.json         # ไฟล์บอร์ด System Security
```

*   **Datasource Provisioning:** ไฟล์ [datasource.yml](provisioning/datasources/datasource.yml) จะทำการลงทะเบียนฐานข้อมูล Prometheus (`http://prometheus:9090`) เป็นแหล่งข้อมูลหลัก (Default Datasource) ให้อัตโนมัติทันทีที่เซิร์ฟเวอร์สตาร์ต
*   **Dashboard Providers:** ไฟล์ [dashboard.yml](provisioning/dashboards/dashboard.yml) จะสั่งการให้ Grafana ดึงไฟล์ JSON ในไดเรกทอรีท้องถิ่นไปติดตั้งแยกโฟลเดอร์บนหน้าจอให้อัตโนมัติ:
    1.  โฟลเดอร์ **`NOC & War Room`** $\rightarrow$ ดึงไฟล์จากไดเรกทอรี `noc/`
    2.  โฟลเดอร์ **`Security & Operations`** $\rightarrow$ ดึงไฟล์จากไดเรกทอรี `standard/`

---

## 3. ดัชนีแผงควบคุมระบบ (Dashboards Catalogue)

ระบบมาพร้อมกับ 7 แดชบอร์ดสำเร็จรูปพร้อมใช้งานสำหรับการประเมินสภาพระบบ:

### 3.1 บอร์ดสงครามแผงควบคุมระดับสูง (NOC & War Room Dashboard)
*   **ที่เก็บไฟล์:** [noc_dashboard.json](provisioning/dashboards/noc/noc_dashboard.json)
*   **หน้าที่:** แผงควบคุม Stat Panels ขนาดใหญ่ และใช้สีสัญญาณไฟจราจรเตือนภัยระดับ Single Points of Failure ครอบคลุม **9 จุดวิกฤต (The Critical 9)** เช่น อัตราใช้ Postgres Connections, แรม Redis Eviction, อัตราออเดอร์ Backlog ค้างเติ่ง, และ อัตราการสลับโหมดสำรองของ OSRM (Haversine Ratio)

### 3.2 บอร์ดความมั่นคงปลอดภัย (Security Dashboard)
*   **ที่เก็บไฟล์:** [security.json](provisioning/dashboards/standard/security.json)
*   **หน้าที่:** มอนิเตอร์การจู่โจมและพฤติกรรมผิดปกติ เช่น อัตราการล็อกอินล้มเหลว (High Failed Logins), การระงับบัญชี (Account Lockouts), อัตราคำขอ CSRF Rejections ผิดปกติ, และจำนวนคำขอที่ถูกปฏิเสธเนื่องจากเกิน Rate Limits (DDoS Protection)

### 3.3 บอร์ดทรัพยากรฮาร์ดแวร์ (Infrastructure Dashboard)
*   **ที่เก็บไฟล์:** [infrastructure.json](provisioning/dashboards/standard/infrastructure.json)
*   **หน้าที่:** ตรวจสอบภาระเครื่องโฮสต์และตู้ Docker (CPU, Memory, Disk, Network I/O) มอนิเตอร์หาคอนเทนเนอร์ที่ถูกเชือดดับ (OOMKilled) และวิเคราะห์ประสิทธิภาพคิว Connection Pooling ของ PgBouncer

### 3.4 บอร์ดประสานการทำงานระบบ AI (AI Dispatch Dashboard)
*   **ที่เก็บไฟล์:** [ai_dispatch.json](provisioning/dashboards/standard/ai_dispatch.json)
*   **หน้าที่:** ติดตามความเร็วในการคิด VRP Solver ของ Google OR-Tools, จำนวนผู้ขับที่เป็น Candidates ในแต่ละพื้นที่, และความสำเร็จของการจับคู่ออเดอร์

### 3.5 บอร์ดประสิทธิภาพแอปพลิเคชัน (APM Dashboard)
*   **ที่เก็บไฟล์:** [apm.json](provisioning/dashboards/standard/apm.json)
*   **หน้าที่:** รายงานความหน่วงเวลาเฉลี่ย (Average Latency) และ p95/p99 Latency ของ HTTP API Endpoints ต่างๆ ของ .NET Web API

### 3.6 บอร์ดตัวชี้วัดประสิทธิภาพธุรกิจ (Operations Dashboard)
*   **ที่เก็บไฟล์:** [operations.json](provisioning/dashboards/standard/operations.json)
*   **หน้าที่:** มอนิเตอร์จำนวนออเดอร์ที่สร้าง (Order Created), สถานะยอดออเดอร์ส่งสำเร็จสะสม และจำนวนคนขับ Rider ที่กำลังออนไลน์อยู่ ณ ปัจจุบัน

### 3.7 บอร์ดประสบการณ์ผู้ใช้งาน (Client Experience Dashboard)
*   **ที่เก็บไฟล์:** [client_experience.json](provisioning/dashboards/standard/client_experience.json)
*   **หน้าที่:** มอนิเตอร์ความเร็วการแสดงผลหน้าบ้าน อัตราการเชื่อมต่อใหม่ (SignalR Websocket Reconnect Rate) และความลื่นไหลของพิกัดหน้าแอปมือถือ

---

## 4. วิธีการขึ้นระบบและเปิดใช้งาน (Setup & Verification Steps)

1.  ตรวจสอบว่าบริการ Grafana มีการตั้งค่าการรันในไฟล์ [docker-compose.yml](../docker-compose.yml):
    ```yaml
      grafana:
        image: grafana/grafana:10.0.0
        container_name: delivery-grafana
        ports:
          - "3000:3000"
        volumes:
          - ./grafana/provisioning:/etc/grafana/provisioning
        environment:
          - GF_SECURITY_ADMIN_PASSWORD=admin
        networks:
          - delivery-network
    ```
2.  เริ่มรัน Container ฝั่ง Grafana (รวมถึง Prometheus ที่จำเป็นต้องป้อนข้อมูลให้):
    ```bash
    docker-compose up -d prometheus grafana
    ```
3.  เข้าหน้าเว็บแอดมินนำทางมอนิเตอร์:
    *   **URL:** `http://localhost:3000`
    *   **บัญชีเริ่มต้น:** Username: `admin` | Password: `admin` (ระบบจะให้กรอกรหัสผ่านใหม่ในการเข้าสู่ระบบครั้งแรก)
4.  ตรวจสอบหน้าบอร์ดอัตโนมัติ:
    *   ไปที่เมนู **Dashboards** $\rightarrow$ ท่านจะพบโฟลเดอร์ **`NOC & War Room`** และ **`Security & Operations`** ถูกติดตั้งพร้อมใช้งานทันทีโดยไม่ต้องนำเข้าด้วยมือ
    *   ตรวจสอบสัญญาณเส้นกราฟว่าขึ้นปกติ (มีจุดเชื่อมต่อ Prometheus Data Source โหลดค่าสำเร็จ)

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Context)
*   [Infrastructure, Telemetry & SLO Specification](../.docs/ai-context/spec-infra-devops.md)
*   [DevOps Deployment Manual (README-DEVOPS.md)](../README-DEVOPS.md)
