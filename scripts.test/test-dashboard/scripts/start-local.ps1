param(
  [string]$WorkspaceRoot = (Resolve-Path "$PSScriptRoot\..\..\..").Path
)

$backendPath = Join-Path $WorkspaceRoot "scripts.test\test-dashboard\backend"
$frontendPath = Join-Path $WorkspaceRoot "scripts.test\test-dashboard\frontend"

Write-Host "Checking if Redis is running for Pub/Sub & BullMQ..."
$redisRunning = docker ps -q -f name=test-dashboard-redis
if (!$redisRunning) {
    $redisStopped = docker ps -aq -f name=test-dashboard-redis
    if ($redisStopped) {
        Write-Host "Starting existing Redis container..."
        docker start test-dashboard-redis
    } else {
        Write-Host "Creating and starting new Redis container on port 6379..."
        docker run -d --name test-dashboard-redis -p 6379:6379 redis:alpine
    }
} else {
    Write-Host "Redis is already running."
}

Write-Host "Starting Test Dashboard local processes..."
Write-Host "Workspace: $WorkspaceRoot"
Write-Host "API:       http://localhost:3001"
Write-Host "Frontend:  http://localhost:4200"

Start-Process powershell -WindowStyle Normal -WorkingDirectory $backendPath -ArgumentList "-NoExit", "-Command", "`$env:HOST_WORKSPACE_PATH='$WorkspaceRoot'; npm.cmd run dev:api"
Start-Process powershell -WindowStyle Normal -WorkingDirectory $backendPath -ArgumentList "-NoExit", "-Command", "`$env:HOST_WORKSPACE_PATH='$WorkspaceRoot'; npm.cmd run dev:worker"
Start-Process powershell -WindowStyle Normal -WorkingDirectory $frontendPath -ArgumentList "-NoExit", "-Command", "npm.cmd start -- --host 0.0.0.0"
