# ⚡ ระบบอัปเดตและย้ายโครงสร้างฐานข้อมูลอัตโนมัติ (Automatic Database Migration & Seeding)

เพื่อความง่ายในการพัฒนาและติดตั้งระบบแบบไม่ต้องจัดการทีละขั้นตอนด้วยตนเอง (Zero-Ops Development Startup) ระบบได้รวมเอาการย้ายฐานข้อมูลและใส่ข้อมูลเริ่มต้นไว้ที่ระดับ Startup บูตหลังบ้าน:

- **ตำแหน่งเรียกใช้งาน:** เมธอดขยาย `app.MigrateDatabaseAsync()` ในไฟล์ [Program.cs](../../../BackendApi/Program.cs) ซึ่งประมวลผลลอจิกใน [DatabaseMigrationSetup.cs](../../../BackendApi/Setup/Extensions/DatabaseMigrationSetup.cs)
- **กลไกการทำงาน:**
  1. **Ensure Baseline History:** เรียกใช้ `MigrationBaselineCompatibility.EnsureBaselineHistoryAsync` เพื่อตรวจสอบตารางประวัติ EF migrations ให้เข้ากันได้กับฐานข้อมูลรุ่นเก่าหรือรุ่นใหม่
  2. **Auto-Execution of Pending Migrations:** ค้นหาประวัติ EF Migrations ค้างคา หากพบจะสั่งรันคำสั่ง DDL `context.Database.MigrateAsync()` ปรับปรุง Schema เป็นเวอร์ชันล่าสุดโดยอัตโนมัติ
  3. **Advanced Schema configuration:** ประสานงานส่งต่อให้ [PostgresAdvancedConfigurator](../../../BackendApi/ServiceMigration/PostgresAdvancedConfigurator.cs) จัดตั้งตาราง Partition, Cluster และ Seed database views นอก EF context ทันที
  4. **Data Seeding & Mock Data:** ตรวจจับตัวแปร config `SeedMockData` หากเปิดใช้งานจะเรียก `DataSeeder.SeedAsync` เพื่อประมวลผลเขียนข้อมูลจำลองของร้านค้า เมนูอาหาร และบัญชีผู้ใช้อัตโนมัติ

---

> [!WARNING]
> ### ⚠️ จุดระวัง: ดาบสองคมของ Auto-Migration (Tech Lead's Warning)
> 
> **วิเคราะห์เชิงวิศวกรรม:**  
> การทำ `app.MigrateDatabaseAsync()` ตอน Startup ยอดเยี่ยมมากสำหรับโหมด Development / Sandbox แต่ **"ใน Production จริง"** หากเราทำการขยายระบบแบบ Multi-instance (เช่น Scale backend เป็น 5 instances เพื่อรองรับโหลดสูง) และมัน boot ขึ้นมาพร้อมๆ กัน ทั้ง 5 ตัวจะพยายามรันคิวรีสร้างตารางหรือสลับโครงสร้าง Schema แย่งกันชิงความเร็ว (Race Condition ระดับ Database Schema) ซึ่งจะส่งผลให้ PostgreSQL เกิด **Deadlock** และตารางล็อก ส่งผลให้บูตไม่ผ่านหรือเสียหายได้
> 
> **แนวทางปฏิบัติบน Production (Action Items):**
> 1. **ปิด Auto-Migration บน App Startup:** ตั้งค่าปิดการทำงานของการย้ายฐานข้อมูลในระดับ Code/Config ตอนรันบนโปรดักชัน
> 2. **รัน Migration ผ่าน CI/CD Pipeline หรือ one-shot runner:** เปลี่ยนไปรันคำสั่ง Migration ผ่าน CI/CD Pipeline (Single Runner) ก่อนทำการ Deploy ตัวแอปหลัก ห้ามเพิ่ม Kubernetes ตาม forbidden stack ของโปรเจกต์

