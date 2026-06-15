# 🛠️ System Helper & Bootstrap Scripts (RootScripts/scripts/README.md)

> [!NOTE]
> โฟลเดอร์นี้รวบรวมสคริปต์อัตโนมัติ (Automation Utility Scripts) สำหรับระบบปฏิบัติการ Windows (PowerShell - `.ps1`) และ Linux/macOS (Bash - `.sh`) เพื่อใช้เตรียมระบบ, บูตระบบงานไมโครเซอร์วิสแบบ Local และรันการตรวจสอบความปลอดภัยของโปรเจกต์

---

## 📚 รายละเอียดสคริปต์และการเรียกใช้งาน (Scripts Catalog)

### ⚡ 1. [start-all-apps.ps1](file:///c:/Users/ASUS/Desktop/Project/Delivery/RootScripts/scripts/start-all-apps.ps1)
- **วัตถุประสงค์:** สตาร์ตทุกตู้บริการพื้นฐานและแอปพลิเคชันย่อยพร้อมกันในหน้าต่างแยกโดยอัตโนมัติเพื่ออำนวยความสะดวกในการพัฒนาเชิงลึก (Developer Cockpit Bootstrap)
- **ขั้นตอนการทำ:**
  1. สั่งรันคอนเทนเนอร์ฐานระบบ `docker-compose up -d` (db, redis, rabbitmq, osrm, seq, prometheus, grafana)
  2. รอให้ฐานข้อมูลสตาร์ตพร้อมรับสิทธิ์เชื่อมต่อ
  3. เปิดหน้าต่าง PowerShell ย่อยรัน .NET Core Backend API
  4. เปิดหน้าต่างย่อยรัน FastAPI AI Engine
  5. เปิดหน้าต่างย่อยรัน Angular Admin Dashboard
- **วิธีการเรียกใช้งาน:**
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\RootScripts\scripts\start-all-apps.ps1
  ```

---

### 🟢 2. [start-backend.ps1](file:///c:/Users/ASUS/Desktop/Project/Delivery/RootScripts/scripts/start-backend.ps1)
- **วัตถุประสงค์:** ช่วยสตาร์ต .NET Backend API ในโหมด Development โดยใช้การเช็คความพร้อมก่อนรัน
- **ขั้นตอนการทำ:**
  - ทำการตั้งค่าสภาพแวดล้อมและรันคำสั่ง `dotnet run` ภายใต้โฟลเดอร์โครงการ Backend API
- **วิธีการเรียกใช้งาน:**
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\RootScripts\scripts\start-backend.ps1
  ```

---

### 🗺️ 3. แผนที่และประมวลผลทางถนนออฟไลน์ (OSRM Setup Scripts)
- **ไฟล์สคริปต์:**
  - Windows: [setup-osrm.ps1](file:///c:/Users/ASUS/Desktop/Project/Delivery/RootScripts/scripts/setup-osrm.ps1)
  - Linux/macOS: [setup-osrm.sh](file:///c:/Users/ASUS/Desktop/Project/Delivery/RootScripts/scripts/setup-osrm.sh)
- **วัตถุประสงค์:** ดำเนินกระบวนการดาวน์โหลดแผนที่ดิบจังหวัดอุดรธานี/ประเทศไทยจาก Geofabrik และสกัดแบ่งข้อมูลกราฟ Dijkstra นำไปป้อนให้กับ OSRM Engine
- **วิธีการเรียกใช้งาน:**
  - *Windows:*
    ```powershell
    powershell -ExecutionPolicy Bypass -File .\RootScripts\scripts\setup-osrm.ps1
    ```
  - *Linux/macOS:*
    ```bash
    chmod +x ./RootScripts/scripts/setup-osrm.sh
    ./RootScripts/scripts/setup-osrm.sh
    ```
- **รายละเอียดเพิ่มเติม:** ตรวจดูขั้นตอนทั้งหมดได้ที่ [OSRM Setup Guide](file:///c:/Users/ASUS/Desktop/Project/Delivery/Documents/setup/OSRM-SETUP.md)

---

### 🛡️ 4. ตรวจสอบความปลอดภัยและช่องโหว่ (Security Scan Scripts)
- **ไฟล์สคริปต์:**
  - Windows: [security-scan.ps1](file:///c:/Users/ASUS/Desktop/Project/Delivery/RootScripts/scripts/security-scan.ps1)
  - Linux/macOS: [security-scan.sh](file:///c:/Users/ASUS/Desktop/Project/Delivery/RootScripts/scripts/security-scan.sh)
- **วัตถุประสงค์:** รันการทดสอบและวิเคราะห์หาช่องโหว่ความปลอดภัยระดับซอร์สโค้ด (SAST) และตรวจสอบแพ็กเกจซอฟต์แวร์ย่อย (Dependency Scan) ตามมาตรฐานความปลอดภัยระดับสูงเพื่อความปลอดภัยก่อนส่งมอบ
- **วิธีการเรียกใช้งาน:**
  - *Windows:*
    ```powershell
    powershell -ExecutionPolicy Bypass -File .\RootScripts\scripts\security-scan.ps1
    ```
  - *Linux/macOS:*
    ```bash
    chmod +x ./RootScripts/scripts/security-scan.sh
    ./RootScripts/scripts/security-scan.sh
    ```

---

### 🔑 5. [vault-bootstrap.sh](file:///c:/Users/ASUS/Desktop/Project/Delivery/RootScripts/scripts/vault-bootstrap.sh)
- **วัตถุประสงค์:** ใช้สำหรับกำหนดค่าเริ่มต้นและฉีดข้อมูลความลับ (Secrets & Keys Injection) เช่น JWT Key, API Keys ลงไปใน HashiCorp Vault เพื่อเตรียมตัวสิทธิ์ความปลอดภัยในระดับ Production
- **วิธีการเรียกใช้งาน:**
  ```bash
  chmod +x ./RootScripts/scripts/vault-bootstrap.sh
  ./RootScripts/scripts/vault-bootstrap.sh
  ```
