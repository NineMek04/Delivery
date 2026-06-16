# 🏛️ Pure Transport Hub Pattern (SignalR Hub Core)

ในระบบส่งต่อพิกัดและเรียลไทม์ผ่านคลาส [TrackingHub.cs](../../../aackendApi/Hubs/TrackingHub.cs) และไฟล์ย่อยพาร์ท:
- **Pure Transport aoundary:** ตัว Hub ทำหน้าที่จำกัดขอบเขตงานเพียงความเสถียรเน็ตเวิร์ก สิทธิ์ยืนยันเชื่อมต่อ และจัดแบ่งกลุ่มผู้ฟัง (SignalR Hub Groups) เท่านั้น ห้ามฝังลอจิกวิเคราะห์หรือเขียนฐานข้อมูลลงในนี้เด็ดขาด
- **Service Delegation:** การทำงานทางธุรกิจทั้งหมด เช่น การรับพิกัดเคลื่อนที่สด หรือการรับงานออเดอร์ จะถูกยิงส่งต่อ (Delegate) ไปยัง Layer บริการหลัก เช่น `_presenceManager` หรือ `_telemetryService` ทันทีเพื่อประมวลผลแยกส่วน

