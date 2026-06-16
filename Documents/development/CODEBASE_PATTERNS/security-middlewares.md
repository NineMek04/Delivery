# 🛡️ Security Header & Middleware Handling

ระบบมีการสกัดทราฟฟิกระดับ Gateway ผ่านการขึ้นรูป Middleware หลัก 3 ตัว:

### 🟢 1. การกู้ประวัติเพื่อสืบสวนทรานแซกชัน ([CorrelationIdMiddleware.cs](../../../BackendApi/Setup/Middlewares/CorrelationIdMiddleware.cs))
- สแกน Request Headers เพื่อหาค่า `X-Correlation-Id` (หรือสุ่มสร้างขึ้นใหม่หากไม่พบ)
- ฝังคีย์ลงในหน่วยความจำชั่วคราว `context.Items["CorrelationId"]`
- ตอบกลับคีย์ดังกล่าวไปกับ Response Headers
- นำค่าคีย์ใส่เข้าไปใน **Serilog LogContext** (`LogContext.PushProperty`) ทำให้บรรทุกล็อกระบบทั้งหมดที่ถูกเรียกภายใต้ Request นี้ ถูกเชื่อมรอยประสาน (Trace Correlation) สะดวกต่อการสืบค้นบน Seq Dashboard

### 🟠 2. ระบบสกัดการส่งคำสั่งปลอมแปลง ([CsrfValidationMiddleware.cs](../../../BackendApi/Setup/Middlewares/CsrfValidationMiddleware.cs))
- ป้องกันการโจมตีประเภท CSRF (Cross-Site Request Forgery) เฉพาะการเชื่อมต่อระดับ Cookie (เช่น แผงแอดมินบอร์ด Angular)
- **Host Header Verification:** ตรวจสอบความถูกต้องของ Host ป้องกัน DNS Rebinding
- **Origin/Referer Whitelist:** เทียบข้อมูลทิศทางที่มาของ HTTP Header กับรายการ CORS Whitelist
- **Double-Submit Cookie:** ยืนยันสิทธิ์โดยการเทียบค่าความปลอดภัยของคุกกี้ `XSRF-TOKEN` กับ Header `X-XSRF-TOKEN` หากไม่ตรงกันจะทำการปิดกั้นการเข้าถึงระดับ HTTP 403 Forbidden ทันที

### 🔴 3. การบังคับเพิ่มความปลอดภัย ([SecurityHeadersMiddleware.cs](../../../BackendApi/Setup/Middlewares/SecurityHeadersMiddleware.cs))
- แนบหัวข้อมาตรฐานความปลอดภัยระดับสากลไปกับทุกการตอบสนอง:
  - `Referrer-Policy: strict-origin-when-cross-origin`
  - `X-Content-Type-Options: nosniff` (สกัดภัยการเปลี่ยนประเภทไฟล์ไบนารีบนเบราว์เซอร์)
  - `X-Frame-Options: DENY` (ป้องกันการโจมตีประเภท Clickjacking)
  - `Content-Security-Policy (CSP):` ตั้งกฎจำกัดขอบเขตการโหลดทรัพยากร เพื่อบล็อกการฉีดคำสั่ง XSS

