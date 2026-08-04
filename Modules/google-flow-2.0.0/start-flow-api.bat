@echo off
setlocal

cd /d "%~dp0"

set "FLOW_API_KEY=flow-local-key"
set "HOST=127.0.0.1"
set "API_PORT=8787"

if not exist ".venv\Scripts\python.exe" (
  echo [ERROR] .venv was not found.
  echo Please run install.bat first.
  echo.
  pause
  exit /b 1
)

echo Starting Flow OpenAI-compatible API on http://%HOST%:%API_PORT% ...
echo Stopping old Flow API processes if they exist...
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*google_flow.api*' -and $_.Name -match 'python|cmd' } | ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch {} }"

start "Flow API" cmd /k "set ""FLOW_API_KEY=%FLOW_API_KEY%"" && .\.venv\Scripts\python.exe -m google_flow.api.app --host %HOST% --port %API_PORT%"

timeout /t 3 /nobreak >nul
start "" "http://%HOST%:%API_PORT%/setup"
powershell -NoProfile -Command "try { Invoke-WebRequest -UseBasicParsing http://%HOST%:%API_PORT%/setup/open-login -Method POST -TimeoutSec 15 | Out-Null } catch {}"

echo.
echo Flow services started.
echo Base URL: http://%HOST%:%API_PORT%/v1
echo API Key : %FLOW_API_KEY%
echo Models  : http://%HOST%:%API_PORT%/v1/models
echo Health  : http://%HOST%:%API_PORT%/health
echo Setup   : http://%HOST%:%API_PORT%/setup
echo.
echo You can close this window. The Flow API window will keep running.

endlocal
