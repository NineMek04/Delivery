# 01 — Server Setup & Network Tunnel Guide

## 1. การเตรียม Server สำหรับทดสอบ

1. คัดลอกและตั้งค่า `.env` จาก template:
   ```bash
   cp road-test/config/.env.test.example .env
   ```
2. รัน Docker Test Server:
   ```bash
   docker compose -f docker-compose.yml -f road-test/docker/docker-compose.test.yml up -d
   ```
3. ตรวจสอบสถานะการทำงาน:
   ```bash
   bash road-test/scripts/health-check.sh
   ```

---

## 2. การสร้าง Tunnel เพื่อให้มือถือเข้าถึงได้ผ่าน 4G/5G

เนื่องจากการทดสอบวิ่งบนถนนจริง มือถือจะหลุดออกจากวง Wi-Fi Local จึงจำเป็นต้องมี Public URL ชั่วคราว:

### ตัวเลือก A: Cloudflare Tunnel (แนะนำ)
```bash
cloudflared tunnel --url http://localhost:80
```
จะได้ URL เช่น `https://xxxx.trycloudflare.com`

### ตัวเลือก B: ngrok
```bash
ngrok http 80
```
จะได้ URL เช่น `https://xxxx.ngrok-free.app`

### ตัวเลือก C: Tailscale VPN
เชื่อมต่อเครื่อง Server และมือถือ Android เข้า Tailscale Network เดียวกัน แล้วใช้ IP ของ Tailscale เช่น `http://100.x.y.z:80`
