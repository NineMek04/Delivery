# Nginx Reverse Proxy Subsystem (nginx-proxy/README.md)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือการกำหนดนโยบายรักษาความปลอดภัย การกรองทราฟฟิก และการจัดการโหลดพิกัดระดับเครือข่ายสำหรับระบบ **Nginx Reverse Proxy Gateway**

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
`nginx-proxy` ทำหน้าที่เป็นผู้ควบคุมทราฟฟิกและเกตเวย์รับทางเดียวของระบบ (Single Ingress Gateway):
1.  **Traffic Routing:** รับคำร้องขอเข้ามาทางพอร์ต `8081` และจำแนกความต้องการ:
    -   คำขอหน้าบ้านทั่วไป (`/`) $\rightarrow$ ส่งต่อให้ตู้บริการหน้าบ้าน `frontend`
    -   คำขอข้อมูลระบบ API (`/api/...`) $\rightarrow$ ส่งต่อให้ตู้บริการหลังบ้าน `backend`
    -   คำขอเชื่อมต่อท่อส่งตำแหน่งสด (`/hubs/...`) $\rightarrow$ ส่งต่อให้ SignalR Hub บน `backend`
2.  **Network Shield (OWASP API Security):** บังคับใช้นโยบายสกัดกั้น Header ความปลอดภัย และจำกัดอัตราส่งพิกัดของคนขับรถ (Rate Limiting) เพื่อป้องกันสแปม
3.  **Metrics Protection:** ปิดกั้นไม่ให้ภายนอกเข้าถึงข้อมูลตัวชี้วัด Prometheus (/metrics) โดยไม่มีรหัสผ่านผ่าน Basic Authentication

---

## 2. โครงสร้างและการตั้งค่าทางเทคนิค (Configuration Structure)

บริการรันผ่านไฟล์หลัก [nginx.conf](file:///c:/Users/ASUS/Desktop/Project/Delivery/nginx-proxy/nginx.conf):

*   **Upstream Definition:** กำหนดสายส่งต่อไปหาคอนเทนเนอร์ต่างๆ ใน Docker:
    ```nginx
    upstream backend { server backend:80; }
    upstream frontend { server frontend:80; }
    ```
*   **External Port Mapping:** ระบุพอร์ตเชื่อมต่อออกภายนอกใน [docker-compose.yml](file:///c:/Users/ASUS/Desktop/Project/Delivery/docker-compose.yml):
    -   พอร์ตรับบริการอินเทอร์เน็ต: `8081:80` (ทางเข้าหลัก)

---

## 3. นโยบายความปลอดภัยและเกราะสกัดกั้น (Security & Rate-limiting Rules)

### 3.1 ระบบจำกัดความถี่การยิง (Rate Limiting)
เพื่อรองรับการยิงพิกัดความถี่สูงของคนขับในห้องทดสอบ (Load testing) แต่ควบคุมการสแปมในโปรดักชัน:
-   **Zone Definition:** ประกาศสระจัดเก็บไอพี `api_limit` ขนาด 10MB และจำกัดค่าความเร็วเฉลี่ยไว้สูงสุดที่ **200 Requests/Second**:
    ```nginx
    limit_req_zone $binary_remote_addr zone=api_limit:10m rate=200r/s;
    ```
-   **API Protection:** สัญญาณ `/api` และ SignalR `/hubs` จะผูกเข้ากับข้อจำกัดความถี่นี้ โดยอนุญาตให้มีคำร้องขอกระตุกชั่วขณะได้ไม่เกิน 100 คำขอ (`burst=100 nodelay`) หากเกินระบบจะดีดคืนข้อผิดพลาด HTTP `503 Service Temporarily Unavailable`

### 3.2 กฎความปลอดภัยระดับสูง (OWASP Hardening Headers)
Nginx บังคับฝัง Security Headers ลงในคำตอบรับทุกคำขอดังนี้เพื่อเซฟความปลอดภัยหน้าเว็บ:
1.  `X-Frame-Options "DENY"`: ป้องกันภัยหลอกให้กดยอดปุ่ม (Clickjacking Attacks)
2.  `X-Content-Type-Options "nosniff"`: บังคับเบราว์เซอร์เชื่อชนิด MIME type ห้ามเดาเอาเอง
3.  `X-XSS-Protection "1; mode=block"`: บล็อกหน้าเบราว์เซอร์ทันทีหากตรวจพบภัยคุกคาม XSS
4.  `Content-Security-Policy (CSP)`: ควบคุมเข้มงวดให้หน้าเว็บ Angular โหลดเฉพาะรูปภาพ สคริปต์ และเชื่อมต่อไปยังต้นทางที่ได้รับอนุญาตเท่านั้น
5.  `client_max_body_size 5M`: ตัดสิทธิ์คำขอขนาดเกิน 5 Megabytes เพื่อสกัดกั้นการแอบอัพโหลดไฟล์ขนาดใหญ่มาถล่มเมมโมรี่หลังบ้าน

### 3.3 การป้องกันตัวชี้วัด (/metrics basic auth)
*   ตัวชี้วัดสำหรับ Prometheus (/metrics) ได้รับการปิดกั้นและตรวจสอบผ่านระบบรหัสผ่านพื้นฐาน **Basic Authentication**
*   ไฟล์ระบุรหัสผ่านอ้างอิงที่ [.htpasswd](file:///c:/Users/ASUS/Desktop/Project/Delivery/nginx-proxy/.htpasswd)

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Context)
*   [Infrastructure, Telemetry & SLO Specification](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-infra-devops.md)
*   [DevOps Deployment Manual (README-DEVOPS.md)](file:///c:/Users/ASUS/Desktop/Project/Delivery/README-DEVOPS.md)
