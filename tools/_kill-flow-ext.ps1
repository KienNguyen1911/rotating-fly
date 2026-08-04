Add-Type -AssemblyName System.Management
$procs = Get-CimInstance Win32_Process -Filter "Name = 'python.exe'"
foreach ($p in $procs) {
    if ($p.CommandLine -like "*start-flow-ext.py*") {
        Write-Host ('Killing PID ' + $p.ProcessId)
        Stop-Process -Id $p.ProcessId -Force
    }
}
Start-Sleep -Seconds 2
Write-Host 'Killed.'