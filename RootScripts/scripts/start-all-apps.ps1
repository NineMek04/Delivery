param(
  [string]$WorkspaceRoot = ""
)

# Resolve WorkspaceRoot robustly — $PSScriptRoot can be empty when run from VS Code Extension host
if (!$WorkspaceRoot) {
  if ($PSScriptRoot) {
    # Normal case: script run directly
    $WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
  } elseif ($MyInvocation.MyCommand.Path) {
    # VS Code Extension or dot-sourced: resolve from script path
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $WorkspaceRoot = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
  } else {
    # Last resort: current directory
    $WorkspaceRoot = (Resolve-Path ".").Path
  }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "[STARTING ALL SYSTEM APPLICATIONS & SERVICES LOCALLY]" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Workspace Root: $WorkspaceRoot" -ForegroundColor Gray

# === 1. Load .env Environment Variables ===
$envFile = Join-Path $WorkspaceRoot ".env"
$PostgresPassword = $env:POSTGRES_PASSWORD
$RedisPassword = $env:REDIS_PASSWORD
$JwtSecret = $env:JWT_SECRET
$RabbitmqUser = $env:RABBITMQ_USER
$RabbitmqPassword = $env:RABBITMQ_PASSWORD
$AiServiceApiKey = $env:AI_SERVICE_API_KEY
$SeedAdminPassword = $env:SEED_ADMIN_PASSWORD

if (Test-Path $envFile) {
    Write-Host "Loading environment configurations from .env..." -ForegroundColor Gray
    Get-Content $envFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and !$line.StartsWith('#')) {
            $key, $value = $line -split '=', 2
            $value = $value.Trim().Trim('"').Trim("'")
            if ($key -eq "POSTGRES_PASSWORD") { $PostgresPassword = $value }
            if ($key -eq "REDIS_PASSWORD") { $RedisPassword = $value }
            if ($key -eq "JWT_SECRET") { $JwtSecret = $value }
            if ($key -eq "RABBITMQ_USER") { $RabbitmqUser = $value }
            if ($key -eq "RABBITMQ_PASSWORD") { $RabbitmqPassword = $value }
            if ($key -eq "AI_SERVICE_API_KEY") { $AiServiceApiKey = $value }
            if ($key -eq "SEED_ADMIN_PASSWORD") { $SeedAdminPassword = $value }
            
            [System.Environment]::SetEnvironmentVariable($key, $value, [System.EnvironmentVariableTarget]::Process)
        }
    }
}

$requiredValues = @{
    POSTGRES_PASSWORD = $PostgresPassword
    REDIS_PASSWORD = $RedisPassword
    JWT_SECRET = $JwtSecret
    RABBITMQ_USER = $RabbitmqUser
    RABBITMQ_PASSWORD = $RabbitmqPassword
    AI_SERVICE_API_KEY = $AiServiceApiKey
    SEED_ADMIN_PASSWORD = $SeedAdminPassword
}
foreach ($entry in $requiredValues.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace($entry.Value)) {
        throw "Required environment variable '$($entry.Key)' is missing. Configure it in .env."
    }
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://0.0.0.0:5000"
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Database=delivery_db;Username=postgres;Password=$PostgresPassword;Maximum Pool Size=1024;"
$env:ConnectionStrings__Redis = "localhost:6379,password=$RedisPassword"
$env:AI_SERVICE_URL = "http://localhost:8000"
$env:AI_SERVICE_API_KEY = $AiServiceApiKey
$env:Routing__LocalOsrmUrl = "http://localhost:5001"
$env:MessageBroker__Host = "localhost"
$env:MessageBroker__Port = "5672"
$env:MessageBroker__Username = $RabbitmqUser
$env:MessageBroker__Password = $RabbitmqPassword
$env:Jwt__Key = $JwtSecret
$env:Jwt__Issuer = "DeliveryBackendApi"
$env:Jwt__Audience = "DeliveryClients"
$env:Authentication__RequireSecureCookie = "false"
$env:SeedAdminPassword = $SeedAdminPassword
$env:DATABASE_URL = "postgresql://postgres:$PostgresPassword@localhost:5432/delivery_db"

# === 2. Stop Conflicting Docker Containers & Start Backing Services ===
Write-Host "`n[Docker] Stopping any conflicting application containers..." -ForegroundColor Yellow
docker compose stop backend frontend rider-app ai-service

Write-Host "`n[Docker] Checking and starting backing services..." -ForegroundColor Yellow
docker compose up -d db redis rabbitmq osrm seq prometheus grafana
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Failed to start docker containers. Make sure Docker Desktop is running."
    exit 1
}
Write-Host "Docker backing services are running." -ForegroundColor Green

# === 3. Start C# .NET Backend API (Local Host Mode) ===
$backendPath = Join-Path $WorkspaceRoot "BackendApi"
Write-Host "`n[Backend] Launching C# .NET Backend API in a separate terminal..." -ForegroundColor Yellow
$backendCmd = "Write-Host '===========================================' -ForegroundColor Green; Write-Host '   [Backend] Running on localhost:5000' -ForegroundColor Green; Write-Host '===========================================' -ForegroundColor Green; dotnet run;"
Start-Process powershell -WindowStyle Normal -WorkingDirectory $backendPath -ArgumentList "-NoExit", "-Command", $backendCmd

# === 4. Start Python FastAPI AI Routing Engine (Local Host Mode) ===
$aiPath = Join-Path $WorkspaceRoot "ai-engine"
Write-Host "`n[AI Engine] Launching FastAPI AI Engine in a separate terminal..." -ForegroundColor Yellow
$aiCmd = "Write-Host '===========================================' -ForegroundColor Green; Write-Host '   [AI Engine] Running on localhost:8000' -ForegroundColor Green; Write-Host '===========================================' -ForegroundColor Green; if (Test-Path 'venv') { .\venv\Scripts\activate }; uvicorn main:app --reload --port 8000;"
Start-Process powershell -WindowStyle Normal -WorkingDirectory $aiPath -ArgumentList "-NoExit", "-Command", $aiCmd

# === 5. Start Angular Admin Dashboard (client, store, dashboard) ===
$frontendPath = Join-Path $WorkspaceRoot "admin-dashboard"
Write-Host "`n[Dashboard] Launching Angular Admin Dashboard (Client/Store/Dashboard) in a separate terminal..." -ForegroundColor Yellow
$frontendCmd = "Write-Host '===========================================' -ForegroundColor Green; Write-Host '   [Dashboard] Running on localhost:4200' -ForegroundColor Green; Write-Host '===========================================' -ForegroundColor Green; npm start -- --port 4200;"
Start-Process powershell -WindowStyle Normal -WorkingDirectory $frontendPath -ArgumentList "-NoExit", "-Command", $frontendCmd

# === 6. Start Flutter Rider App ===
$riderPath = Join-Path $WorkspaceRoot "rider_app"
$flutterInPath = $false

if (Get-Command flutter -ErrorAction SilentlyContinue) {
    $flutterInPath = $true
} else {
    # Search common Flutter SDK paths on user system
    $commonFlutterPaths = @("C:\src\flutter\bin", "D:\src\flutter\bin", "C:\tools\flutter\bin", "C:\flutter\bin")
    foreach ($path in $commonFlutterPaths) {
        if (Test-Path $path) {
            $env:PATH = "$path;" + $env:PATH
            $flutterInPath = $true
            break
        }
    }
}

if ($flutterInPath) {
    Write-Host "`n[Rider App] Launching Flutter Rider App (Web) locally in a separate terminal..." -ForegroundColor Yellow
    $riderCmd = "Write-Host '===========================================' -ForegroundColor Green; Write-Host '   [Rider App] Running locally on localhost:8080' -ForegroundColor Green; Write-Host '===========================================' -ForegroundColor Green; flutter run -d chrome --web-port 8080 --dart-define=API_BASE_URL=http://localhost:5000;"
    Start-Process powershell -WindowStyle Normal -WorkingDirectory $riderPath -ArgumentList "-NoExit", "-Command", $riderCmd
} else {
    Write-Host "`n[Rider App] Flutter SDK not detected locally. Falling back to Docker Rider App..." -ForegroundColor Yellow
    docker compose up -d --build rider-app
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to start Docker rider-app. Make sure Docker is running."
    } else {
        Write-Host "Rider App container started. Serving at http://localhost:8080" -ForegroundColor Green
    }
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "[OK] All processes launched in separate windows!" -ForegroundColor Green
Write-Host "  - .NET Backend API: http://localhost:5000" -ForegroundColor Gray
Write-Host "  - AI FastAPI Engine: http://localhost:8000" -ForegroundColor Gray
Write-Host "  - Angular Portals (Admin/Customer/Store): http://localhost:4200" -ForegroundColor Gray
if ($flutterInPath) {
    Write-Host "  - Flutter Rider Web App: http://localhost:8080" -ForegroundColor Gray
}
Write-Host "==========================================================" -ForegroundColor Green

# === 7. Automatically Open Portals in Default Browser ===
Write-Host "`nOpening web portals in your browser in 25 seconds (waiting for server startup & compilation)..." -ForegroundColor Cyan
for ($i = 25; $i -gt 0; $i--) {
    Write-Host "$i..." -NoNewline -ForegroundColor Gray
    Start-Sleep -Seconds 1
}
Write-Host "`nOpening pages..." -ForegroundColor Cyan

# Open all 4 web pages
Write-Host "Opening web portals..." -ForegroundColor Green
try {
    Start-Process "http://localhost:4200/map"             # 1. Admin Dashboard (Angular)
    Start-Process "http://localhost:8080/#/customer"      # 2. Customer Portal (Flutter)
    Start-Process "http://localhost:8080/#/store"         # 3. Store Portal (Flutter)
    Start-Process "http://localhost:8080/#/"              # 4. Rider Portal (Flutter)
} catch {
    Write-Warning "Failed to automatically open browser pages. You can manually visit them: "
    Write-Warning "  - Admin Map: http://localhost:4200/map"
    Write-Warning "  - Customer Portal: http://localhost:8080/#/customer"
    Write-Warning "  - Store Partner: http://localhost:8080/#/store"
    Write-Warning "  - Rider App: http://localhost:8080/#/"
}



