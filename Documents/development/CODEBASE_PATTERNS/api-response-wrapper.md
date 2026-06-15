# 📦 รูปแบบการห่อหุ้มคำตอบรับ API ขากลับ (Unified REST API Response Wrapper)

เพื่อรักษาความเป็นมาตรฐานของคำตอบรับทาง HTTP (Unified API Contracts) ของทั้งโครงการ และอำนวยความสะดวกในการอ่านลอจิกขารับของฝั่ง Angular/Flutter ไคลเอนต์ ตัวระบบหลังบ้านมีการใช้ Action Filter สำหรับครอบโครงสร้าง JSON เสมอ:

- **ตำแหน่งตัวควบคุมหลัก:** [GlobalResponseFilter.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Core/Filters/GlobalResponseFilter.cs)

---

## 📊 1. โครงสร้างคำตอบรับมาตรฐาน (Standard Response JSON)

คำร้องขอ REST API ส่วนใหญ่เมื่อตอบกลับจะมีรูปแบบ JSON เหมือนกันทั้งหมด ดังนี้:
```json
{
  "status": 200,
  "success": true,
  "message": "สำเร็จ",
  "errors": null,
  "value": { ... } // ข้อมูลจริงที่ส่งกลับ (หรืออาจเป็นลิสต์ข้อมูล)
}
```
หากเป็นกรณีส่งผิดพลาด (เช่น HTTP 400 Validation Error) โครงสร้างจะเปลี่ยนเป็น:
```json
{
  "status": 400,
  "success": false,
  "message": "คำขอไม่ถูกต้อง",
  "errors": [ "รหัสฟิลด์ไม่สามารถว่างได้" ],
  "value": null
}
```

---

## ⚡ 2. กลไกการห่อหุ้มข้อมูลอัตโนมัติ (Automatic Response Wrapping Filter)

นักพัฒนาหลังบ้านไม่จำเป็นต้องสั่ง New `ApiResponse` ในทุกๆ Action Method ของ Controller ด้วยตนเอง แต่ระบบจะทำการห่อให้ผ่าน `GlobalResponseFilter` ทันทีเมื่อสั่ง Return วัตถุข้อมูล:

*   **เมื่อส่งสถานะสำเร็จ (HTTP 200-299):**  
    ระบบจะเอาผลลัพธ์ที่เป็นตัวแปร (เช่น DTO, Entity, String) ไปบรรจุลงเป็นค่า `value` ของคลาส `ApiResponse<object>` ทันที
*   **เมื่อพบข้อผิดพลาดทั่วไป (เช่น HTTP 400/404/500):**  
    จะทำการแปลงตัวแปรผิดปกติให้อยู่ในบล็อก `ApiResponse.Fail` และสลับค่า `success = false` พร้อมดึงรหัส Error Code และ Default Message ตาม HTTP Code ออกมาแนบแบบอัตโนมัติ
*   **กรณีข้ามข้อยกเว้น:**  
    หากคลาสผลลัพธ์ที่ส่งกลับเป็น `ApiResponse` อยู่แล้ว ตัวกรองจะปล่อยผ่านเพื่อให้คลาสสามารถปรับเปลี่ยนข้อมูลได้โดยไม่เกิดข้อผิดพลาดในการห่อซ้ำซ้อน

---

## 🛡️ 3. การยกเว้นไม่ใช้การครอบรูปแบบ (Response Wrapping Bypass)

สำหรับคำร้องขอบางประเภทที่ไม่จำเป็นต้องส่งค่ากลับเป็นโครงสร้าง JSON ดังกล่าว เช่น สถิติของ Prometheus (`/metrics`), ไฟล์รูปภาพยืนยันการรับส่งอาหาร, สัญญาณดาวน์โหลดสตรีมมิ่ง หรือหน้าจอยืนยันความสมบูรณ์ของระบบ (`/health`):

- **ตัวบ่งชี้การยกเว้น ([DisableWrapperAttribute.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Core/Attributes/DisableWrapperAttribute.cs)):**  
  นักพัฒนาหลังบ้านสามารถประกาศประดับหัวข้อเมธอดนั้นๆ ด้วยแท็ก `[DisableWrapper]` ได้ทันที:
  ```csharp
  [HttpGet("metrics")]
  [DisableWrapper]
  public IActionResult GetMetrics()
  {
      return File(metricsStream, "text/plain"); // ปล่อยดิบผ่าน Raw Stream ออกไป
  }
  ```
  เมื่อ `GlobalResponseFilter` ตรวจพบ Attribute ดังกล่าวจากคำสั่ง `EndpointMetadata` จะทำการข้ามผ่าน (Bypass) ไปรันคำสั่งปกติของระบบโดยไม่มีการเข้ามาแทรกแซงโครงสร้าง
