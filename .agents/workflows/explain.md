---
description: แกะลอจิกและอธิบายการทำงานของ Legacy/Complex Code
---

# 🧠 Workflow: อธิบายการทำงาน (Explain Code)

ใช้สำหรับการให้ AI ช่วยแกะการทำงานของบล็อกโค้ดหรือ Logic ของระบบเก่าที่ซับซ้อน:

## 🚦 ขั้นตอนการทำงาน:
1. **Analyze Target**: อ่านเนื้อหาโค้ดในไฟล์เป้าหมาย (ด้วยเครื่องมือเปิดอ่านไฟล์)
2. **Reverse Engineering**:
   - แกะความเชื่อมโยงต่างๆ ระหว่าง Dependency (เช่น Controller สัมพันธ์กับ Model ไหน)
   - สรุป Data flow ขาเข้า-และขาออก (Inputs / Outputs)
3. **Diagram Generation**: 
   - สร้าง Flowchart วาดด้วย **MermaidJS** เพื่ออธิบายการทำงานให้เป็นรูปภาพสแกนสายตาง่าย
4. **Context Preservation**:
   - ผลการแกะสเปคที่มีขนาดยาว ให้สร้างเป็นเอกสาร Artifact ที่ `.ai/shared/logs/scratch/explain_[name].md`
   - พิมพ์ในแชทแค่หัวข้อย่อๆ และส่วนของ Mermaid Diagram

**ตัวอย่างการเรียกใช้:**
> `/explain การทำงานของ AuthController`
