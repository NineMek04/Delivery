#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../../.." && pwd)"
cd "$REPO_ROOT"

echo "=================================================="
echo " Starting Road Test Docker Server Environment... "
echo "=================================================="

# Check if .env exists
if [ ! -f ".env" ]; then
    echo "[!] .env file not found. Copying from RootScripts/scripts.test/road-test/config/.env.test.example..."
    cp RootScripts/scripts.test/road-test/config/.env.test.example .env
    echo "[!] Please verify passwords in .env before running in a public environment."
fi

# Run docker compose with test override
docker compose -f docker-compose.yml -f RootScripts/scripts.test/road-test/docker/docker-compose.test.yml up -d

echo ""
echo "Services started successfully!"
echo "Run 'bash RootScripts/scripts.test/road-test/scripts/health-check.sh' to verify service status."
