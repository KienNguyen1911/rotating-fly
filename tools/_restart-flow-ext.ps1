Add-Type -AssemblyName System.Management
$target = 'D:\Dev\AssetAutomator\Modules\google-flow-2.0.0\start-flow-ext.py'
$procs = Get-CimInstance Win32_Process -Filter "Name = 'python.exe'"
foreach ($p in $procs) {
    if ($p.CommandLine -like "*$target*") {
        Write-Host ("Killing PID {0}" -f $p.ProcessId)
        Stop-Process -Id $p.ProcessId -Force
    }
}
Write-Host 'Done.'