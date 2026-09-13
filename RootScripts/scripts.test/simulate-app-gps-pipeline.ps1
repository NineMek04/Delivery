<#
.SYNOPSIS
    Simulate Mobile Rider App GPS Ingestion Pipeline & Verify DB Persistence

.DESCRIPTION
    Tests the real-time SignalR GPS stream (with mobile accuracy 55m-75m) and
    REST batch ingestion (/api/v1/telemetry/gps/batch), verifying that points
    are queued to RabbitMQ and persisted to public."RiderLocationHistories".
#>

param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$RiderEmail = "sorryilostcontact@gmail.com",
    [string]$RiderPassword = "Password123!"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$simDir = Join-Path $scriptDir "test/e2e-simulator"
$jsScript = Join-Path $simDir "simulate-app-gps-pipeline.js"

if (-not (Test-Path $jsScript)) {
    Write-Error "Could not find test script at: $jsScript"
    exit 1
}

$env:BASE_URL = $BaseUrl
$env:RIDER_EMAIL = $RiderEmail
$env:RIDER_PASSWORD = $RiderPassword

Write-Host "Running GPS Pipeline Simulation against $BaseUrl..." -ForegroundColor Cyan

Push-Location $simDir
try {
    node simulate-app-gps-pipeline.js
} finally {
    Pop-Location
}
