param (
    [string]$OutputFile = "docker_stats_run.csv",
    [int]$IntervalSeconds = 5
)

$containers = "delivery-backend delivery-db delivery-redis delivery-rabbitmq delivery-route-optimizer delivery-nginx"

Write-Host "Starting Docker Resource Monitoring. Output: $OutputFile. Interval: $IntervalSeconds seconds."
Write-Host "Press Ctrl+C to stop."

# Write CSV Header
"Timestamp,Container,CPU %,Mem Usage,Mem Limit,Mem %,Net I/O,Block I/O,PIDs" | Out-File -FilePath $OutputFile -Encoding utf8

try {
    while ($true) {
        $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        $stats = docker stats --no-stream --format "{{.Name}},{{.CPUPerc}},{{.MemUsage}},{{.MemPerc}},{{.NetIO}},{{.BlockIO}},{{.PIDs}}" $containers.Split(" ")
        
        foreach ($stat in $stats) {
            # Format: delivery-backend,0.00%,10MB / 2GB,0.50%,1KB / 0B,0B / 0B,10
            # Split by comma
            $parts = $stat -split ","
            $name = $parts[0]
            $cpu = $parts[1]
            $memRaw = $parts[2] -split " / "
            $memUsage = $memRaw[0]
            $memLimit = $memRaw[1]
            $memPerc = $parts[3]
            $netIO = $parts[4]
            $blockIO = $parts[5]
            $pids = $parts[6]
            
            $csvLine = "$timestamp,$name,$cpu,$memUsage,$memLimit,$memPerc,$netIO,$blockIO,$pids"
            $csvLine | Out-File -FilePath $OutputFile -Append -Encoding utf8
        }
        
        Start-Sleep -Seconds $IntervalSeconds
    }
} catch {
    Write-Host "Monitoring stopped."
}
