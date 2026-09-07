#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../../.." && pwd)"
cd "$REPO_ROOT"

echo "=================================================="
echo " Stopping Road Test Docker Server Environment...  "
echo "=================================================="

docker compose -f docker-compose.yml -f RootScripts/scripts.test/road-test/docker/docker-compose.test.yml down

echo "Services stopped."
