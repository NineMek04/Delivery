#!/bin/bash
set -e

echo "=================================================="
echo " Stopping Road Test Docker Server Environment...  "
echo "=================================================="

docker compose -f docker-compose.yml -f road-test/docker/docker-compose.test.yml down

echo "Services stopped."
