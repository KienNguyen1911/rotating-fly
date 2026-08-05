@echo off
setlocal

cd /d "%~dp0\\.."

set "PKG_NAME=flow-image-cli-local-api-v1.0.0"
set "RELEASE_ROOT=%CD%\\release-package"
set "DIST_ROOT=%RELEASE_ROOT%\\dist"
set "TARGET_DIR=%DIST_ROOT%\\%PKG_NAME%"
set "ZIP_FILE=%DIST_ROOT%\\%PKG_NAME%.zip"

echo.
echo ==========================================
echo   Build Release Package
echo ==========================================
echo.

if exist "%TARGET_DIR%" (
  echo Removing old package directory...
  rmdir /s /q "%TARGET_DIR%"
)

if exist "%ZIP_FILE%" (
  echo Removing old zip package...
  del /f /q "%ZIP_FILE%"
)

if not exist "%DIST_ROOT%" (
  mkdir "%DIST_ROOT%"
)

echo Creating package directory...
mkdir "%TARGET_DIR%"

echo Copying core files...
xcopy /e /i /y "google_flow" "%TARGET_DIR%\\google_flow" >nul
copy /y "install.bat" "%TARGET_DIR%\\" >nul
copy /y "start-flow-api.bat" "%TARGET_DIR%\\" >nul
copy /y "API_USAGE.md" "%TARGET_DIR%\\" >nul
copy /y "README.md" "%TARGET_DIR%\\" >nul
copy /y "README-ar.md" "%TARGET_DIR%\\" >nul
copy /y "requirements.txt" "%TARGET_DIR%\\" >nul
copy /y "pyproject.toml" "%TARGET_DIR%\\" >nul
copy /y "config.toml" "%TARGET_DIR%\\" >nul
copy /y "interactive_generate.py" "%TARGET_DIR%\\" >nul
copy /y "LICENSE" "%TARGET_DIR%\\" >nul

echo Cleaning Python cache files...
for /d /r "%TARGET_DIR%" %%d in (__pycache__) do @if exist "%%d" rmdir /s /q "%%d"
del /s /q "%TARGET_DIR%\\*.pyc" >nul 2>nul

echo Creating zip package...
powershell -NoProfile -Command "Compress-Archive -Path '%TARGET_DIR%\\*' -DestinationPath '%ZIP_FILE%' -Force"
if errorlevel 1 (
  echo [ERROR] Failed to create zip package.
  exit /b 1
)

echo.
echo Release package created successfully.
echo Folder: %TARGET_DIR%
echo Zip   : %ZIP_FILE%
echo.
pause
endlocal
