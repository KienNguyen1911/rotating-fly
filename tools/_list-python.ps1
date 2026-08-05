Add-Type -AssemblyName System.Management
$procs = Get-CimInstance Win32_Process -Filter "Name = 'python.exe'"
foreach ($p in $procs) {
    Write-Host ("PID {0}  Parent {1}  Started {2}" -f $p.ProcessId, $p.ParentProcessId, $p.CreationDate)
    Write-Host ("  CmdLine: " + $p.CommandLine)
    Write-Host ""
}