param(
  [string]$WorkspaceRoot = (Resolve-Path "$PSScriptRoot\..\..\..").Path
)

$backendPath = Join-Path $WorkspaceRoot "scripts.test\test-dashboard\backend"
$frontendPath = Join-Path $WorkspaceRoot "scripts.test\test-dashboard\frontend"

Write-Host "Starting Test Dashboard local processes..."
Write-Host "Workspace: $WorkspaceRoot"
Write-Host "API:       http://localhost:3001"
Write-Host "Frontend:  http://localhost:4200"

Start-Process powershell -WindowStyle Normal -WorkingDirectory $backendPath -ArgumentList "-NoExit", "-Command", "`$env:HOST_WORKSPACE_PATH='$WorkspaceRoot'; npm.cmd run dev:api"
Start-Process powershell -WindowStyle Normal -WorkingDirectory $backendPath -ArgumentList "-NoExit", "-Command", "`$env:HOST_WORKSPACE_PATH='$WorkspaceRoot'; npm.cmd run dev:worker"
Start-Process powershell -WindowStyle Normal -WorkingDirectory $frontendPath -ArgumentList "-NoExit", "-Command", "npm.cmd start -- --host 0.0.0.0"
