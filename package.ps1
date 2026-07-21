# Script đóng gói ứng dụng AssetAutomator v1.1

$ErrorActionPreference = "Stop"

# 1. Định nghĩa các đường dẫn
$projectRoot = Get-Location
$publishDir = Join-Path $projectRoot "bin\Release\net10.0-windows\win-x64\publish"
$packageOutDir = Join-Path $projectRoot "dist_package"
$zipFile = Join-Path $projectRoot "AssetAutomator_v1.1.zip"

Write-Host "=== BẮT ĐẦU ĐÓNG GÓI ỨNG DỤNG v1.1 ===" -ForegroundColor Cyan

# 2. Xóa các thư mục build cũ nếu có
if (Test-Path $packageOutDir) {
    Write-Host "Đang dọn dẹp thư mục package cũ..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $packageOutDir
}
if (Test-Path $zipFile) {
    Remove-Item -Force $zipFile
}

# 3. Chạy dotnet publish
Write-Host "Đang build và publish ứng dụng..." -ForegroundColor Green
dotnet publish AssetAutomator.csproj -c Release -r win-x64 --self-contained true

# 4. Tạo thư mục package mới
New-Item -ItemType Directory -Path $packageOutDir | Out-Null

# 5. Sao chép bản build WPF (.NET) sang thư mục package (bao gồm cả thư mục ẩn .playwright)
Write-Host "Đang sao chép ứng dụng WPF..." -ForegroundColor Green
Get-ChildItem -Path $publishDir -Force | Copy-Item -Destination $packageOutDir -Recurse -Force

# 6. Tạo file nén zip
Write-Host "Đang tạo file nén zip..." -ForegroundColor Green
Compress-Archive -Path "$packageOutDir\*" -DestinationPath $zipFile -Force

Write-Host "=== ĐÓNG GÓI HOÀN TẤT ===" -ForegroundColor Cyan
Write-Host "File đóng gói tại: $zipFile" -ForegroundColor Cyan
