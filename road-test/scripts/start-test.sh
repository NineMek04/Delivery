#!/bin/bash
set -e

echo "=================================================="
echo " Starting Road Test Docker Server Environment... "
echo "=================================================="

# Check if .env exists
if [ ! -f ".env" ]; then
    echo "[!] .env file not found. Copying from road-test/config/.env.test.example..."
    cp road-test/config/.env.test.example .env
    echo "[!] Please verify passwords in .env before running in a public environment."
fi

# Run docker compose with test override
docker compose -f docker-compose.yml -f road-test/docker/docker-compose.test.yml up -d

echo ""
echo "Services started successfully!"
echo "Run 'bash road-test/scripts/health-check.sh' to verify service status."
