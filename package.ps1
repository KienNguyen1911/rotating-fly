# Script đóng gói ứng dụng AutoCreateImage

$ErrorActionPreference = "Stop"

# 1. Định nghĩa các đường dẫn
$projectRoot = Get-Location
$publishDir = Join-Path $projectRoot "bin\Release\net10.0-windows\win-x64\publish"
$pythonDistDir = Join-Path $projectRoot "Chatgpt2Api\dist\Chatgpt2Server"
$packageOutDir = Join-Path $projectRoot "dist_package"
$zipFile = Join-Path $projectRoot "AutoCreateImage_v1.0.0.zip"

Write-Host "=== BẮT ĐẦU ĐÓNG GÓI ỨNG DỤNG ===" -ForegroundColor Cyan

# 2. Xóa các thư mục build cũ nếu có
if (Test-Path $packageOutDir) {
    Write-Host "Đang dọn dẹp thư mục package cũ..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $packageOutDir
}
if (Test-Path $zipFile) {
    Remove-Item -Force $zipFile
}

# 3. Tạo thư mục package mới
New-Item -ItemType Directory -Path $packageOutDir | Out-Null

# 4. Sao chép bản build WPF (.NET) sang thư mục package
Write-Host "Đang sao chép ứng dụng WPF..." -ForegroundColor Green
Copy-Item -Path "$publishDir\*" -Destination $packageOutDir -Recurse -Force

# 5. Sao chép bản build Python API Server
Write-Host "Đang sao chép API Server..." -ForegroundColor Green
$targetPythonDir = Join-Path $packageOutDir "Chatgpt2Server"
Copy-Item -Path $pythonDistDir -Destination $targetPythonDir -Recurse -Force

# 6. Tạo file nén zip
Write-Host "Đang tạo file nén zip..." -ForegroundColor Green
Compress-Archive -Path "$packageOutDir\*" -DestinationPath $zipFile -Force

Write-Host "=== ĐÓNG GÓI HOÀN TẤT ===" -ForegroundColor Cyan
Write-Host "File đóng gói tại: $zipFile" -ForegroundColor Cyan
