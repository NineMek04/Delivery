# Security Scan Script for Delivery Routing System
$ErrorActionPreference = "Stop"
$HasVulnerabilities = $false

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "1. Scanning .NET Backend Dependencies..." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
$dotnetDir = Join-Path $PSScriptRoot "../BackendApi"
if (Test-Path $dotnetDir) {
    Push-Location $dotnetDir
    try {
        $dotnetOut = dotnet list BackendApi.csproj package --include-transitive --vulnerable
        Write-Host $dotnetOut

        if ($dotnetOut -match "High" -or $dotnetOut -match "Critical") {
            Write-Host "[-] Dotnet vulnerability check failed: High or Critical severity vulnerabilities detected!" -ForegroundColor Red
            $HasVulnerabilities = $true
        } else {
            Write-Host "[+] Dotnet vulnerability check passed." -ForegroundColor Green
        }

        Write-Host ""
        Write-Host "Checking for deprecated .NET packages..." -ForegroundColor Cyan
        $deprecatedOut = dotnet list BackendApi.csproj package --deprecated
        $deprecatedStr = $deprecatedOut -join "`n"
        Write-Host $deprecatedStr
        if ($deprecatedStr -match "has the following deprecated packages") {
            Write-Host "[-] Dotnet deprecated package check failed: Deprecated packages detected!" -ForegroundColor Red
            $HasVulnerabilities = $true
        } else {
            Write-Host "[+] No deprecated .NET packages found." -ForegroundColor Green
        }
    }
    catch {
        Write-Host "[-] Failed to run dotnet scan command: $_" -ForegroundColor Red
        $HasVulnerabilities = $true
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "BackendApi directory not found. Skipping." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "2. Scanning Angular Frontend Dependencies..." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
$angularDir = Join-Path $PSScriptRoot "../admin-dashboard"
if (Test-Path $angularDir) {
    Push-Location $angularDir
    try {
        npm audit --audit-level=high
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            Write-Host "[-] Npm audit failed with exit code $exitCode. High/Critical vulnerabilities detected!" -ForegroundColor Red
            $HasVulnerabilities = $true
        } else {
            Write-Host "[+] Npm audit passed." -ForegroundColor Green
        }

        Write-Host ""
        Write-Host "Checking for outdated npm packages (Informational)..." -ForegroundColor Cyan
        npm outdated
        # npm outdated exits with 1 when packages are outdated. We do not fail the build for this, just display it.
    }
    catch {
        Write-Host "[-] Failed to run npm audit command: $_" -ForegroundColor Red
        $HasVulnerabilities = $true
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "admin-dashboard directory not found. Skipping." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "3. Scanning Python Route Optimizer Dependencies..." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
$aiDir = Join-Path $PSScriptRoot "../../route-optimizer"
if (Test-Path $aiDir) {
    Push-Location $aiDir
    try {
        if (Get-Command pip-audit -ErrorAction SilentlyContinue) {
            pip-audit -r requirements.txt --strict
            $exitCode = $LASTEXITCODE
            if ($exitCode -ne 0) {
                Write-Host "[-] Pip-audit failed with exit code $exitCode. Vulnerabilities detected!" -ForegroundColor Red
                $HasVulnerabilities = $true
            } else {
                Write-Host "[+] Pip-audit passed." -ForegroundColor Green
            }
        } else {
            Write-Host "[!] pip-audit command not found. Please install it with 'pip install pip-audit'. Skipping check." -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "[-] Failed to run pip-audit command: $_" -ForegroundColor Red
        $HasVulnerabilities = $true
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "route-optimizer directory not found. Skipping." -ForegroundColor Yellow
}

Write-Host ""
if ($HasVulnerabilities) {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "Security scan failed. High/Critical vulnerabilities found." -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    exit 1
} else {
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Security scan completed successfully. No High/Critical vulnerabilities found." -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    exit 0
}
