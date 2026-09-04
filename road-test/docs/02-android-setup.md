# 02 — Android Setup & APK Build Guide

## 1. การตั้งค่า Base URL ใน Flutter Rider App

ก่อน Build APK ให้ตรวจสอบหรือกำหนด Server Endpoint (Public Tunnel URL จากขั้นตอนที่ 01) ในแอปพลิเคชัน:

* ปิด Mock GPS (`ENABLE_MOCK_GPS: false`)
* กำหนด Base API URL ให้ชี้ไปยัง Public URL (เช่น `https://xxxx.trycloudflare.com`)

---

## 2. การคอมไพล์ Android APK

รันคำสั่งภายในไดเรกทอรี `rider_app/`:

```bash
cd rider_app
flutter build apk --release
```

ผลลัพธ์ไฟล์ APK จะอยู่ที่:
`rider_app/build/app/outputs/flutter-apk/app-release.apk`

---

## 3. การติดตั้งลงบนโทรศัพท์จริง

1. ต่อโทรศัพท์ Android เข้ากับคอมพิวเตอร์ผ่านสาย USB (เปิด USB Debugging)
2. สั่งติดตั้งผ่าน ADB:
   ```bash
   adb install -r rider_app/build/app/outputs/flutter-apk/app-release.apk
   ```
3. หรือส่งไฟล์ APK ไปยังโทรศัพท์แล้วกด Install

---

## 4. การให้สิทธิ์ (Permissions) บนมือถือ

เมื่อเปิดแอปพลิเคชันครั้งแรก:
* **Location Permission:** เลือก **"Allow all the time" (อนุญาตตลอดเวลา)** เพื่อให้ GPS ทำงานได้แม้ปิดหน้าจอหรือพับแอป
* **Battery Optimization:** ตั้งค่าเป็น **"Unrestricted" (ไม่จำกัด)** เพื่อป้องกันไม่ให้ Android OS ปิดแอปขณะทำงานในพื้นหลัง
