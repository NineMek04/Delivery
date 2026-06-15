# 🎨 Fluent API & Validation Pattern

### 🟢 1. การคัดกรองข้อมูลเข้าแบบรัดกุมอัตโนมัติ ([ValidationFilter.cs](../../../BackendApi/Core/Filters/ValidationFilter.cs))
- ระบบผูกใช้สัญญากับ **FluentValidation** 
- ดักจับข้อมูลขารับก่อนเข้าถึงตัวแอปพลิเคชันหลัก ด้วย Action Filter `ValidationFilter`
- ค้นหา Validator (`IValidator<T>`) ใน DI Container สำหรับพารามิเตอร์แต่ละตัวของ Action Method โดยอัตโนมัติ หากข้อมูลไม่ตรงตามกำหนด ระบบจะ short-circuit ส่ง HTTP 400 และรายละเอียดฟิลด์ที่ไม่ถูกต้องกลับทันที เพื่อประหยัด CPU ในฝั่ง Business logic

### 🟠 2. การประกาศโครงสร้างเชิงพื้นที่ ([ApplicationDbContext.cs](../../../BackendApi/Data/ApplicationDbContext.cs))
- ตั้งค่าส่วนขยาย Spatial PostGIS: `modelBuilder.HasPostgresExtension("postgis");`
- ประกาศทำดัชนีเชิงพื้นที่ (Spatial Index) โดยใช้โครงสร้าง **GiST (Generalized Search Tree)** บนตารางหลักเพื่อความเร็วในการเทียบพิกัด:
  ```csharp
  modelBuilder.Entity<Rider>()
      .HasIndex(r => r.CurrentLocation)
      .HasMethod("gist")
      .HasDatabaseName("IX_Riders_CurrentLocation_Gist");
  ```
- ยกเว้นไม่ประกาศทำ Index ของตาราง Partition อย่าง `RiderLocationHistories` ที่ฝั่ง Fluent API เพื่อป้องกันปัญหา Migration ลบและสร้างอินเด็กซ์ซ้ำ (ย้ายสิทธิ์การควบคุมดัชนีไปที่ Raw SQL ใน Migration setup แทน)
