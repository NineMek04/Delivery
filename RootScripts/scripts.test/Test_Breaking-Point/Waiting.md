# เพื่อป้องกันปัญหาในอนาคต (ถ้ามีการแก้เครือข่ายคอนเทนเนอร์)
- ถ้าในอนาคตต้องการสั่ง `docker network connect` แมนนวลสำหรับ RabbitMQ หรือบริการที่มีการพึ่งพากันผ่านชื่อบริการของ Docker Compose แนะนำให้แนบตัวแปร `--alias` ไปด้วยเสมอ เช่น:
```bash
docker network connect --alias rabbitmq delivery_default delivery-rabbitmq
```
-เพื่อไม่ให้ Docker Compose สูญเสีย DNS Hostname Mapping