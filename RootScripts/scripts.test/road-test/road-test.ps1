<#
.SYNOPSIS
    Road Test Master Control Center
.DESCRIPTION
    Unified management script for the Road Test environment.
    Supports both interactive menu and direct CLI commands.
.EXAMPLE
    powershell ./RootScripts/scripts.test/road-test/road-test.ps1
    powershell ./RootScripts/scripts.test/road-test/road-test.ps1 start
    powershell ./RootScripts/scripts.test/road-test/road-test.ps1 health
    powershell ./RootScripts/scripts.test/road-test/road-test.ps1 tunnel
    powershell ./RootScripts/scripts.test/road-test/road-test.ps1 build -TunnelUrl "https://xxxx.trycloudflare.com"
    powershell ./RootScripts/scripts.test/road-test/road-test.ps1 reset
    powershell ./RootScripts/scripts.test/road-test/road-test.ps1 stop
#>

param(
    [Parameter(Position=0)]
    [ValidateSet("start", "stop", "health", "tunnel", "build", "reset", "menu", "workflow")]
    [string]$Action = "menu",

    [Parameter(Mandatory=$false)]
    [string]$TunnelUrl
)

$ScriptDir = $PSScriptRoot
$ScriptsFolder = Join-Path $ScriptDir "scripts"
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "../../..")).Path

function Show-Header {
    Clear-Host
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "   🛵 ROAD TEST MASTER CONTROL CENTER                    " -ForegroundColor Cyan
    Write-Host "   Workspace: RootScripts/scripts.test/road-test/              " -ForegroundColor DarkCyan
    Write-Host "==========================================================" -ForegroundColor Cyan
}

function Run-Start {
    Write-Host "`n[Action] Starting Road Test Server..." -ForegroundColor Yellow
    & "$ScriptsFolder/start-test.ps1"
}

function Run-Health {
    Write-Host "`n[Action] Running System Health Checks..." -ForegroundColor Yellow
    & "$ScriptsFolder/health-check.ps1"
}

function Run-Tunnel {
    Write-Host "`n[Action] Starting Cloudflare Tunnel via Docker..." -ForegroundColor Yellow
    Write-Host "NOTE: Keep this window running to maintain public connectivity.`n" -ForegroundColor DarkYellow
    docker run --rm -it --network=host cloudflare/cloudflared:latest tunnel --url http://localhost:80
}

function Run-BuildApk {
    param([string]$Url)
    Write-Host "`n[Action] Building Rider App Release APK..." -ForegroundColor Yellow
    if ([string]::IsNullOrWhiteSpace($Url)) {
        & "$ScriptsFolder/build-apk.ps1"
    } else {
        & "$ScriptsFolder/build-apk.ps1" -TunnelUrl $Url
    }
}

function Run-Reset {
    Write-Host "`n[Action] Resetting Road Test Data..." -ForegroundColor Yellow
    & "$ScriptsFolder/reset-test-data.ps1"
}

function Run-Stop {
    Write-Host "`n[Action] Stopping Road Test Server..." -ForegroundColor Yellow
    & "$ScriptsFolder/stop-test.ps1"
}

function Run-Workflow {
    Show-Header
    Write-Host "`n[Workflow] Step 1/3: Starting Server Environment..." -ForegroundColor Yellow
    Run-Start

    Write-Host "`nWaiting 10 seconds for service stabilization..." -ForegroundColor DarkGray
    Start-Sleep -Seconds 10

    Write-Host "`n[Workflow] Step 2/3: Checking Services Health..." -ForegroundColor Yellow
    Run-Health

    Write-Host "`n----------------------------------------------------------" -ForegroundColor DarkCyan
    Write-Host " Next Step (Step 3/3): Start Tunnel and Build APK" -ForegroundColor Cyan
    Write-Host " 1. Run 'powershell ./RootScripts/scripts.test/road-test/road-test.ps1 tunnel' in a separate window" -ForegroundColor White
    Write-Host " 2. Copy the generated https://xxxx.trycloudflare.com URL" -ForegroundColor White
    Write-Host " 3. Run 'powershell ./RootScripts/scripts.test/road-test/road-test.ps1 build -TunnelUrl <URL>'" -ForegroundColor White
    Write-Host "----------------------------------------------------------`n" -ForegroundColor DarkCyan
}

# --- Command-line execution ---
switch ($Action.ToLower()) {
    "start"    { Run-Start; exit 0 }
    "health"   { Run-Health; exit 0 }
    "tunnel"   { Run-Tunnel; exit 0 }
    "build"    { Run-BuildApk -Url $TunnelUrl; exit 0 }
    "reset"    { Run-Reset; exit 0 }
    "stop"     { Run-Stop; exit 0 }
    "workflow" { Run-Workflow; exit 0 }
}

# --- Interactive Menu ---
do {
    Show-Header
    Write-Host " [1] Start Server Environment          (start-test)" -ForegroundColor White
    Write-Host " [2] Run Health Checks                 (health-check)" -ForegroundColor White
    Write-Host " [3] Open Cloudflare Tunnel via Docker (tunnel)" -ForegroundColor White
    Write-Host " [4] Build Release APK                 (build-apk)" -ForegroundColor White
    Write-Host " [5] Reset Test Telemetry & Cache      (reset-test-data)" -ForegroundColor White
    Write-Host " [6] Stop Server Environment           (stop-test)" -ForegroundColor White
    Write-Host " [7] Guided Quick-Start Workflow       (start -> health -> guide)" -ForegroundColor Green
    Write-Host " [0] Exit" -ForegroundColor Red
    Write-Host "==========================================================" -ForegroundColor Cyan
    $choice = Read-Host " Select an option (0-7)"

    switch ($choice) {
        "1" { Run-Start; Read-Host "`nPress Enter to continue..." }
        "2" { Run-Health; Read-Host "`nPress Enter to continue..." }
        "3" { Run-Tunnel; Read-Host "`nPress Enter to continue..." }
        "4" { Run-BuildApk; Read-Host "`nPress Enter to continue..." }
        "5" { Run-Reset; Read-Host "`nPress Enter to continue..." }
        "6" { Run-Stop; Read-Host "`nPress Enter to continue..." }
        "7" { Run-Workflow; Read-Host "`nPress Enter to continue..." }
        "0" { Write-Host "`nExiting Road Test Control Center.`n" -ForegroundColor Gray; break }
        default { Write-Host "`nInvalid option, please try again." -ForegroundColor Red; Start-Sleep -Seconds 1 }
    }
} while ($choice -ne "0")
