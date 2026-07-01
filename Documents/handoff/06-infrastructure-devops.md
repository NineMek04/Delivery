# 06 Infrastructure And DevOps

## Compose Services

Current compose topology:

```text
db
pgbouncer
backend
redis
route-optimizer
frontend
rider-app
osrm
nginx-proxy
seq
prometheus
grafana
alertmanager
rabbitmq
vault
vault-bootstrap
cadvisor
node-exporter
postgres-exporter
redis-exporter
```

## Development Ports

```text
Backend:       5000 -> 80
PostgreSQL:    5432 -> 5432
PgBouncer:     6432 -> 5432
Redis:         6379 -> 6379
AI:            8009 -> 8000
Admin:         4201 -> 80
Flutter web:   8083 -> 80
OSRM:          5001 -> 5000
Seq UI:        8082 -> 80
Prometheus:    9090 -> 9090
Grafana:       3000 -> 3000
Alertmanager:  9093 -> 9093
RabbitMQ UI:  15672 -> 15672
Vault:         8200 -> 8200
```

Exporters are internal scrape targets:

```text
cadvisor:8080
node-exporter:9100
postgres-exporter:9187
redis-exporter:9121
rabbitmq:15692
backend:80/metrics
```

## Nginx And Tiles

- `nginx-proxy` is the main ingress for frontend/API/hubs.
- Rider web has same-origin `/map-tiles/` proxy.
- Tile responses should be cached on persistent `map_tile_cache` volume for about 30 days.
- Do not hammer public OpenStreetMap tiles directly from many browser sessions.

## Secrets

- Production secrets must come from Vault when `VAULT_REQUIRED=true`.
- Never commit `.env`, JWT secrets, database passwords, Vault AppRole secret IDs, or tokens.

## Observability

Grafana dashboards should cover:

- Business Health: active orders, order states, active riders
- Route optimizer/OSRM: latency, fallback count, route failure count
- System Health: RabbitMQ queue depth, Redis, Postgres, container CPU/memory, rate limiting, token refresh failures

Logs must carry correlation ID and include order/rider identifiers when available.
