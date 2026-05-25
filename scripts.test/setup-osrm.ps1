# OSRM Offline Map Builder and Setup Helper v1.0
# This script automates downloading the Thailand map and compiling it for OSRM Docker container.

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host " OSRM Offline Map Compiler & Setup Helper v1.0" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""

# 1. Prepare Directory
$DataDir = Join-Path $pwd "osrm_data"
if (!(Test-Path $DataDir)) {
    Write-Host "Creating OSRM Data Directory: $DataDir ..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
}

$PbfFile = Join-Path $DataDir "udon-thani.osm.pbf"

# 2. Download raw map data from Geofabrik
if (!(Test-Path $PbfFile)) {
    Write-Host "Downloading Thailand raw map data (osm.pbf) from Geofabrik..." -ForegroundColor Cyan
    Write-Host "This download is ~180MB and may take 1-3 minutes depending on your internet connection." -ForegroundColor Gray
    
    $Uri = "https://download.geofabrik.de/asia/thailand-latest.osm.pbf"
    
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $Uri -OutFile $PbfFile -UseBasicParsing
        Write-Host "Download completed successfully!" -ForegroundColor Green
    }
    catch {
        Write-Host "Error downloading map data: $_" -ForegroundColor Red
        Exit 1
    }
} else {
    Write-Host "Raw map data detected. Skipping download..." -ForegroundColor Green
}

# 3. Compile map network via Docker OSRM Backend Toolchain
Write-Host ""
Write-Host "Starting OSRM Toolchain compilation via Docker..." -ForegroundColor Yellow

# Phase A: osrm-extract
Write-Host ""
Write-Host "[1/3] Extracting road networks (osrm-extract)..." -ForegroundColor Cyan
docker run --rm --user root -v "${DataDir}:/data" osrm/osrm-backend osrm-extract -p /usr/local/share/osrm/profiles/car.lua /data/udon-thani.osm.pbf

# Phase B: osrm-partition
Write-Host ""
Write-Host "[2/3] Partitioning street cells (osrm-partition)..." -ForegroundColor Cyan
docker run --rm --user root -v "${DataDir}:/data" osrm/osrm-backend osrm-partition /data/udon-thani.osrm

# Phase C: osrm-customize
Write-Host ""
Write-Host "[3/3] Customizing street route metrics (osrm-customize)..." -ForegroundColor Cyan
docker run --rm --user root -v "${DataDir}:/data" osrm/osrm-backend osrm-customize /data/udon-thani.osrm

Write-Host "============================================================" -ForegroundColor Green
Write-Host " OSRM OFFLINE COMPILATION COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host " Offline map files compiled inside: $DataDir" -ForegroundColor Gray
Write-Host " Next step: restart your osrm container to launch offline speed:" -ForegroundColor Yellow
Write-Host " -> docker-compose restart osrm" -ForegroundColor Cyan
