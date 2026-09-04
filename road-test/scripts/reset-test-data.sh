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

echo "Flushing Redis rider location keys..."
docker exec -it delivery-redis redis-cli -a "${REDIS_PASSWORD:-password}" KEYS "rider:*:location" | xargs -r docker exec -it delivery-redis redis-cli -a "${REDIS_PASSWORD:-password}" DEL

echo "Data reset complete."
