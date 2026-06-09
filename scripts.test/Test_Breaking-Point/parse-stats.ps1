param(
    [string]$File = "stage1_signalr_v3.csv"
)

$data = Import-Csv $File

function Analyze-Container($name) {
    $cData = $data | Where-Object { $_.Container -eq $name }
    if ($cData.Count -eq 0) {
        Write-Host "No data for $name"
        return
    }

    $cpus = $cData | ForEach-Object { [double]($_.("CPU %") -replace '%', '') }
    $mems = $cData | ForEach-Object {
        $raw = $_.("Mem Usage")
        $val = [double]($raw -replace '[A-Za-z]', '')
        if ($raw -like "*GiB*") {
            $val = $val * 1024
        }
        $val
    }
    $pids = $cData | ForEach-Object { [int]$_.PIDs }

    $cpuMeas = $cpus | Measure-Object -Average -Maximum
    $memMeas = $mems | Measure-Object -Average -Maximum
    $pidMeas = $pids | Measure-Object -Average -Maximum

    Write-Host "=== Container: $name ==="
    Write-Host ("CPU - Max: {0:N2}%, Avg: {1:N2}%" -f $cpuMeas.Maximum, $cpuMeas.Average)
    Write-Host ("Mem - Max: {0:N2} MiB, Avg: {1:N2} MiB" -f $memMeas.Maximum, $memMeas.Average)
    Write-Host ("PIDs - Max: {0}, Avg: {1:N1}" -f $pidMeas.Maximum, $pidMeas.Average)
    Write-Host ""
}

Analyze-Container "delivery-backend"
Analyze-Container "delivery-rabbitmq"
Analyze-Container "delivery-db"
Analyze-Container "delivery-redis"
Analyze-Container "delivery-ai"
Analyze-Container "delivery-nginx"
