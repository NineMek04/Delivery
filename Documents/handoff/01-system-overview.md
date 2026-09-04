# 01 System Overview

## Purpose

ระบบนี้เป็นแพลตฟอร์มจัดส่งอาหาร/สินค้าแบบ realtime มี 4 กลุ่มผู้ใช้หลัก:

- Customer: เลือกร้าน, สั่งสินค้า, ติดตามไรเดอร์
- StorePartner: จัดการร้าน/เมนู, รับหรือปฏิเสธออเดอร์
- Rider: รับงาน, ส่ง GPS, อัปเดตสถานะจัดส่ง
- Admin/Dispatcher: ดู dashboard, map, rider, order และ dispatch operations

## Runtime Topology

```text
Customer/Store/Rider Flutter App
        |
        | REST /api/v1, SignalR /hubs/*
        v
Nginx / Docker network
        |
        +--> BackendApi (.NET 8)
        |       +--> PostgreSQL/PostGIS source of truth
        |       +--> Redis operational cache, locks, realtime rider state
        |       +--> RabbitMQ integration events and background processing
        |       +--> Local OSRM route service
        |
        +--> Angular Admin Dashboard
        +--> Route Optimizer (FastAPI, route-optimizer service name)
        +--> Observability: Seq, Prometheus, Grafana, Alertmanager, exporters
```

## Main Data Ownership

- PostgreSQL/PostGIS is the durable source of truth.
- Redis is not a source of truth; it stores current operational state, locks, route cache, and realtime lookup data.
- RabbitMQ carries integration events. Consumers must be idempotent through `ProcessedEvents`.
- SignalR carries realtime transport only. Business state changes must go through services/REST/state machines.

## State Boundaries

- Order states live in backend state machine and must not be invented by clients.
- Rider states are `OFFLINE`, `IDLE`, `RESERVED`, `BUSY`, `STALE`.
- Admin map must display rider state, not order state, for rider markers.
- Accuracy `<= 50m` can enter core telemetry/dispatch. Accuracy `> 50m` and `<= 300m` is degraded Admin UI telemetry only. Accuracy `> 300m` is rejected.

## Critical Rules

- Do not add forbidden stack: Kafka, Kubernetes, full CQRS, Event Store, Saga Orchestrator, gRPC mesh, Redis Cluster, Elasticsearch.
- Do not remove fallback paths from route optimizer/OSRM-critical code.
- Do not move test folders outside `RootScripts/scripts.test/test/`, except Angular `*.spec.ts` beside components.
- Do not physically delete critical business history where contract says soft-delete.
