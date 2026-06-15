# 📄 การโหลดตัวแปรสภาพแวดล้อมจำลอง (Custom Dotenv Variable Mapping)

สเปกการแปลงระบบตัวแปรสิ่งแวดล้อมข้ามภาษาและโมเดลจำลอง:
- **ตำแหน่งในโค้ด:** เรียกใช้งาน `DotEnvLoader.Load` ใน [Program.cs](../../../BackendApi/Program.cs#L24-L30)
- **เหตุผลเชิงเทคนิค:** เพื่อให้อ่านค่าจากไฟล์ `.env` ได้อย่างสอดคล้องกับมาตรฐานการเข้าถึง Configuration ของ .NET ระบบจึงแปลงตัวอักษรดับเบิ้ลอันเดอร์สกอร์ `__` ใน `.env` ให้เป็นเครื่องหมายโคลอน `:` บนคอลเลกชันในหน่วยความจำ ตัวอย่างเช่น:
  - ค่าอินพุต: `ConnectionStrings__DefaultConnection`
  - ค่าที่ใช้จริงในแอปพลิเคชัน: `ConnectionStrings:DefaultConnection`
  ช่วยประสานการตั้งค่าร่วมกับ Secret Storage เช่น Docker Env และ Vault ได้อย่างไร้รอยต่อ
