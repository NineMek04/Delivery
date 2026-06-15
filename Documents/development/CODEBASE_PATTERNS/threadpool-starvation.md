# ⚡ การป้องกันสภาวะหน่วงของกลุ่มเทรด (ThreadPool Starvation Prevention)

ในตอนสตาร์ตบูต API หลัก มีการกำหนดตั้งค่าขนาดเทรดพื้นฐานขั้นต่ำไว้ใน [Program.cs](file:///c:/Users/ASUS/Desktop/Project/Delivery/BackendApi/Program.cs#L20-L21):
```csharp
System.Threading.ThreadPool.SetMinThreads(1000, 1000);
```
- **เหตุผลเชิงลึกทางระบบ:**  
  เมื่อไรเดอร์นับร้อยหรือแอดมินหลายคนทำการเชื่อมต่อและส่งสัญญาณพิกัด GPS/Heartbeat เข้ามาพร้อมกันถี่ ๆ (Thundering Herd Problem) ตัวขับ EF Core และ StackExchange.Redis จะใช้การทำงานแบบ Asynchronous Tasks (`await`) หากสายการสร้างเทรดใหม่ของ .NET (ซึ่งสร้างที่ความเร็วต่ำเพียง 1-2 เทรดต่อวินาที) ทำตามปริมาณคำขอยิงไม่ทัน จะเกิดปัญหา **ThreadPool Starvation** ส่งผลให้แอปพลิเคชันหน่วงอย่างรวนและหมดเวลาทำงาน (Timeout) การจองเทรดล่วงหน้า 1,000 เทรดช่วยป้องกันภัยคอขวดนี้ได้อย่างเด็ดขาด
