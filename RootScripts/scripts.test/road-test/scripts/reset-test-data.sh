#!/bin/bash
set -e

echo "=================================================="
echo " Resetting Road Test Telemetry & Cache Data...    "
echo "=================================================="

# Check confirmation
read -p "Are you sure you want to flush Redis rider locations and test GPS history? (y/N) " confirm
if [[ "$confirm" != "y" && "$confirm" != "Y" ]]; then
    echo "Aborted."
    exit 0
fi

echo "Flushing Redis rider location & telemetry keys..."
docker exec delivery-redis redis-cli -a "${REDIS_PASSWORD:-password}" DEL riders:locations || true
docker exec delivery-redis redis-cli -a "${REDIS_PASSWORD:-password}" --scan --pattern "riders:gps:*" | xargs -r docker exec delivery-redis redis-cli -a "${REDIS_PASSWORD:-password}" DEL || true
docker exec delivery-redis redis-cli -a "${REDIS_PASSWORD:-password}" --scan --pattern "riders:heartbeat:*" | xargs -r docker exec delivery-redis redis-cli -a "${REDIS_PASSWORD:-password}" DEL || true
docker exec delivery-redis redis-cli -a "${REDIS_PASSWORD:-password}" --scan --pattern "riders:speed_buffer:*" | xargs -r docker exec delivery-redis redis-cli -a "${REDIS_PASSWORD:-password}" DEL || true
docker exec delivery-redis redis-cli -a "${REDIS_PASSWORD:-password}" --scan --pattern "riders:status:*" | xargs -r docker exec delivery-redis redis-cli -a "${REDIS_PASSWORD:-password}" DEL || true

echo "Data reset complete."

