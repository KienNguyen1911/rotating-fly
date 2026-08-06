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
$uiProjectPath = Join-Path $projectRoot "src\AssetAutomator.WinUI\AssetAutomator.WinUI.csproj"
$publishDir = Join-Path $projectRoot "src\AssetAutomator.WinUI\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish"
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

# 5b. Copy Modules/ folder từ repo root vào package
#     CRITICAL: Modules chứa Gemini-API-2.0.0/server.py và google-flow-2.0.0/ — không có thì app crash khi khởi động Python server
$modulesSrc = Join-Path $projectRoot "Modules"
if (Test-Path $modulesSrc) {
    Write-Host "Đang copy Modules/ vào package..." -ForegroundColor Green
    Copy-Item -Path $modulesSrc -Destination (Join-Path $packageOutDir "Modules") -Recurse -Force
    Write-Host "  Da copy Modules/ thanh cong." -ForegroundColor DarkGreen

    $modulePkgDir = Join-Path $packageOutDir "Modules"

    # 5c. Dọn dev artifacts khỏi Modules/ — cookies, test data KHONG release
    # cookies.json — chứa tokens, KHONG release
    foreach ($f in @(
        (Join-Path $modulePkgDir "Gemini-API-2.0.0\cookies.json"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\cookies.json")
    )) {
        if (Test-Path $f) {
            Remove-Item -Force $f
            Write-Host "  Da xoa: $((Split-Path $f -Leaf))" -ForegroundColor DarkYellow
        }
    }

    # transcript.txt, scratch/, tests/, examples/ — test data KHONG release
    foreach ($pattern in @(
        (Join-Path $modulePkgDir "Gemini-API-2.0.0\transcript.txt"),
        (Join-Path $modulePkgDir "Gemini-API-2.0.0\scratch"),
        (Join-Path $modulePkgDir "Gemini-API-2.0.0\tests"),
        (Join-Path $modulePkgDir "Gemini-API-2.0.0\test_cli.py"),
        (Join-Path $modulePkgDir "Gemini-API-2.0.0\test_client_features.py"),
        (Join-Path $modulePkgDir "Gemini-API-2.0.0\test_deep_research.py"),
        (Join-Path $modulePkgDir "Gemini-API-2.0.0\test_gem_mixin.py"),
        (Join-Path $modulePkgDir "Gemini-API-2.0.0\test_metadata_isolation.py"),
        (Join-Path $modulePkgDir "Gemini-API-2.0.0\test_save_image.py"),
        (Join-Path $modulePkgDir "Gemini-API-2.0.0\convert-cookies.py"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\scratch"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\tests"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\examples"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\test_api_fixed.py"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\test_flow_ext.py"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\update.md"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\ARCHITECTURE*.md"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\README-ar.md"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\CHANGELOG_LOCAL.txt"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\install.bat"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\start-flow*.bat"),
        (Join-Path $modulePkgDir "google-flow-2.0.0\config.toml")
    )) {
        $items = Get-ChildItem -Path $pattern -Force -ErrorAction SilentlyContinue
        foreach ($item in $items) {
            if ($item.PSIsContainer) {
                $size = (Get-ChildItem $item.FullName -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum / 1MB
                Remove-Item -Recurse -Force $item.FullName
                Write-Host ("  Da xoa folder: {0} ({1:N0} MB)" -f $item.Name, [math]::Round($size)) -ForegroundColor DarkYellow
            } else {
                Remove-Item -Force $item.FullName
                Write-Host ("  Da xoa file: {0}" -f $item.Name) -ForegroundColor DarkYellow
            }
        }
    }

    # 5d. Dọn cross-platform Playwright binaries — chi can Windows, tiet kiem ~477MB
    $playwrightDir = Join-Path $packageOutDir ".playwright\node"
    foreach ($plat in @("darwin-arm64", "darwin-x64", "linux-arm64", "linux-x64")) {
        $platPath = Join-Path $playwrightDir $plat
        if (Test-Path $platPath) {
            $size = (Get-ChildItem $platPath -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum / 1MB
            Remove-Item -Recurse -Force $platPath
            Write-Host ("  Da xoa Playwright platform: {0} ({1:N0} MB)" -f $plat, [math]::Round($size)) -ForegroundColor DarkYellow
        }
    }
} else {
    Write-Host "  WARNING: Khong tim thay thu muc Modules/ tai repo root!" -ForegroundColor Red
}

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

# 7b. Copy update.xml vào package directory và đưa vào zip luôn
#     AutoUpdater cần update.xml bên trong zip để check version mới nhất
$updateXmlSrc = Join-Path $projectRoot "update.xml"
if (Test-Path $updateXmlSrc) {
    Copy-Item -Force $updateXmlSrc $packageOutDir
    Write-Host "  Da copy update.xml vao package." -ForegroundColor DarkGreen
}
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
