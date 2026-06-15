# 🏛️ Base Controller Pattern

คลาส [DeliveryControllerBase.cs](../../../BackendApi/Core/DeliveryControllerBase.cs) ทำหน้าที่เป็นจุดศูนย์กลางของการควบคุม REST API ทั้งหมดของ .NET 8 Backend:

### ⚙️ คุณลักษณะทางสถาปัตยกรรม:
- **Routing & Content-Type:** บังคับโครงสร้างพาธ API รูปแบบเดียวกันทั้งหมดผ่าน `[Route("api/v1/[controller]")]` และส่งกลับ JSON เท่านั้นผ่าน `[Produces("application/json")]`
- **Dynamic Dependency Resolution (Lazy-loading):**  
  แทนที่การเปิดใช้ Constructor Injection ในทุก ๆ Controller ซึ่งก่อให้เกิดการส่งพารามิเตอร์ซ้ำซาก (Bloated Constructors) ระบบใช้คุณสมบัติสืบค้นจาก HttpContext:
  ```csharp
  protected DBHandlerCore DB => _db ??= HttpContext.RequestServices.GetRequiredService<DBHandlerCore>();
  ```
- **Claim Accessors:** มีฟังก์ชัน `CurrentUserId` ในตัว เพื่อดึงข้อมูล `NameIdentifier` หรือ `sub` จาก JSON Web Token (JWT) ของผู้ใช้ที่กำลังล็อกอินแบบอัตโนมัติ
