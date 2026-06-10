$OutputFile = "docker_stats_log.txt"
"Timestamp,Container,CPU(%),MemUsage,MemLimit,Mem(%)" > $OutputFile

while ($true) {
    $Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $Stats = docker stats --no-stream --format "{{.Name}},{{.CPUPerc}},{{.MemUsage}},{{.MemPerc}}"
    foreach ($Line in $Stats) {
        $Parts = $Line -split ','
        if ($Parts.Length -eq 4) {
            $MemParts = $Parts[2] -split ' / '
            $MemUsage = $MemParts[0]
            $MemLimit = $MemParts[1]
            "$Timestamp,$($Parts[0]),$($Parts[1]),$MemUsage,$MemLimit,$($Parts[3])" >> $OutputFile
        }
    }
    Start-Sleep -Seconds 2
}
