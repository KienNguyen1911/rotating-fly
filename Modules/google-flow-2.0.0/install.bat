@echo off
setlocal

cd /d "%~dp0"
set "PYTHON_CMD="

echo.
echo ==========================================
echo   Flow Image Local API Installer
echo ==========================================
echo.

where uv >nul 2>nul
if not errorlevel 1 (
  set "USE_UV=1"
  echo [INFO] Found uv package manager! Will use it for super-fast installation.
) else (
  set "USE_UV=0"
)

where py >nul 2>nul
if not errorlevel 1 (
  set "PYTHON_CMD=py -3"
)

if not defined PYTHON_CMD (
  where python >nul 2>nul
  if not errorlevel 1 (
    set "PYTHON_CMD=python"
  )
)

if not defined PYTHON_CMD (
  echo [ERROR] Python was not found in PATH.
  echo Please install Python 3.10+ first, then run this installer again.
  pause
  exit /b 1
)

echo [1/6] Checking Python version...
call %PYTHON_CMD% --version

if not exist ".venv\\Scripts\\python.exe" (
  echo [2/6] Creating virtual environment...
  if "%USE_UV%"=="1" (
    call uv venv .venv
  ) else (
    call %PYTHON_CMD% -m venv .venv
  )
  if errorlevel 1 (
    echo [ERROR] Failed to create virtual environment.
    pause
    exit /b 1
  )
) else (
  echo [2/6] Virtual environment already exists.
)

echo [3/6] Upgrading installer/pip...
if "%USE_UV%"=="1" (
  echo [INFO] Using uv pip, skipping pip upgrade.
) else (
  .\.venv\Scripts\python.exe -m pip install --upgrade pip
  if errorlevel 1 (
    echo [ERROR] Failed to upgrade pip.
    pause
    exit /b 1
  )
)

echo [4/6] Installing Python dependencies...
if "%USE_UV%"=="1" (
  call uv pip install -r requirements.txt
) else (
  .\.venv\Scripts\python.exe -m pip install -r requirements.txt
)
if errorlevel 1 (
  echo [ERROR] Failed to install requirements.
  pause
  exit /b 1
)

echo [5/6] Installing project in editable mode...
if "%USE_UV%"=="1" (
  call uv pip install -e .
) else (
  .\.venv\Scripts\python.exe -m pip install -e .
)
if errorlevel 1 (
  echo [ERROR] Failed to install project package.
  pause
  exit /b 1
)

echo [6/6] Installing Playwright Chromium...
.\.venv\Scripts\python.exe -m playwright install chromium
if errorlevel 1 (
  echo [ERROR] Failed to install Playwright Chromium.
  pause
  exit /b 1
)

echo.
echo Installation completed successfully.
echo.
echo Next step:
echo   Double-click start-flow-api.bat
echo.
echo That will:
echo   1. Start the local API service
echo   2. Open the setup page
echo   3. Open the Flow login page
echo.
echo After the user logs in to Google Flow, the rest will complete automatically.
echo.
pause
endlocal
