$RepoRoot = (Resolve-Path "$PSScriptRoot/../../../..").Path
Push-Location $RepoRoot
try {
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host " Stopping Road Test Docker Server Environment...  " -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan

    docker compose -f docker-compose.yml -f RootScripts/scripts.test/road-test/docker/docker-compose.test.yml down

    Write-Host "Services stopped." -ForegroundColor Green
}
finally {
    Pop-Location
}
