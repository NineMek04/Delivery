# 🏛️ Base Service / Unit of Work Pattern

การทำงานกับฐานข้อมูล EF Core ในระบบถูกห่อหุ้ม (Encapsulated) ด้วยคลาส [DBHandlerCore.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Core/DataHandlers/DBHandlerCore.cs) ทำหน้าที่เป็น Unit of Work และ Repository กลาง:

### ⚙️ เทคนิคการเขียนโค้ดขั้นสูง:
- **การจัดการ Audit เสมือนจริง (Automatic Audit Generation):**  
  เมื่อเรียก `InsertObject` หรือ `UpdateObject` ระบบจะใช้คุณลักษณะ **C# Reflection** เพื่อตรวจสอบฟิลด์แบบยืดหยุ่น (Case-insensitive) หากพบฟิลด์ เช่น `CreatedAt`, `CreatedBy`, `UpdatedAt` หรือ `UpdateUserId` ระบบจะเติมค่าวันเวลาระดับสากล (`DateTime.UtcNow`) และ UUID ผู้ใช้งานล่าสุดให้โดยอัตโนมัติ (`ApplyAuditValues`)
- **การลบแบบซอฟต์ดีลีทอัตโนมัติ (Reflection-based Soft Delete):**  
  ฟังก์ชัน `DeleteObjectAsync` จะเช็คฟิลด์บ่งบอกการลบ (`DelFlag` หรือ `DEL_FLAG`) ของโมเดลปลายทาง หากพบฟิลด์ดังกล่าวระบบจะทำการเปลี่ยนค่าเป็น `'Y'` และอัปเดตแทนการสั่ง Delete จริงออกนอกดิสก์
- **ดึงคิวรีตามเงื่อนไข (Conditional Query Filter):**  
  ฟังก์ชัน `GetQuery<TEntity>()` จะทำงานร่วมกับ `ConditionContext` เพื่อประยุกต์ใช้ Global Filters (เช่น กรองโมเดลที่ถูก Soft Delete ออกไปแล้ว หรือแยกสิทธิ์ข้อมูลตามสาขาของไรเดอร์)
