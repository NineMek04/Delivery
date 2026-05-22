---
module: RabbitMQ Dead Letter Queue (DLQ) & Schema Versioning Policy
status: Active Operational Policy
---

# 🐇 Dead Letter Queue (DLQ) & Schema Versioning Policy

## 1. Schema Versioning Policy (นโยบายการจัดการเวอร์ชันข้อมูล)
เพื่อรองรับการขยายตัวของระบบในอนาคตโดยไม่ทำให้ระบบเดิมพัง (Backward Compatibility) ให้ใช้กฎควบคุมดังนี้:
- ทุกๆ Event Payload ต้องมีฟิลด์ `"schemaVersion": 1` กำกับอยู่ด้านบนสุดเสมอ
- **Non-breaking changes:** การเพิ่มฟิลด์ใหม่ (Optional Fields) ➡️ ห้ามปรับเลขเวอร์ชัน ให้ใช้เวอร์ชันเดิมได้
- **Breaking schema changes:** การลบฟิลด์เดิม, เปลี่ยนประเภทตัวแปร (Data Type), หรือเปลี่ยนโครงสร้างหลัก ➡️ บังคับต้องขยับเลข `schemaVersion` ขึ้นทีละ 1 (Increment) และ**ต้องรักษาความเข้ากันได้ย้อนหลัง (Backward Compatibility) ให้ระบบเวอร์ชันก่อนหน้า 1 เวอร์ชัน (1 prior version)** ทำงานร่วมกันได้เสมอ

## 2. Operational Dead Letter Policy
- **Max Retry Count:** พยายามลองซ้ำสูงสุด 5 ครั้ง ในกรณีประมวลผลข้อความล้มเหลว
- **Retry Backoff:** ใช้กลไก Exponential Backoff (หน่วงเวลาทวีคูณ: 2s ➡️ 4s ➡️ 8s ➡️ 16s ➡️ 32s)
- **Poison Message Handling:** หากครบ 5 ครั้งแล้วยังล้มเหลว ระบบจะตีตราเป็นข้อความมีพิษ (Poison Message) และย้ายเข้าคิวล้างขยะ `delivery_dead_letter_queue` (DLQ) ทันที เพื่อไม่ให้บล็อกคิวงานหลัก
- **Alert Thresholds:** หากมีข้อความค้างในตู้ DLQ สะสมเกิน **10 ข้อความ** ระบบตรวจจับสถิติต้องยิงสัญญาณเตือนภัย (Alert) ขึ้นหน้าจอแอดมินทันที
