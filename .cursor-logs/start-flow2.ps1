$ErrorActionPreference = "Continue"

$ProjectRoot = "D:\Dev\AssetAutomator"
$PythonExe   = Join-Path $ProjectRoot "src\AssetAutomator.WinUI\bin\x64\Debug\net10.0-windows10.0.26100.0\tools\PythonEmbed\python.exe"
$RootPath    = Join-Path $ProjectRoot "src\AssetAutomator.WinUI\bin\x64\Debug\net10.0-windows10.0.26100.0\tools\PythonSource"
$LogDir      = Join-Path $ProjectRoot ".cursor-logs"
$StdoutLog   = Join-Path $LogDir "flow2.stdout.log"
$StderrLog   = Join-Path $LogDir "flow2.stderr.log"
$PidFile     = Join-Path $LogDir "flow2.pid"

if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir | Out-Null }
"" | Set-Content -Path $StdoutLog
"" | Set-Content -Path $StderrLog

$env:PYTHONIOENCODING = "utf-8"
$env:PYTHONUNBUFFERED = "1"
$env:FLOW_API_KEY     = "flow-local-key"
$env:HOST             = "127.0.0.1"
$env:API_PORT         = "8787"

# Write the python -c body to a file so we don't have to escape quotes.
$BodyFile = Join-Path $LogDir "flow2.body.py"
@"
import uvicorn
uvicorn.run('google_flow_ext.api.app:app', host='127.0.0.1', port=8787, log_level='info')
"@ | Set-Content -Path $BodyFile -Encoding UTF8

Write-Host "Starting server..."
Write-Host "  exe  : $PythonExe"
Write-Host "  cwd  : $RootPath"
Write-Host "  body : $BodyFile"

try {
    $proc = Start-Process -FilePath $PythonExe `
                          -ArgumentList @($BodyFile) `
                          -WorkingDirectory $RootPath `
                          -RedirectStandardOutput $StdoutLog `
                          -RedirectStandardError  $StderrLog `
                          -WindowStyle Hidden `
                          -PassThru
    Write-Host "Server PID: $($proc.Id)"
    Set-Content -Path $PidFile -Value $proc.Id
}
catch {
    Write-Host "Failed to start: $_" -ForegroundColor Red
    exit 1
}
