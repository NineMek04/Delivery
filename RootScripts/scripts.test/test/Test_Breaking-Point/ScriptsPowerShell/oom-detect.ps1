Write-Host "Checking for OOMKilled containers..."

$containers = docker ps -a -q
$oomCount = 0

foreach ($container in $containers) {
    $inspect = docker inspect $container | ConvertFrom-Json
    $name = $inspect[0].Name.TrimStart("/")
    $oomKilled = $inspect[0].State.OOMKilled
    
    if ($oomKilled -eq $true) {
        Write-Host "WARNING: Container $name was OOMKilled!" -ForegroundColor Red
        $oomCount++
    }
}

if ($oomCount -eq 0) {
    Write-Host "No OOMKilled containers found. System is stable." -ForegroundColor Green
} else {
    Write-Host "Found $oomCount OOMKilled containers." -ForegroundColor Yellow
}
