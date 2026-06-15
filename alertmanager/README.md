# Alertmanager Notification Subsystem (alertmanager/README.md)

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือการบริหารจัดการเส้นทางเตือนภัยพิบัติ และกำหนดนโยบายส่งข้อความเตือนไปยังระบบแชทภายนอกสำหรับตู้บริการ **Alertmanager**

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
`alertmanager` ทำหน้าที่เป็นศูนย์คัดกรองจัดกลุ่มเตือนภัย (Alert Dispatch Coordinator):
1.  **Deduplication & Grouping:** จัดกลุ่มสัญญาณเตือนภัยที่เกิดขึ้นพร้อมๆ กัน (เช่น เมื่อ API ล่มจะเกิดสัญญาณเตือนหลายบริการพ่วงมา) เพื่อสรุปเป็นข้อความเดียว ป้องกันปัญหาสแปมข้อความท่วมท้นในช่องแชท
2.  **Silence Management:** กำหนดระบบละเว้นชั่วคราว (Silence) เพื่อปิดเสียงเตือนขณะที่วิศวกรกำลังดำเนินระบบซ่อมแซม
3.  **Notification Routing:** จับคู่ระดับความฉุกเฉินและยิงส่งต่อไปยังช่องทางการสื่อสารทีมพัฒนา เช่น Discord Webhooks

---

## 2. โครงสร้างและการคัดกรองข้อความ (Routing Configuration)

รายละเอียดการส่งผ่านข้อความระบุไว้ในไฟล์หลัก [alertmanager.yml](alertmanager.yml):

*   **Resolve Timeout:** กำหนดช่วงเวลา 5 นาที (`resolve_timeout: 5m`) หากสัญญาณเตือนเงียบหายเกิน 5 นาที ระบบจะสรุปส่งผลลัพธ์ว่าปัญหาความปลอดภัยได้รับการแก้ไขแล้ว (`send_resolved: true`)
*   **Grouping Rules (การควบคุมความถี่):**
    -   `group_by: ['alertname']`: จัดกลุ่มข้อความเตือนภัยตามรหัสชื่อสัญญาณเตือนเดียวกัน
    -   `group_wait: 30s`: หน่วงเวลา 30 วินาทีก่อนยิงข้อความแรก เพื่อรอจัดรวมก้อนเตือนภัยที่เกิดขึ้นในวินาทีใกล้เคียงกัน
    -   `group_interval: 5m`: รอเป็นเวลา 5 นาทีก่อนจะส่งข้อความแจ้งเตือนความคืบหน้าเพิ่มเติมของกลุ่มเดิม
    -   `repeat_interval: 2h`: ถ้ายอดปัญหาเดิมยังไม่ถูกแก้ไข ระบบจะหน่วงเวลา 2 ชั่วโมงก่อนยิงซ้ำ (ป้องกันเสียงเตือนรบกวนถล่มทลาย)

---

## 3. ช่องทางการส่งข้อมูลแจ้งเตือน (Discord Webhook Integration)

*   **Default Receiver:** ข้อความเตือนภัยและผลสรุปหลังแก้ปัญหา (`send_resolved`) จะถูกส่งออกไปยังช่องทาง **`discord-webhook`**
*   **การตั้งค่าใช้งานจริง (Remediation):**  
    ก่อนนำระบบขึ้นทำงานจริง ทีม DevOps จะต้องแก้ไข URL ตัวแปร **`webhook_url`** ในไฟล์ [alertmanager.yml](alertmanager.yml#L14) จากเดิมที่เป็นค่า placeholder ให้กลายเป็น URL Webhook จริงที่สร้างมาจากเซิร์ฟเวอร์ Discord ของบริษัท:
    ```yaml
    receivers:
    - name: 'discord-webhook'
      discord_configs:
      - webhook_url: 'https://discord.com/api/webhooks/YOUR_REAL_SECURE_CHANNEL_WEBHOOK_URL'
        send_resolved: true
    ```

---

## 4. วิธีการขึ้นระบบและเปิดมอนิเตอร์ (Verification Steps)

1.  เริ่มรัน Container ของ Alertmanager:
    ```bash
    docker-compose up -d alertmanager
    ```
2.  เข้าหน้าต่างควบคุมหลัก (สำหรับเปิดทำ Silence):
    *   **URL:** `http://localhost:9093` (ในโหมด Dev)
3.  ทดสอบจำลองส่งคำเตือน:
    -   เมื่อเกิดเหตุการณ์ Firing บน Prometheus, ข้อความเตือนจะถูกส่งมายังหน้าเว็บนี้เพื่อให้วิศวกรกดยอมรับทราบเรื่อง (Acknowledge) หรือกดระงับแจ้งเตือนชั่วคราวได้

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Context)
*   [Infrastructure, Telemetry & SLO Specification](../.docs/ai-context/spec-infra-devops.md)
*   [Prometheus Metrics Subsystem (prometheus/README.md)](../prometheus/README.md)
