# 🛠️ Command Run Sheet (PowerShell Script Guide - Documents/infrastructure/RUN-COMMANDS.md)

ไฟล์นี้รวบรวมคำสั่ง PowerShell สำหรับการรัน Service และการรัน Test ต่างๆ ภายในระบบ Delivery Smart Routing System เพื่อความสะดวกในการคัดลอกไปรันด้วยตนเอง (Manual Execution)

---

## ⚡ 0. Quick Start (รันระบบทั้งหมดด้วยสคริปต์เดียว)

หากต้องการรันทุกบริการของระบบขึ้นมาพร้อมกันในหน้าต่างแยกโดยอัตโนมัติ (และ Bypass นโยบายความปลอดภัยชั่วคราวเพื่อให้รันสคริปต์ได้):

```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery

# รันสคริปต์หลักด้วยการบายพาสนโยบายความปลอดภัย
powershell -ExecutionPolicy Bypass -File .\RootScripts\scripts\start-all-apps.ps1
```

---

## 🏛️ 1. Docker Backing Services (บริการพื้นฐาน)

คำสั่งสำหรับจัดการกับ Infrastructure Containers (PostgreSQL, Redis, RabbitMQ, OSRM, Seq, Prometheus, Grafana)

### 🚀 เริ่มต้นบริการ Backing Services
หยุด Container สำหรับแอปพลิเคชันที่อาจซ้ำซ้อน และสั่งรันบริการพื้นฐานทั้งหมด:
```powershell
# หยุดคอนเทนเนอร์หลักที่อาจทำงานทับซ้อนกับการรันแบบ Local
docker compose stop backend frontend rider-app ai-service

# รันบริการ Backing Services ในโหมด Background
docker compose up -d db redis rabbitmq osrm seq prometheus grafana
```

### 🛑 หยุดบริการทั้งหมด
```powershell
docker compose down
```

---

## ⚙️ 2. Running Services Locally (รันทีละเซอร์วิสแบบ Local)

เพื่อความสะดวกในการ Debug คุณสามารถเปิด Terminal ใหม่สำหรับแต่ละบริการและรันคำสั่งเหล่านี้:

### 🟢 2.1 Backend API (.NET 8)
*รันบนพอร์ต: `http://localhost:5000`*

**แบบ Multi-line (คัดลอกทั้งหมดวางใน PowerShell):**
```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery
powershell -ExecutionPolicy Bypass -File .\RootScripts\scripts\start-backend.ps1
```

**แบบบรรทัดเดียว (Single Line Copy-Paste):**
```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery; powershell -ExecutionPolicy Bypass -File .\RootScripts\scripts\start-backend.ps1
```

---

### 🤖 2.2 AI Routing Engine (Python FastAPI + OR-Tools)
*รันบนพอร์ต: `http://localhost:8000`*

**แบบ Multi-line (คัดลอกทั้งหมดวางใน PowerShell):**
```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery\ai-engine
$env:DATABASE_URL="postgresql://postgres:$env:POSTGRES_PASSWORD@localhost:5432/delivery_db"
if (Test-Path "venv") { .\venv\Scripts\activate }
uvicorn main:app --reload --port 8000
```

**แบบบรรทัดเดียว (Single Line Copy-Paste):**
```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery\ai-engine; $env:DATABASE_URL="postgresql://postgres:$env:POSTGRES_PASSWORD@localhost:5432/delivery_db"; if (Test-Path "venv") { .\venv\Scripts\activate }; uvicorn main:app --reload --port 8000
```

---

### 🎨 2.3 Angular Admin Dashboard
*รันบนพอร์ต: `http://localhost:4200`*

```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery\admin-dashboard
npm start -- --port 4200
```

---

### 📱 2.4 Flutter Rider App (Web Port 8080)
*รันบนพอร์ต: `http://localhost:8080`*

**แบบรัน Local (ผ่าน Chrome Browser):**
```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery\rider_app
flutter run -d chrome --web-port 8080 --dart-define=API_BASE_URL=http://localhost:5000
```

**แบบรันผ่าน Docker (ในกรณีไม่มี Flutter SDK ในระบบ):**
```powershell
docker compose up -d --build rider-app
```

---

## 💾 3. Database Operations (การจัดการฐานข้อมูล)

สำหรับกรณีต้องการอัปเดต Database Schema ด้วย EF Core Migrations:

```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery\BackendApi

# อัปเดตฐานข้อมูลเป็นเวอร์ชันล่าสุด
dotnet ef database update

# สร้าง Migration ใหม่ (แทนที่ [MigrationName] ด้วยชื่อที่ต้องการตั้ง)
dotnet ef migrations add [MigrationName]
```

---

## 🗺️ 4. OSRM Map Preparation (เตรียมการแผนที่ออฟไลน์)

หากยังไม่มีไฟล์ข้อมูลแผนที่ OSRM Udon Thani ให้รันคำสั่งเหล่านี้เพื่อดาวน์โหลดและคอมไพล์ระบบโครงข่ายถนน:

```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery
# รันผ่านสคริปต์ช่วยทำ
powershell -ExecutionPolicy Bypass -File .\RootScripts\scripts\setup-osrm.ps1

# สั่ง Docker รีสตาร์ทเซอร์วิส OSRM หลังคอมไพล์เสร็จ
docker compose restart osrm
```

---

## 🧪 5. Testing & Simulations (การรันเทสระบบและบอทจำลอง)

รวบรวมคำสั่งสำหรับการทดสอบส่วนต่างๆ ภายใต้โฟลเดอร์ทดสอบกลาง `RootScripts/scripts.test/`

### 🧪 5.1 C# Backend Tests
```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery

# รัน Integration Tests ทั้งหมด
dotnet test RootScripts/scripts.test/test/BackendApi.IntegrationTests

# รัน Unit Tests ทั้งหมด
dotnet test RootScripts/scripts.test/test/BackendApi.UnitTests
```

### 🐍 5.2 Python AI Engine Tests (PyTest)
```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery
if (Test-Path "ai-engine\venv") { .\ai-engine\venv\Scripts\activate }

# รัน PyTest ทั้งหมดในโฟลเดอร์ AI Engine Tests
pytest RootScripts/scripts.test/test/ai-engine.tests

# รันเฉพาะไฟล์ที่ต้องการ (เช่น test_eta_velocity.py)
python -m pytest RootScripts/scripts.test/test/ai-engine.tests/test_eta_velocity.py
```

### 🤖 5.3 E2E Simulator (บอทจำลองไรเดอร์วิ่งส่งงาน)
```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery

# รันบอทจำลอง E2E เสมือนจริง
node RootScripts/scripts.test/test/e2e-simulator/simulate-e2e.js

# รันบอทจำลองความเข้ากันได้ของระบบ Flutter
node RootScripts/scripts.test/test/e2e-simulator/test-flutter-compat.js
```

### 📈 5.4 System Load & Stress Tests (การทดสอบความเสถียรเมื่อมีโหลดสูง)
ก่อนรัน ตรวจสอบให้แน่ใจว่าติดตั้ง Node Packages แล้ว:
```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery\RootScripts\scripts.test\test\load-test
npm install
```

รันคำสั่งทดสอบผ่าน `npm run` (จาก root directory หรือ load-test directory):
```powershell
cd c:\Users\ASUS\Desktop\Project\Delivery

# ทดสอบโหลดส่วน SignalR Connections
npm --prefix RootScripts/scripts.test/test/load-test run test:signalr

# ทดสอบโหลดการเรียกใช้งาน REST API
npm --prefix RootScripts/scripts.test/test/load-test run test:api

# ทดสอบการรับส่งออเดอร์และการจัดสรรงานความถี่สูง (Dispatch)
npm --prefix RootScripts/scripts.test/test/load-test run test:dispatch

# ทดสอบสถานการณ์ตัดการเชื่อมต่อและต่อสายใหม่พร้อมกัน (Reconnect Chaos)
npm --prefix RootScripts/scripts.test/test/load-test run test:reconnect

# ทดสอบเสถียรภาพการรับส่งข้อมูล (Resilience Stress)
npm --prefix RootScripts/scripts.test/test/load-test run test:resilience
```
