# Script đóng gói ứng dụng AssetAutomator
# Sử dụng: .\package.ps1 [-Version "1.2"]

param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

# Nhập phiên bản nếu chưa truyền vào
if (-not $Version) {
    $Version = Read-Host "Nhập phiên bản cần build (VD: 1.1)"
    if (-not $Version) {
        Write-Host "Chưa nhập phiên bản. Sẽ sử dụng mặc định: 1.0" -ForegroundColor Yellow
        $Version = "1.0"
    }
}

Write-Host "=== BẮT ĐẦU ĐÓNG GÓI ỨNG DỤNG v$Version ===" -ForegroundColor Cyan

# 1. Định nghĩa các đường dẫn
$projectRoot = Get-Location
$uiProjectPath = Join-Path $projectRoot "src\AssetAutomator.UI\AssetAutomator.UI.csproj"
$publishDir = Join-Path $projectRoot "src\AssetAutomator.UI\bin\Release\net10.0-windows\win-x64\publish"
$packageOutDir = Join-Path $projectRoot "dist_package"
$zipFile = Join-Path $projectRoot "AssetAutomator_v${Version}.zip"

# 2. Kiểm tra file csproj tồn tại
if (-not (Test-Path $uiProjectPath)) {
    Write-Host "Lỗi: Không tìm thấy file project tại: $uiProjectPath" -ForegroundColor Red
    exit 1
}

# 3. Xóa các thư mục build cũ nếu có
if (Test-Path $packageOutDir) {
    Write-Host "Đang dọn dẹp thư mục package cũ..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $packageOutDir
}
if (Test-Path $zipFile) {
    Remove-Item -Force $zipFile
}

# 4. Chạy dotnet publish
Write-Host "Đang build và publish ứng dụng..." -ForegroundColor Green
dotnet publish $uiProjectPath -c Release -r win-x64 --self-contained true -p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    Write-Host "Lỗi: Build thất bại!" -ForegroundColor Red
    exit 1
}

# 5. Tạo thư mục package mới và sao chép bản build sang (bao gồm cả thư mục ẩn .playwright)
Write-Host "Đang chuẩn bị thư mục package..." -ForegroundColor Green
New-Item -ItemType Directory -Path $packageOutDir | Out-Null
Get-ChildItem -Path $publishDir -Force | Copy-Item -Destination $packageOutDir -Recurse -Force

# 6. Dọn dẹp cache và thông tin cá nhân
Write-Host "Đang dọn dẹp các tệp cấu hình cá nhân và cache..." -ForegroundColor Yellow
$filesToRemove = @(
    "appsettings.json",
    "ai84_api_key.txt",
    "image_api_key.txt",
    "image_api_url.txt",
    "manual-proxies.txt"
)
foreach ($file in $filesToRemove) {
    $filePath = Join-Path $packageOutDir $file
    if (Test-Path $filePath) {
        Remove-Item -Force $filePath
        Write-Host "  Đã xóa: $file" -ForegroundColor DarkYellow
    }
}

# Xóa thư mục tạm TempProfile_ sinh ra trong quá trình chạy
Get-ChildItem -Path $packageOutDir -Directory -Filter "TempProfile_*" | ForEach-Object {
    Remove-Item -Recurse -Force $_.FullName
    Write-Host "  Đã xóa thư mục tạm: $($_.Name)" -ForegroundColor DarkYellow
}

# 7. Tạo file nén zip
Write-Host "Đang tạo file nén zip..." -ForegroundColor Green
Compress-Archive -Path "$packageOutDir\*" -DestinationPath $zipFile -Force

Write-Host ""
Write-Host "=== ĐÓNG GÓI HOÀN TẤT ===" -ForegroundColor Cyan
Write-Host "File đóng gói tại: $zipFile" -ForegroundColor Cyan

# 8. Cập nhật file update.xml cho AutoUpdater
Write-Host "Đang cập nhật file update.xml cho AutoUpdater..." -ForegroundColor Green
$updateXml = @"
<?xml version="1.0" encoding="UTF-8"?>
<item>
    <version>${Version}</version>
    <url>https://github.com/KienNguyen1911/AssetAutomator-Releases/releases/download/v${Version}/AssetAutomator_v${Version}.zip</url>
    <changelog>https://github.com/KienNguyen1911/AssetAutomator-Releases/releases/tag/v${Version}</changelog>
    <mandatory>false</mandatory>
</item>
"@
$updateXml | Out-File -FilePath (Join-Path $projectRoot "update.xml") -Encoding UTF8
Write-Host "Đã tạo file update.xml (nhớ commit và push file này nhé!)" -ForegroundColor Cyan
