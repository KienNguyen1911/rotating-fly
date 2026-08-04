# Setup-PythonEmbed.ps1
# Self-contained Python embedded + Google Flow Local server bundle.
# Workflow end-user: giải nén `publish/` → chạy AssetAutomator.WinUI.exe → chạy script này 1 lần đầu
# → tải Python Embedded, cài deps, copy google_flow + google_flow_ext, đánh dấu hoàn thành.
# Sau đó WinUI tự spawn Python server mỗi lần khởi động.

[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path "$PSScriptRoot\.."),
    [string]$SourceDir = (Join-Path $ProjectRoot "..\Modules\google-flow-2.0.0"),
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$EmbedDir = Join-Path $ProjectRoot "PythonEmbed"
$SourceDestDir = Join-Path $ProjectRoot "PythonSource"
$MarkerPath = Join-Path $EmbedDir ".installed-marker"
$ZipPath = Join-Path $ProjectRoot "python-embed.zip"
$PythonUrl = "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip"
$GetPipUrl = "https://bootstrap.pypa.io/get-pip.py"
$GetPipPath = Join-Path $ProjectRoot "get-pip.py"

# Google Flow dependencies (mirror requirements.txt + extras needed by google_flow_ext).
$Deps = @(
    "curl-cffi",
    "Pillow",
    "playwright",
    "fastapi",
    "uvicorn",
    "python-multipart",
    "nodriver",
    "httpx",
    "aiosqlite",
    "apscheduler",
    "python-dotenv",
    "redis"
)

function Write-Step {
    param([string]$Text)
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Assert-Source {
    if (-not (Test-Path (Join-Path $SourceDir "google_flow"))) {
        throw "Khong tim thay google_flow/ trong $SourceDir. Ban da copy Modules/google-flow-2.0.0 chua?"
    }
    if (-not (Test-Path (Join-Path $SourceDir "google_flow_ext"))) {
        throw "Khong tim thay google_flow_ext/ trong $SourceDir."
    }
}

try {
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "   AssetAutomator - Python Embedded + Google Flow Setup" -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "EmbedDir     = $EmbedDir"
    Write-Host "PythonSource = $SourceDestDir"
    Write-Host "Source       = $SourceDir"

    Assert-Source

    if ((Test-Path $MarkerPath) -and -not $Force) {
        Write-Host ""
        Write-Host "[SKIP] Da cai dat (marker ton tai). Dung -Force de cai lai." -ForegroundColor Yellow
        Write-Host "       $MarkerPath" -ForegroundColor DarkGray
        exit 0
    }

    if (-not (Test-Path $EmbedDir)) {
        Write-Step "[1/7] Tao thu muc PythonEmbed..."
        New-Item -ItemType Directory -Path $EmbedDir | Out-Null
    } else {
        Write-Step "[1/7] PythonEmbed/ da ton tai - tiep tuc."
    }

    $PythonExe = Join-Path $EmbedDir "python.exe"
    if (-not (Test-Path $PythonExe)) {
        Write-Step "[2/7] Tai Python 3.11.9 Embedded (~10MB)..."
        if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
        Invoke-WebRequest -Uri $PythonUrl -OutFile $ZipPath -UseBasicParsing
        Write-Step "[3/7] Giai nen..."
        Expand-Archive -Path $ZipPath -DestinationPath $EmbedDir -Force
        Remove-Item $ZipPath -Force
    } else {
        Write-Step "[2-3/7] python.exe da co - bo qua tai/giai nen."
    }

    $PthFile = Join-Path $EmbedDir "python311._pth"
    if (Test-Path $PthFile) {
        Write-Step "[4/7] Kich hoat site-packages trong python311._pth..."
        $content = Get-Content $PthFile
        if ($content -notmatch '^(?<!#)import site') {
            $content = $content -replace '^#import site', 'import site'
            Set-Content -Path $PthFile -Value $content
        }
    }

    Write-Step "[5/7] Cai pip..."
    if (-not (& $PythonExe -m pip --version 2>$null)) {
        if (Test-Path $GetPipPath) { Remove-Item $GetPipPath -Force }
        Invoke-WebRequest -Uri $GetPipUrl -OutFile $GetPipPath -UseBasicParsing
        & $PythonExe $GetPipPath --no-warn-script-location
        if (Test-Path $GetPipPath) { Remove-Item $GetPipPath -Force }
    }

    Write-Step "[6/7] Cai dependencies google_flow_ext (mat ~3-5 phut)..."
    foreach ($dep in $Deps) {
        Write-Host "    - $dep" -ForegroundColor DarkGray
        & $PythonExe -m pip install --upgrade $dep --no-warn-script-location 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Lenh pip install $dep that bai."
        }
    }

    Write-Step "[7/7] Cai Playwright Chromium..."
    & $PythonExe -m playwright install chromium 2>&1 | Out-Null

    Write-Step "Mirror google_flow + google_flow_ext vao PythonSource/..."
    if (Test-Path $SourceDestDir) {
        Remove-Item $SourceDestDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $SourceDestDir | Out-Null
    Copy-Item -Path (Join-Path $SourceDir "google_flow") -Destination $SourceDestDir -Recurse -Force
    Copy-Item -Path (Join-Path $SourceDir "google_flow_ext") -Destination $SourceDestDir -Recurse -Force

    Write-Step "Dat marker hoan thanh..."
    "installed=$(Get-Date -Format 'o')`npython=3.11.9-embed-amd64`nsrc=$SourceDir" | Set-Content -Path $MarkerPath

    # CRITICAL: Install a .pth file so the embedded Python finds google_flow_ext.
    # python311._pth restricts sys.path and ignores PYTHONPATH, but .pth files
    # in Lib/site-packages are processed at startup. Relative path resolves
    # from Lib/site-packages/ → Lib/ → PythonEmbed/ → tools/ → PythonSource/.
    Write-Step "Tao _assetautomator.pth de Python embedded import duoc google_flow_ext..."
    $pthPath = Join-Path $EmbedDir "Lib\site-packages\_assetautomator.pth"
    "../../../PythonSource" | Set-Content -Path $pthPath

    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " [HOAN THANH] Python Embedded + Google Flow da san sang." -ForegroundColor Green
    Write-Host " python.exe:    $PythonExe" -ForegroundColor Green
    Write-Host " source root:   $SourceDestDir" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "Ban co the chay AssetAutomator.WinUI.exe ngay bay gio."
}
catch {
    Write-Host ""
    Write-Host "[LOI] $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
    exit 1
}
