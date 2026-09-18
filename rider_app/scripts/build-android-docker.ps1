# =============================================================================
# Build Android APK via Flutter Docker Container (No local Flutter/Java required)
# =============================================================================
param(
    [string]$TunnelUrl = ""
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$appDir = Resolve-Path (Join-Path $scriptDir "..")

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Building Delivery App Release APK via Docker...  " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

$apiFlag = ""
if ($TunnelUrl) {
    $apiFlag = "--dart-define=API_BASE_URL=$TunnelUrl"
    Write-Host "Pre-configuring Server Base URL: $TunnelUrl" -ForegroundColor Yellow
} else {
    Write-Host "Server URL will be configurable inside app (Settings > Server URL)" -ForegroundColor Yellow
}

$buildCmd = "flutter pub get && flutter build apk --release --android-skip-build-dependency-validation $apiFlag"

Write-Host "`n--> Compiling Android Release APK inside Docker container..." -ForegroundColor Cyan
docker run --rm -v delivery-gradle-cache:/root/.gradle -v "${appDir}:/app" -w /app ghcr.io/cirruslabs/flutter:stable bash -c "$buildCmd"

if ($LASTEXITCODE -eq 0) {
    $apkPath = Join-Path $appDir "build\app\outputs\flutter-apk\app-release.apk"
    if (Test-Path $apkPath) {
        Write-Host "`n==================================================" -ForegroundColor Green
        Write-Host " [OK] Android Release APK Built Successfully!" -ForegroundColor Green
        Write-Host "==================================================" -ForegroundColor Green
        Write-Host "Output File: $apkPath" -ForegroundColor White

        $rootApkDir = Join-Path $appDir "..\apk"
        if (-not (Test-Path $rootApkDir)) {
            New-Item -ItemType Directory -Path $rootApkDir -Force | Out-Null
        }
        $targetApk = Join-Path $rootApkDir "rider-app.apk"
        Copy-Item $apkPath -Destination $targetApk -Force
        Write-Host " [OK] Copied APK to Server Host Directory: $targetApk" -ForegroundColor Green

        # Copy into running backend docker container if running
        $backendRunning = (docker ps --filter "name=delivery-backend" --filter "status=running" -q)
        if ($backendRunning) {
            docker exec delivery-backend mkdir -p /app/apk
            docker cp $targetApk delivery-backend:/app/apk/rider-app.apk
            Write-Host " [OK] Deployed APK into live delivery-backend container (/app/apk/rider-app.apk)" -ForegroundColor Green
        }
    }
} else {
    Write-Host "`n[ERROR] APK build failed with code $LASTEXITCODE" -ForegroundColor Red
}
