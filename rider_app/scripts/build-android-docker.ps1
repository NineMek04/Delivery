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
docker run --rm -v "${appDir}:/app" -w /app ghcr.io/cirruslabs/flutter:stable bash -c "$buildCmd"

if ($LASTEXITCODE -eq 0) {
    $apkPath = Join-Path $appDir "build\app\outputs\flutter-apk\app-release.apk"
    if (Test-Path $apkPath) {
        Write-Host "`n==================================================" -ForegroundColor Green
        Write-Host " [OK] Android Release APK Built Successfully!" -ForegroundColor Green
        Write-Host "==================================================" -ForegroundColor Green
        Write-Host "Output File: $apkPath" -ForegroundColor White
    }
} else {
    Write-Host "`n[ERROR] APK build failed with code $LASTEXITCODE" -ForegroundColor Red
}
