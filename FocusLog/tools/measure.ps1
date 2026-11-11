param(
    [int]$Minutes = 30,
    [string]$Process = 'FocusLog',
    [int]$IntervalSec = 10,
    [string]$Out = 'focuslog-measure.csv'
)
# Samples CPU %, memory, disk writes and battery discharge rate of FocusLog.
$p = Get-Process -Name $Process -ErrorAction Stop | Select-Object -First 1
$cores = [Environment]::ProcessorCount
$end = (Get-Date).AddMinutes($Minutes)
$rows = New-Object System.Collections.Generic.List[object]
$prevCpu = $p.TotalProcessorTime
$prevTime = Get-Date
while ((Get-Date) -lt $end) {
    Start-Sleep -Seconds $IntervalSec
    try {
        $p.Refresh()
        $now = Get-Date
        $cpu = $p.TotalProcessorTime
        $cpuPct = (($cpu - $prevCpu).TotalSeconds / (($now - $prevTime).TotalSeconds * $cores)) * 100
        $prevCpu = $cpu; $prevTime = $now
        $io = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" -ErrorAction SilentlyContinue
        $discharge = $null
        try { $discharge = (Get-CimInstance -Namespace root\wmi -ClassName BatteryStatus -ErrorAction Stop).DischargeRate } catch {}
        $rows.Add([pscustomobject]@{
            Time          = $now.ToString('s')
            CpuPercent    = [math]::Round($cpuPct, 3)
            WorkingSetMB  = [math]::Round($p.WorkingSet64 / 1MB, 1)
            PrivateMB     = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
            WriteBytes    = $io.WriteTransferCount
            DischargeMW   = $discharge
        })
    } catch { Write-Warning $_; break }
}
$rows | Export-Csv -NoTypeInformation -Path $Out
$avg = ($rows | Measure-Object CpuPercent -Average).Average
$first = $rows | Select-Object -First 1; $last = $rows | Select-Object -Last 1
Write-Host ("Samples: {0}  Avg CPU: {1:N3}%  Writes: {2:N0} bytes in {3} min" -f $rows.Count, $avg, ($last.WriteBytes - $first.WriteBytes), $Minutes)
Write-Host "Saved $Out"
