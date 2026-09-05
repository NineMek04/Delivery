Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Running Road Test Environment Health Checks...   " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Check Docker container status
Write-Host "`n--- Docker Containers Status ---" -ForegroundColor Yellow
docker compose -f docker-compose.yml -f road-test/docker/docker-compose.test.yml ps

# 2. Check Backend Health Endpoint
Write-Host "`n--- Backend Health Check ---" -ForegroundColor Yellow
try {
    $res = Invoke-WebRequest -Uri "http://localhost:5000/health" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
    if ($res.StatusCode -eq 200) {
        Write-Host " [OK] Backend API is HEALTHY (http://localhost:5000/health)" -ForegroundColor Green
    } else {
        Write-Host " [!] Backend API returned status $($res.StatusCode)" -ForegroundColor Red
    }
} catch {
    Write-Host " [!] Backend API is NOT responding or UNHEALTHY: $_" -ForegroundColor Red
}

# 3. Check Nginx Reverse Proxy
Write-Host "`n--- Nginx Proxy Health Check ---" -ForegroundColor Yellow
try {
    $res = Invoke-WebRequest -Uri "http://localhost:80/health" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
    if ($res.StatusCode -eq 200) {
        Write-Host " [OK] Nginx Reverse Proxy is HEALTHY (http://localhost:80/health)" -ForegroundColor Green
    } else {
        Write-Host " [!] Nginx Proxy returned status $($res.StatusCode)" -ForegroundColor Red
    }
} catch {
    Write-Host " [!] Nginx Proxy is NOT responding on port 80: $_" -ForegroundColor Red
}

# 4. Check OSRM Routing Service
Write-Host "`n--- OSRM Engine Check ---" -ForegroundColor Yellow
try {
    $res = Invoke-WebRequest -Uri "http://localhost:5001/route/v1/driving/102.80,17.40;102.81,17.41?overview=false" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
    if ($res.StatusCode -eq 200) {
        Write-Host " [OK] OSRM Engine is HEALTHY (http://localhost:5001)" -ForegroundColor Green
    } else {
        Write-Host " [!] OSRM Engine returned status $($res.StatusCode)" -ForegroundColor Red
    }
} catch {
    Write-Host " [!] OSRM Engine is NOT responding on port 5001: $_" -ForegroundColor Red
}

Write-Host "`nHealth check complete.`n" -ForegroundColor Cyan
