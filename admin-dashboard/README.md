# Angular 19 Admin Dashboard Subsystem

> [!NOTE]
> เอกสารฉบับนี้เป็นคู่มือสำหรับนักพัฒนาซอฟต์แวร์สาย **Frontend (Angular)** เพื่อควบคุมดูแลการพัฒนา ทำความเข้าใจโครงสร้างการวาดแผนที่ Canvas และการเชื่อมโยงระบบข้อมูลแบบเรียลไทม์

---

## 1. บทบาทและหน้าที่หลักของระบบ (System Role)
Admin Dashboard เป็นแอปพลิเคชันรูปแบบ Single Page Application (SPA) ที่ทำหน้าที่เป็นหน้าจอควบคุมและเฝ้าดูความคืบหน้าของกองรถ (Fleet Operations) ข้อมูลพิกัด Rider และการประมวลผลออเดอร์:
1.  **Operations Map:** แสดงพิกัด Rider, เส้นทางเดินทางแบบถนนจริง (OSRM Polylines), และความคลาดเคลื่อนสัญญาณพิกัด
2.  **Order Dispatch Simulator:** จำลองการสร้างออเดอร์ ยิงพิกัดเสมือนของ Rider และสังเกตการคำนวณจับคู่ของ AI VRP (SimMap)
3.  **Fleet Management:** จัดการและตรวจสอบสถานะของคนขับ (Rider) และร้านค้าพันธมิตร (Shops)

---

## 2. ข้อกำหนดเบื้องต้นและการติดตั้ง (Prerequisites & Setup)

### ข้อกำหนดทางเทคนิค (Prerequisites)
*   **Node.js:** แนะนำเวอร์ชัน LTS (v18 หรือ v20)
*   **Angular CLI:** ติดตั้งทั่วโลกหรือรันผ่าน `npx` (ใช้เวอร์ชัน 19)

### วิธีการรันโปรเจกต์ภายในเครื่อง (Local Run)
1.  ไปที่ไดเรกทอรีโครงการหน้าบ้าน:
    ```bash
    cd c:\Users\ASUS\Desktop\Project\Delivery\admin-dashboard
    ```
2.  ติดตั้งแพ็กเกจและ Dependencies (รวมถึง OpenAPI Generator สำหรับสร้าง API DTOs):
    ```bash
    npm install
    ```
3.  เปิดรันบริการในโหมดพัฒนา (Development Mode):
    ```bash
    npm run start
    ```
    *(หน้าจอแอดมินบอร์ดจะรันบนพอร์ต `http://localhost:4201` เพื่อหลีกเลี่ยงการชนกับพอร์ตอื่นๆ)*
4.  การคอมไพล์เพื่อใช้งานจริง (Production Build):
    ```bash
    npm run build
    ```

---

## 3. การจัดการสถานะและการทำงานเรียลไทม์ (State & SignalR WebSockets)
หัวใจของความลื่นไหลในระบบคือการรับพิกัดสดของ Rider จาก Backend:
-   **SignalR Ingestion Service:** จัดการผ่าน [tracking-signalr.service.ts](file:///c:/Users/ASUS/Desktop/Project/Delivery/admin-dashboard/src/app/core/services/tracking-signalr.service.ts)
    -   เชื่อมต่อกับ Backend Hub ที่ปลายทาง `/hubs/tracking`
    -   ดักรับเหตุการณ์เรียลไทม์ เช่น `RiderLocationUpdated`, `OrderStatusChanged`, `OfferAcceptedResult`, `DispatchScanStarted`, `DispatchCandidatesRanked`
-   **กฎการป้องกันหน่วยความจำรั่วไหล (Memory Leak Prevention Rule):**
    -   หน้าจอแดชบอร์ดมีการวาด Marker บนแผนที่ตลอดเวลา จึงห้ามปล่อยให้ Subscription และ Layer แผนที่ค้างอยู่เมื่อปิด Component
    -   ทุก Component หน้าจอจะต้องทำลายล้าง Layer ล้าง Marker ของ Leaflet และกดยกเลิกการรับข่าวสาร (`unsubscribe`) ทุกตัวในฟังก์ชัน `ngOnDestroy()` เสมอ เพื่อประหยัดแรมของเบราว์เซอร์

---

## 4. ส่วนประกอบหน้าจอหลัก (Key UI Components)

### 4.1 แผงแผนที่ติดตามพนักงาน [MapComponent](file:///c:/Users/ASUS/Desktop/Project/Delivery/admin-dashboard/src/app/features/map/map.component.ts)
ทำหน้าที่วาด Leaflet Map และอัปเดตเส้นทางเดินของรถ:
*   **Canvas Rendering:** เพื่อความลื่นไหลในการแสดงพนักงานขับรถหลักร้อยคนพร้อมกัน หน้าแผนที่สลับมาเรนเดอร์หมุดด้วย **HTML5 Canvas** (แทนการใช้ DOM/SVG Node มาตรฐาน) ช่วยเพิ่มประสิทธิภาพ Frame Rate
*   **Anti-XSS Ingestion (ความปลอดภัยป๊อปอัพ):**  
    ห้ามทำ Raw String Interpolation ลงใน HTML Popups บนแผนที่ตรงๆ (เสี่ยงภัยคุกคาม Cross-Site Scripting) ให้ทำการ Escape ค่าตัวแปรเสมอ และประกาศใช้วิธี **Programmatic Event Binding** (เช่น `L.DomEvent.on(...)`) แทนการฝัง inline `onclick` ลงบนโค้ด String ของป๊อปอัพ
*   **GPS Accuracy Signal Circles:** เรนเดอร์ตำแหน่งพิกัดที่ไม่แม่นยำ (ความคลาดเคลื่อน GPS สูง) เป็นวงกลมสีเทาโปร่งแสงแสดงขนาดรัศมีฟิลเตอร์ตามความคลาดเคลื่อนจริง

### 4.2 ตารางรายชื่อคนขับ [RidersComponent](file:///c:/Users/ASUS/Desktop/Project/Delivery/admin-dashboard/src/app/features/riders/riders.component.ts)
*   ใช้สำหรับติดตามความพร้อมของคนขับ แสดงตารางรายการแยกตามสถานะ `IDLE` (สีเขียว), `BUSY` (สีแดง), `OFFLINE` (สีเทา)
*   **การอัปเดตอัตโนมัติ:** เมื่อคนขับส่งสัญญาณพิกัดหรือเปลี่ยนสถานะ ระบบจะเรียกฟังก์ชัน `recalculateStats()` ทันที เพื่อปรับยอดจำนวนรวมของพนักงานว่างและงานกำลังวิ่งบนหน้าต่างด้านบนโดยผู้ใช้ไม่ต้องรีเฟรชหน้าจอ (Reactive Refresh)

### 4.3 แผงติดตามคำสั่งซื้อ [OrdersComponent](file:///c:/Users/ASUS/Desktop/Project/Delivery/admin-dashboard/src/app/features/orders/orders.component.ts)
*   แสดงคำสั่งซื้อที่เข้ามาในคิวจัดส่ง สังเกตและวิเคราะห์ออเดอร์ที่ล่าช้า (Backlog Orders) หรือไรเดอร์ที่หมดเวลาข้อเสนอ (Dispatch Timeout)

---

## 5. ค่าคงที่สภาพแวดล้อม (Environment Variables)
ค่ากำหนดการเชื่อมต่อระบบถูกจัดเก็บในไฟล์ [environment.ts](file:///c:/Users/ASUS/Desktop/Project/Delivery/admin-dashboard/src/environments/environment.ts):
```typescript
export const environment = {
  production: false,
  apiUrl: '/api/v1',                   // endpoint ของ Backend API
  hubUrl: '/hubs/tracking',           // endpoint สำหรับ SignalR WebSocket
  osmTileUrl: '/tiles/{z}/{x}/{y}.png' // endpoint การดึงแผนที่ Cache (หรือใช้ OpenStreetMap tile ตรง)
};
```

---

## 🔗 เอกสารอ้างอิง Spec เชิงลึก (Original Contracts)
*   [Angular Admin Specification Sheet](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/spec-frontend.md)
*   [Double-submit CSRF Protection and Secure Cookie Spec](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/contracts/api-contracts.md)
*   [SignalR Event Payloads Matrix](file:///c:/Users/ASUS/Desktop/Project/Delivery/.docs/ai-context/contracts/signalr-contracts.md)
