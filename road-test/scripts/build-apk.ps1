param(
    [Parameter(Mandatory=$false)]
    [string]$TunnelUrl
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Building Rider App APK for Real Road Test...     " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

if ([string]::IsNullOrWhiteSpace($TunnelUrl)) {
    $TunnelUrl = Read-Host "Enter Server Public URL (e.g. https://xxxx.trycloudflare.com)"
}

if ([string]::IsNullOrWhiteSpace($TunnelUrl)) {
    Write-Host "`n[ERROR] Server Public URL is required to build the APK." -ForegroundColor Red
    Write-Host "Usage: powershell ./road-test/scripts/build-apk.ps1 -TunnelUrl 'https://xxxx.trycloudflare.com'`n" -ForegroundColor Yellow
    exit 1
}

# Trim trailing slash if present
$TunnelUrl = $TunnelUrl.TrimEnd('/')

Write-Host "`nTarget Server Base URL: $TunnelUrl" -ForegroundColor Yellow

# Auto-detect Flutter SDK path if not in current session PATH
if (-not (Get-Command flutter -ErrorAction SilentlyContinue)) {
    $KnownFlutterPaths = @(
        "C:\src\flutter\bin",
        "C:\flutter\bin",
        "E:\flutter\bin",
        "$env:LOCALAPPDATA\flutter\bin",
        "$env:USERPROFILE\flutter\bin"
    )
    foreach ($path in $KnownFlutterPaths) {
        if (Test-Path "$path\flutter.bat") {
            Write-Host "[i] Auto-detected Flutter at: $path" -ForegroundColor DarkGray
            $env:Path = "$path;$env:Path"
            break
        }
    }
}

# Ensure Java 17 (LTS) is configured for Gradle compatibility
$KnownJdkPaths = @(
    "C:\Program Files\Microsoft\jdk-17.0.20.101-hotspot",
    "C:\Program Files\Java\jdk-17",
    "C:\Program Files\Eclipse Adoptium\jdk-17*"
)
foreach ($jdk in $KnownJdkPaths) {
    $resolved = Resolve-Path $jdk -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($resolved -and (Test-Path "$($resolved.Path)\bin\java.exe")) {
        $env:JAVA_HOME = $resolved.Path
        $env:Path = "$($resolved.Path)\bin;$env:Path"
        Write-Host "[i] Using JDK 17 at: $($resolved.Path)" -ForegroundColor DarkGray
        break
    }
}

$RiderAppDir = Join-Path $PSScriptRoot "../../rider_app"
Push-Location $RiderAppDir

try {
    Write-Host "`n--> Fetching Flutter dependencies..." -ForegroundColor Cyan
    flutter pub get
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Failed to fetch Flutter packages." -ForegroundColor Red
        exit $LASTEXITCODE
    }

    Write-Host "`n--> Compiling Android Release APK..." -ForegroundColor Cyan
    flutter build apk --release --android-skip-build-dependency-validation --dart-define=API_BASE_URL=$TunnelUrl
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`n[ERROR] APK Build failed!" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    $ApkPath = (Resolve-Path (Join-Path $RiderAppDir "build/app/outputs/flutter-apk/app-release.apk")).Path
    Write-Host "`n==================================================" -ForegroundColor Green
    Write-Host " [OK] APK Build Completed Successfully!" -ForegroundColor Green
    Write-Host "==================================================" -ForegroundColor Green
    Write-Host "APK Output Path:" -ForegroundColor White
    Write-Host "   $ApkPath`n" -ForegroundColor Yellow
    Write-Host "📲 Next Steps for Real Phone Testing:" -ForegroundColor White
    Write-Host "   1. Send APK to the test phone (via USB, Drive, or LINE/Chat)" -ForegroundColor Gray
    Write-Host "   2. Install and Grant 'Allow all the time' location permission" -ForegroundColor Gray
    Write-Host "   3. Follow test cases in road-test/docs/03-gps-test.md`n" -ForegroundColor Gray
}
finally {
    Pop-Location
}
