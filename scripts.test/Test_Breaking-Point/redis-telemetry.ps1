Write-Host "Fetching Redis Health Metrics..."

$info = docker exec delivery-redis redis-cli info

$used_memory = ($info | Select-String "used_memory_human:").ToString().Split(":")[1].Trim()
$connected_clients = ($info | Select-String "connected_clients:").ToString().Split(":")[1].Trim()
$ops_per_sec = ($info | Select-String "instantaneous_ops_per_sec:").ToString().Split(":")[1].Trim()
$keyspace_hits = ($info | Select-String "keyspace_hits:").ToString().Split(":")[1].Trim()
$keyspace_misses = ($info | Select-String "keyspace_misses:").ToString().Split(":")[1].Trim()
$mem_frag_ratio = ($info | Select-String "mem_fragmentation_ratio:").ToString().Split(":")[1].Trim()

Write-Host "--------------------------------"
Write-Host "Redis Health Metrics"
Write-Host "--------------------------------"
Write-Host "Used Memory          : $used_memory"
Write-Host "Connected Clients    : $connected_clients"
Write-Host "Ops / Sec            : $ops_per_sec"
Write-Host "Keyspace Hits        : $keyspace_hits"
Write-Host "Keyspace Misses      : $keyspace_misses"
Write-Host "Fragmentation Ratio  : $mem_frag_ratio"

$frag_value = [double]$mem_frag_ratio
if ($frag_value -gt 3.0) {
    Write-Host "WARNING: Fragmentation Ratio is CRITICAL (>3.0)" -ForegroundColor Red
} elseif ($frag_value -gt 2.0) {
    Write-Host "WARNING: Fragmentation Ratio is WARNING (>2.0)" -ForegroundColor Yellow
} else {
    Write-Host "Fragmentation Ratio is GOOD (<1.5)" -ForegroundColor Green
}
