# Infrastructure, Telemetry And SLO

**Version:** 1.0.0 | **Updated:** 2026-06-14

## 1. Compose Topology

Active services:

`db`, `pgbouncer`, `backend`, `redis`, `ai-service`, `frontend`, `rider-app`,
`osrm`, `nginx-proxy`, `seq`, `prometheus`, `grafana`, `alertmanager`,
`rabbitmq`, `vault`, `vault-bootstrap`

Base compose ไม่ควร expose internal ports. Development override bind ทุก port
กับ `127.0.0.1`.

The development override builds `rider-app` with `ENABLE_MOCK_GPS=true` so the
browser can exercise dispatch flows without geolocation. The base Docker build
argument defaults to `false` and production must not enable it.

| Service | Dev Host Port | Container Port |
|---|---:|---:|
| Backend | 5000 | 80 |
| PostgreSQL | 5432 | 5432 |
| PgBouncer | 6432 | 5432 |
| Redis | 6379 | 6379 |
| AI | 8009 | 8000 |
| Admin | 4201 | 80 |
| Flutter web | 8083 | 80 |
| OSRM | 5001 | 5000 |
| Seq UI | 8082 | 80 |
| Prometheus | 9090 | 9090 |
| Grafana | 3000 | 3000 |
| RabbitMQ UI | 15672 | 15672 |
| Vault | 8200 | 8200 |

## 2. Database And Cache

- `postgis/postgis:15-3.3`
- PgBouncer transaction pooling
- Redis AOF, 256 MB, `allkeys-lru`; PostgreSQL fallback is mandatory
- ProcessedEvents cleanup ต้องใช้ indexed `ProcessedAt`
- partition/index DDL ต้อง idempotent และอยู่ใน service migration

## 3. Routing And Secrets

- OSRM uses MLD and local `udon-thani.osrm`
- production ห้าม public OSRM fallback
- Backend/AI secrets มาจาก Vault AppRole เมื่อ `VAULT_REQUIRED=true`
- ห้าม commit `.env`, tokens, passwords หรือ AppRole secret IDs

## 4. RabbitMQ

- integration events เท่านั้น; telemetry raw high-frequency ใช้ pipeline ที่กำหนด
- consumer ต้อง idempotent ผ่าน ProcessedEvents
- retry จำกัดสูงสุด 5 ก่อน DLQ
- management image เปิด Prometheus plugin ผ่าน `rabbitmq/enabled_plugins`

## 5. Telemetry

- high-frequency input ห้าม query PostgreSQL ต่อ message เพื่อ dashboard
- aggregate/batch ก่อน SignalR broadcast
- admin telemetry target สูงสุด 0.5 Hz สำหรับ summary stream
- frontend ต้อง throttle/batch UI update และใช้ Canvas map rendering

## 6. Operational Targets

- Max SignalR connections target: 500
- Max GPS ingestion target: 100/sec
- Max telemetry payload: 16 KB
- RabbitMQ processing lag target: < 3s
- Redis dispatch lock TTL: contract-specific และห้ามเกิน business timeout
- Dashboard ต้องมี Business Health, AI/OSRM latency และ System Health panels
- GPS history ต้อง query PostgreSQL history endpoint ไม่อ่าน Redis
