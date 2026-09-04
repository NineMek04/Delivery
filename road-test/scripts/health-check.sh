#!/bin/bash

echo "=================================================="
echo " Running Road Test Environment Health Checks...   "
echo "=================================================="

# 1. Check Docker container status
echo ""
echo "--- Docker Containers Status ---"
docker compose -f docker-compose.yml -f road-test/docker/docker-compose.test.yml ps

# 2. Check Backend Health Endpoint
echo ""
echo "--- Backend Health Check ---"
if curl -s -f http://localhost:5000/health > /dev/null; then
    echo " Backend API is HEALTHY (http://localhost:5000/health)"
else
    echo "[!] Backend API is NOT responding or UNHEALTHY"
fi

# 3. Check Nginx Reverse Proxy
echo ""
echo "--- Nginx Proxy Health Check ---"
if curl -s -f http://localhost:80/health > /dev/null; then
    echo " Nginx Reverse Proxy is HEALTHY (http://localhost:80/health)"
else
    echo "[!] Nginx Proxy is NOT responding on port 80"
fi

# 4. Check OSRM Routing Service
echo ""
echo "--- OSRM Engine Check ---"
if curl -s -f "http://localhost:5001/route/v1/driving/102.80,17.40;102.81,17.41?overview=false" > /dev/null; then
    echo " OSRM Engine is HEALTHY (http://localhost:5001)"
else
    echo "[!] OSRM Engine is NOT responding on port 5001"
fi

echo ""
echo "Health check complete."
