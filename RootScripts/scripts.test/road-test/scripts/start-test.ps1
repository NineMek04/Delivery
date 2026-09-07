$RepoRoot = (Resolve-Path "$PSScriptRoot/../../../..").Path
Push-Location $RepoRoot
try {
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host " Starting Road Test Docker Server Environment... " -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan

    if (-not (Test-Path ".env")) {
        Write-Host "[!] .env file not found. Copying from RootScripts/scripts.test/road-test/config/.env.test.example..." -ForegroundColor Yellow
        Copy-Item "RootScripts/scripts.test/road-test/config/.env.test.example" ".env"
        Write-Host "[!] Please verify passwords in .env before running in a public environment." -ForegroundColor Yellow
    }

    docker compose -f docker-compose.yml -f RootScripts/scripts.test/road-test/docker/docker-compose.test.yml up -d

    Write-Host "`nServices started successfully!" -ForegroundColor Green
    Write-Host "Run 'powershell ./RootScripts/scripts.test/road-test/scripts/health-check.ps1' to verify service status.`n" -ForegroundColor Green
}
finally {
    Pop-Location
}
