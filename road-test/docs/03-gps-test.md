# 03 — GPS Test: Stationary & Walking Verification

## วัตถุประสงค์
ตรวจสอบว่าโทรศัพท์ Android อ่านค่า GPS จริงและส่งข้อมูลเข้าสู่ Backend + Redis + PostGIS ได้อย่างถูกต้อง ก่อนนำไปทดสอบบนยานพาหนะ

---

## 1. การทดสอบแบบอยู่นิ่งกับที่ (Test A: Stationary Test)

1. วางโทรศัพท์ไว้นิ่งๆ ในที่โล่งแจ้ง (เพื่อให้รับสัญญาณดาวเทียม GPS ได้ชัดเจน)
2. ล็อกอินเข้าสู่ Rider App และกด **"เริ่มงาน / เข้าสู่สถานะพร้อมรับงาน"**
3. ตรวจสอบการส่งพิกัด:
   * แอปแสดงค่า Latitude, Longitude, Accuracy ที่ถูกต้อง
   * ตรวจสอบว่าพิกัดถูกบันทึกลงใน Redis (`rider:{id}:location`)
   * ตรวจสอบว่าบันทึกลงในตาราง `GpsHistories` บน PostgreSQL

---

## 2. การทดสอบการเดิน (Test B: Walking Test)

1. ถือโทรศัพท์แล้วเดินเป็นระยะทาง **100 – 500 เมตร**
2. สังเกตหน้าจอ Admin Web Dashboard (`http://localhost:4201`):
   * Marker บนแผนที่ต้องเคลื่อนที่ตามตำแหน่งจริงแบบ Real-time ผ่าน SignalR
   * เส้นทางการเดิน (Breadcrumb Track) ถูกวาดต่อเนื่องตามแนวทางเดิน
3. บันทึกผลค่า Accuracy และค่า Latency ของการอัปเดต
