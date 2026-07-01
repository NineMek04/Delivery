# Runtime Modes

## DEMO MODE
- Fake riders enabled
- Relaxed rate limits
- Synthetic telemetry allowed

## STAGING MODE
- Real JWT auth
- Real Redis locks
- Real RabbitMQ retry policy

## FAILURE SIMULATION MODE
- Random route optimizer timeout injection
- RabbitMQ delayed ACK
- GPS jitter enabled
