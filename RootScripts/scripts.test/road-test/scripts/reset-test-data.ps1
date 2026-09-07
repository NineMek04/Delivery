Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Resetting Road Test Telemetry & Cache Data...    " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

$confirm = Read-Host "Are you sure you want to flush Redis rider locations and test GPS history? (y/N)"
if ($confirm -ne 'y' -and $confirm -ne 'Y') {
    Write-Host "Aborted." -ForegroundColor Yellow
    exit 0
}

$redisPass = if ($env:REDIS_PASSWORD) { $env:REDIS_PASSWORD } else { "password" }

Write-Host "Flushing Redis rider location & telemetry keys..." -ForegroundColor Yellow
docker exec delivery-redis redis-cli -a $redisPass DEL riders:locations

# Flush pattern keys using redis-cli eval or keys
$flushScript = "local keys = redis.call('keys', 'riders:*'); for i=1,#keys,5000 do redis.call('del', unpack(keys, i, math.min(i+4999, #keys))) end; return #keys"
docker exec delivery-redis redis-cli -a $redisPass EVAL $flushScript 0

Write-Host "Data reset complete." -ForegroundColor Green
