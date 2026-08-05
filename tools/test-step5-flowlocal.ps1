#requires -Version 5.1
<#
.SYNOPSIS
    Smoke test for Step 5 (Batch Image Generation) of the AssetAutomator pipeline.

.DESCRIPTION
    Drives the local Python Flow server (http://127.0.0.1:8787) directly using the same
    JSON payload structure that AssetAutomator.Application.Services.Providers.FlowLocalImageGenProvider
    sends. Useful to prove the image-gen path works end-to-end without launching the WinUI app.

.PARAMETER OutputDir
    Folder containing scenes.json (default: D:\Assets\Gemini\tall-poppy-syndrome).

.PARAMETER Limit
    Generate only the first N scenes. Default 0 = all scenes.

.EXAMPLE
    .\test-step5-flowlocal.ps1
    .\test-step5-flowlocal.ps1 -Limit 3
#>

[CmdletBinding()]
param(
    [string]$OutputDir = "D:\Assets\Gemini\tall-poppy-syndrome",
    [int]$MaxConcurrent = 3,
    [int]$Limit = 0,
    [string]$ApiKey = "flow-local-key",
    [string]$BaseUrl = "http://127.0.0.1:8787/v1",
    [string]$Model = "nano-banana-2"
)

$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"

function Write-Step([string]$T) {
    Write-Host ""
    Write-Host ("=" * 78) -ForegroundColor Cyan
    Write-Host $T -ForegroundColor Cyan
    Write-Host ("=" * 78) -ForegroundColor Cyan
}

function Write-Info([string]$T)  { Write-Host "[INFO] $T" -ForegroundColor Gray }
function Write-OK([string]$T)    { Write-Host "[OK]   $T" -ForegroundColor Green }
function Write-Warn([string]$T)  { Write-Host "[WARN] $T" -ForegroundColor Yellow }
function Write-Err([string]$T)   { Write-Host "[ERR]  $T" -ForegroundColor Red }

# ── 0. Pre-flight: confirm Flow server is reachable ──────────────────────────
Write-Step "0. Pre-flight"
Write-Info "OutputDir = $OutputDir"
Write-Info "BaseUrl   = $BaseUrl"

try {
    $health = Invoke-RestMethod -Uri "http://127.0.0.1:8787/health" -TimeoutSec 5 -ErrorAction Stop
    Write-OK "Flow Local /health -> 200 ($($health | ConvertTo-Json -Compress))"
}
catch {
    Write-Err "Flow Local /health unreachable: $($_.Exception.Message)"
    Write-Err "Make sure the python server is running on port 8787."
    exit 1
}

# ── 1. Locate scenes.json ────────────────────────────────────────────────────
$scenesPath = Join-Path $OutputDir "scenes.json"
if (-not (Test-Path $scenesPath)) {
    Write-Err "scenes.json not found at $scenesPath"
    exit 2
}

Write-Info "Loading scenes.json..."
$root = Get-Content -Raw $scenesPath | ConvertFrom-Json
$allScenes = $root.scenes
if (-not $allScenes -or $allScenes.Count -eq 0) {
    Write-Err "scenes.json contains 0 scenes."
    exit 3
}

if ($Limit -gt 0) {
    $scenes = @($allScenes | Select-Object -First $Limit)
    Write-Warn "Loaded $($allScenes.Count) scenes, will process first $Limit."
} else {
    $scenes = @($allScenes)
    Write-Info "Loaded $($scenes.Count) scenes."
}

$imgDir = Join-Path $OutputDir "img"
if (-not (Test-Path $imgDir)) {
    New-Item -ItemType Directory -Path $imgDir -Force | Out-Null
    Write-Info "Created $imgDir."
}

# ── 2. Generate scene images (parallel via Start-ThreadJob) ─────────────────
Write-Step "2. Generating images (concurrency = $MaxConcurrent)"

# Workers can't share scope, so we serialise scene meta into a JSON file per job.
$jobs = @()
foreach ($scene in $scenes) {
    $tmpJobMeta = [System.IO.Path]::GetTempFileName() + ".json"
    @{ baseUrl = $BaseUrl; apiKey = $ApiKey; model = $Model; imgDir = $imgDir; scene = $scene } `
        | ConvertTo-Json -Depth 10 | Set-Content -Path $tmpJobMeta -Encoding UTF8

    $scriptBlock = {
        param([string]$MetaPath)
        $meta = Get-Content -Raw $MetaPath | ConvertFrom-Json
        $baseUrl = $meta.baseUrl
        $apiKey  = $meta.apiKey
        $model   = $meta.model
        $imgDir  = $meta.imgDir
        $scene   = $meta.scene

        $sceneId = if ($scene.id) { $scene.id } else { "scene_{0:D3}" -f [int]$scene.scene }
        $payload = @{
            model          = $model
            prompt         = $scene.image_prompt
            size           = "16:9"
            quality        = "standard"
            response_format = "url"
        } | ConvertTo-Json -Depth 5

        try {
            $resp = Invoke-RestMethod -Uri "$baseUrl/images/generations" `
                -Method POST -Body $payload -ContentType "application/json" `
                -Headers @{ Authorization = "Bearer $apiKey" } -TimeoutSec 600
            $first = $resp.data[0]
            $url   = $first.url
            $out   = Join-Path $imgDir "$sceneId.png"
            Invoke-WebRequest -Uri $url -OutFile $out -TimeoutSec 120 | Out-Null
            [pscustomobject]@{ scene = $scene.scene; id = $sceneId; status = "OK"; path = $out }
        }
        catch {
            [pscustomobject]@{ scene = $scene.scene; id = $sceneId; status = "FAIL"; error = $_.Exception.Message }
        }
        finally {
            Remove-Item $MetaPath -ErrorAction SilentlyContinue
        }
    }

    $jobs += Start-Job -ScriptBlock $scriptBlock -ArgumentList $tmpJobMeta
}

Write-Info "Waiting for $($jobs.Count) jobs..."
$results = @()
foreach ($j in $jobs) {
    $r = $j | Wait-Job | Receive-Job
    $results += $r
    Remove-Job $j -Force
}

# ── 3. Summarise ─────────────────────────────────────────────────────────────
Write-Step "3. Summary"

$ordered = $results | Sort-Object { [int]$_.scene }
$okList   = @($ordered | Where-Object { $_.status -eq "OK" })
$failList = @($ordered | Where-Object { $_.status -ne "OK" })

foreach ($r in $ordered) {
    if ($r.status -eq "OK") {
        $sz = (Get-Item $r.path).Length
        $line = "{0,3} {1,-12} OK    {2}  ({3:N0} B)" -f $r.scene, $r.id, $r.path, $sz
        Write-OK   $line
    } else {
        $line = "{0,3} {1,-12} FAIL  {2}" -f $r.scene, $r.id, $r.error
        Write-Err  $line
    }
}

Write-Host ""
Write-Info ("Total : {0}" -f $ordered.Count)
Write-OK   ("OK    : {0}" -f $okList.Count)
if ($failList.Count -eq 0) {
    Write-OK   ("FAIL  : {0}" -f $failList.Count)
} else {
    Write-Err  ("FAIL  : {0}" -f $failList.Count)
}

$pngs = @(Get-ChildItem -Path $imgDir -Filter "*.png" -ErrorAction SilentlyContinue)
if ($pngs.Count -gt 0) {
    Write-OK   ("img/ PNG files now on disk: {0}" -f $pngs.Count)
} else {
    Write-Err  ("img/ PNG files now on disk: {0}" -f $pngs.Count)
}

if ($failList.Count -gt 0) { exit 4 } else { exit 0 }