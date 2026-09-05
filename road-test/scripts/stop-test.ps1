Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Stopping Road Test Docker Server Environment...  " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

docker compose -f docker-compose.yml -f road-test/docker/docker-compose.test.yml down

Write-Host "Services stopped." -ForegroundColor Green
