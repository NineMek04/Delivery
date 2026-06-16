$data = Import-Csv "docker_stats_log.txt"
$data | ForEach-Object { 
    $_.'CPU(%)' = [double]($_.'CPU(%)' -replace '%','')
    $_.'Mem(%)' = [double]($_.'Mem(%)' -replace '%','') 
}
$data | Group-Object Container | Select-Object Name, 
    @{Name='MaxCPU(%)'; Expression={($_.Group | Measure-Object -Property 'CPU(%)' -Maximum).Maximum}}, 
    @{Name='MaxMem(%)'; Expression={($_.Group | Measure-Object -Property 'Mem(%)' -Maximum).Maximum}} | Format-Table -AutoSize
