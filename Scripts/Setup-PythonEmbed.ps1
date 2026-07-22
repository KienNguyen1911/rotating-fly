# Setup-PythonEmbed.ps1
# Tự động tải Python 3.11 Embedded Portable và cài đặt các thư viện cần thiết (youtube-transcript-api)
# Dành cho Developer / Build script trước khi đóng gói ứng dụng phát hành.

$ErrorActionPreference = "Stop"

$ProjectRoot = Resolve-Path "$PSScriptRoot\.."
$EmbedDir = Join-Path $ProjectRoot "PythonEmbed"
$ZipPath = Join-Path $ProjectRoot "python-embed.zip"
$PythonUrl = "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip"
$GetPipUrl = "https://bootstrap.pypa.io/get-pip.py"
$GetPipPath = Join-Path $ProjectRoot "get-pip.py"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   AssetAutomator - Python Embedded Setup Script" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

Write-Host "[1/5] Kiểm tra thư mục PythonEmbed..." -ForegroundColor Yellow
if (-not (Test-Path $EmbedDir)) {
    New-Item -ItemType Directory -Path $EmbedDir | Out-Null
}

Write-Host "[2/5] Tải gói Python 3.11 Embedded từ python.org..." -ForegroundColor Yellow
Invoke-WebRequest -Uri $PythonUrl -OutFile $ZipPath

Write-Host "[3/5] Giải nén Python Embedded vào $EmbedDir..." -ForegroundColor Yellow
Expand-Archive -Path $ZipPath -DestinationPath $EmbedDir -Force
Remove-Item -Path $ZipPath -Force

# Mở khóa site-packages trong python311._pth
$PthFile = Join-Path $EmbedDir "python311._pth"
if (Test-Path $PthFile) {
    Write-Host "[4/5] Kích hoạt site-packages trong python311._pth..." -ForegroundColor Yellow
    $content = Get-Content $PthFile
    $content = $content -replace '#import site', 'import site'
    Set-Content -Path $PthFile -Value $content
}

Write-Host "[5/5] Cài đặt pip và youtube-transcript-api..." -ForegroundColor Yellow
Invoke-WebRequest -Uri $GetPipUrl -OutFile $GetPipPath
& "$EmbedDir\python.exe" $GetPipPath --no-warn-script-location
if (Test-Path $GetPipPath) { Remove-Item -Path $GetPipPath -Force }

& "$EmbedDir\python.exe" -m pip install youtube-transcript-api --no-warn-script-location

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " [HOÀN THÀNH] Môi trường Python Portable đã sẵn sàng tại:" -ForegroundColor Green
Write-Host " $EmbedDir" -ForegroundColor Green
Write-Host " Khách hàng chỉ cần giải nén file Zip của App là dùng luôn!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
