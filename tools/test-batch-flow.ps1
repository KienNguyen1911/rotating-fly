#Requires -Version 5.1
<#
.SYNOPSIS
    End-to-end diagnostic test for the Flow Local batch image-gen pipeline.

.DESCRIPTION
    Calls the Python Google-Flow-Extended API running on 127.0.0.1:8787 directly,
    mimicking the exact request shapes that FlowLocalImageGenProvider.cs sends
    from the WinUI client. Uses System.Net.Http.HttpClient for max compatibility
    with Windows PowerShell 5.1.

.EXAMPLE
    .\test-batch-flow.ps1                       # run all tests
    .\test-batch-flow.ps1 -TestCase 3           # only run test #3
    .\test-batch-flow.ps1 -ServerUrl http://localhost:8787
#>

[CmdletBinding()]
param(
    [string]$ServerUrl = "http://127.0.0.1:8787",
    [string]$ApiKey    = "flow-local-key",
    [int]$TestCase     = 0,           # 0 = run all
    [string]$Prompt    = "A tiny red apple on a white plate, studio lighting, photorealistic",
    [string]$Model     = "nano-banana-2-landscape",
    [string]$Size      = "1536x1024",
    [string]$Quality   = "standard"
)

$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Net.Http

$baseUrl = $ServerUrl.TrimEnd('/')
if (-not $baseUrl.EndsWith('/v1', [System.StringComparison]::OrdinalIgnoreCase)) {
    $baseUrl += '/v1'
}

# Shared HttpClient with long timeout
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromMinutes(10)

# ANSI helpers (Windows 10+ supports VT)
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
$hostSupportsAnsi = $Host.UI.RawUI -ne $null -and $env:TERM -ne 'dumb'

function Color([string]$text, [string]$color) {
    if ($hostSupportsAnsi) { return "`e[$color`m$text`e[0m" }
    return $text
}
function Pass([string]$msg) { Write-Host (Color "  [PASS] $msg" '32') }
function Fail([string]$msg) { Write-Host (Color "  [FAIL] $msg" '31') }
function Info([string]$msg) { Write-Host (Color "  [INFO] $msg" '33') }
function Head([string]$title) {
    Write-Host ""
    Write-Host (Color ("=" * 70) '36')
    Write-Host (Color "  $title" '36')
    Write-Host (Color ("=" * 70) '36')
}

function Invoke-ApiPost {
    param(
        [string]$Path,
        [hashtable]$JsonBody
    )
    $url = "$baseUrl$Path"
    $json = $JsonBody | ConvertTo-Json -Depth 10 -Compress
    Info "POST $url"
    Info "body: $json"

    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, $url)
    $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $ApiKey)
    $req.Content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, "application/json")

    try {
        $resp = $client.SendAsync($req).GetAwaiter().GetResult()
        $bodyText = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return @{
            status = [int]$resp.StatusCode
            body   = $bodyText
        }
    }
    catch [System.Net.Http.HttpRequestException] {
        Fail ("HTTP request failed: " + $_.Exception.Message)
        return @{ status = -1; body = $_.Exception.Message }
    }
    catch [System.TimeoutException] {
        Fail ("Request timed out after 10 min")
        return @{ status = -2; body = $_.Exception.Message }
    }
    catch {
        Fail ("Unexpected error: " + $_.Exception.Message)
        return @{ status = -3; body = $_.Exception.Message }
    }
}

function Invoke-ApiGet {
    param([string]$Path)
    $url = "$baseUrl$Path"
    Info "GET $url"
    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, $url)
    $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $ApiKey)
    try {
        $resp = $client.SendAsync($req).GetAwaiter().GetResult()
        $bodyText = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return @{ status = [int]$resp.StatusCode; body = $bodyText }
    }
    catch {
        return @{ status = -1; body = $_.Exception.Message }
    }
}

function Show-Body {
    param([string]$Body, [int]$MaxLen = 600)
    $snippet = if ($Body.Length -gt $MaxLen) { $Body.Substring(0, $MaxLen) + '...(truncated)' } else { $Body }
    Write-Host ("  body: " + $snippet)
}

# =============================================================================
# Pre-flight
# =============================================================================
Head "Pre-flight: server reachability"
Info "Server URL : $ServerUrl"
Info "Base URL   : $baseUrl"
Info "API key    : $($ApiKey.Substring(0, [Math]::Min(8, $ApiKey.Length)))..."

$healthReq = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, "$ServerUrl/health")
try {
    $h = $client.SendAsync($healthReq).GetAwaiter().GetResult()
    $hb = $h.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ([int]$h.StatusCode -eq 200) {
        Pass "GET /health -> 200 OK"
        Info "  body: $hb"
    } else {
        Fail "GET /health -> $($h.StatusCode)"
        Info "  body: $hb"
        exit 1
    }
}
catch {
    Fail "Cannot reach $ServerUrl"
    Info "  error: $($_.Exception.Message)"
    Info "  Start it with: cd Modules\google-flow-2.0.0 && python start-flow-ext.py"
    exit 1
}

# =============================================================================
# Test cases
# =============================================================================
$global:LastProjectId = $null
$global:LastReferenceMediaId = $null

function Run-Test1 {
    Head "Test 1: POST /v1/projects"
    $r = Invoke-ApiPost "/projects" @{ title = "Diagnostic Batch Project" }
    Info "  HTTP $($r.status)"
    Show-Body $r.body
    if ($r.status -eq 200) {
        try {
            $obj = $r.body | ConvertFrom-Json
            $global:LastProjectId = $obj.project_id
            Pass "project_id = $($obj.project_id)"
            Pass "project_url = $($obj.project_url)"
        } catch {
            Fail "Could not parse JSON: $($_.Exception.Message)"
        }
        return $true
    } else {
        Fail "Non-200 status"
        return $false
    }
}

function Run-Test2 {
    Head "Test 2: POST /v1/images/generations (text-only, no project)"
    $body = @{
        model           = $Model
        prompt          = $Prompt
        size            = $Size
        quality         = $Quality
        response_format = "url"
    }
    $r = Invoke-ApiPost "/images/generations" $body
    Info "  HTTP $($r.status)"
    Show-Body $r.body 1200
    return ($r.status -eq 200)
}

function Run-Test3 {
    Head "Test 3: POST /v1/images/generations (with project_id)"
    if (-not $global:LastProjectId) { Info "  no project_id cached; running test 1 first"; Run-Test1 | Out-Null }
    $body = @{
        model           = $Model
        prompt          = $Prompt
        size            = $Size
        quality         = $Quality
        response_format = "url"
        project_id      = $global:LastProjectId
    }
    $r = Invoke-ApiPost "/images/generations" $body
    Info "  HTTP $($r.status)"
    Show-Body $r.body 1200
    return ($r.status -eq 200)
}

function Run-Test4 {
    Head "Test 4: POST /v1/images/generations (with reference_media_id)"
    if (-not $global:LastReferenceMediaId) {
        Info "  no cached reference_media_id (run /v1/images/edits first); skipping"
        return $true
    }
    $body = @{
        model              = $Model
        prompt             = $Prompt
        size               = $Size
        quality            = $Quality
        response_format    = "url"
        reference_media_id = $global:LastReferenceMediaId
    }
    $r = Invoke-ApiPost "/images/generations" $body
    Info "  HTTP $($r.status)"
    Show-Body $r.body 1200
    return ($r.status -eq 200)
}

function Run-Test5 {
    Head "Test 5: POST /v1/images/generations (raw model without aspect suffix)"
    $body = @{
        model           = "nano-banana-2"
        prompt          = $Prompt
        size            = $Size
        quality         = $Quality
        response_format = "url"
    }
    $r = Invoke-ApiPost "/images/generations" $body
    Info "  HTTP $($r.status)"
    Show-Body $r.body 1200
    return ($r.status -eq 200)
}

function Run-Test6 {
    Head "Test 6: POST /v1/images/generations (quality=hd)"
    $body = @{
        model           = $Model
        prompt          = $Prompt
        size            = $Size
        quality         = "hd"
        response_format = "url"
    }
    $r = Invoke-ApiPost "/images/generations" $body
    Info "  HTTP $($r.status)"
    Show-Body $r.body 1200
    return ($r.status -eq 200)
}

function Run-Test7 {
    Head "Test 7: POST /v1/images/generations (size=2k)"
    $body = @{
        model           = $Model
        prompt          = $Prompt
        size            = "2k"
        quality         = "standard"
        response_format = "url"
    }
    $r = Invoke-ApiPost "/images/generations" $body
    Info "  HTTP $($r.status)"
    Show-Body $r.body 1200
    return ($r.status -eq 200)
}

function Run-Test8 {
    Head "Test 8: POST /v1/images/generations with project_title (forces create_new_project)"
    Info "  This will trigger sdk.generator.create_new_project -> POST /v1/projects internally"
    $body = @{
        model           = $Model
        prompt          = $Prompt
        size            = $Size
        quality         = $Quality
        response_format = "url"
        project_title   = "Diagnostic Title Test 8"
    }
    $r = Invoke-ApiPost "/images/generations" $body
    Info "  HTTP $($r.status)"
    Show-Body $r.body 1500
    return ($r.status -eq 200)
}

function Run-Test9 {
    Head "Test 9: POST /v1/images/generations with NON-EXISTENT project_id (simulates WinUI fallback)"
    $bogus = "00000000-0000-0000-0000-000000000000"
    $body = @{
        model           = $Model
        prompt          = $Prompt
        size            = $Size
        quality         = $Quality
        response_format = "url"
        project_id      = $bogus
    }
    $r = Invoke-ApiPost "/images/generations" $body
    Info "  HTTP $($r.status)"
    Show-Body $r.body 1500
    return ($r.status -eq 200)
}

function Run-Test10 {
    Head "Test 10: REGRESSION -- POST /v1/projects (createNewProject should now use ST)"
    Info "  Verifies fix: create_project in google_flow_ext/client.py now uses st_token=st"
    $r = Invoke-ApiPost "/projects" @{ title = "Regression Test 10" }
    Info "  HTTP $($r.status)"
    Show-Body $r.body 1500
    return ($r.status -eq 200)
}

function Run-Test11 {
    Head "Test 11: REGRESSION -- generation with fresh project_title (full pipeline)"
    Info "  Verifies fix end-to-end: project_title -> createProject -> batchGenerateImages"
    $body = @{
        model           = $Model
        prompt          = $Prompt
        size            = $Size
        quality         = $Quality
        response_format = "url"
        project_title   = "Regression Test 11"
    }
    $r = Invoke-ApiPost "/images/generations" $body
    Info "  HTTP $($r.status)"
    Show-Body $r.body 1500
    return ($r.status -eq 200)
}

# =============================================================================
# Dispatch
# =============================================================================
$tests = @(
    @{ id = 1; name = "Create Project";                  fn = { Run-Test1 } }
    @{ id = 2; name = "Image Generation (text-only)";   fn = { Run-Test2 } }
    @{ id = 3; name = "Image Generation (with project)";fn = { Run-Test3 } }
    @{ id = 4; name = "Image Generation (with ref_media_id)"; fn = { Run-Test4 } }
    @{ id = 5; name = "Image Generation (raw model)";   fn = { Run-Test5 } }
    @{ id = 6; name = "Image Generation (quality=hd)";  fn = { Run-Test6 } }
    @{ id = 7; name = "Image Generation (size=2k)";     fn = { Run-Test7 } }
    @{ id = 8; name = "Generation with project_title (triggers createProject)"; fn = { Run-Test8 } }
    @{ id = 9; name = "Generation with bogus project_id (fallback test)";       fn = { Run-Test9 } }
    @{ id = 10; name = "REGRESSION: POST /v1/projects (uses ST fix)";            fn = { Run-Test10 } }
    @{ id = 11; name = "REGRESSION: full pipeline via project_title";           fn = { Run-Test11 } }
)

$results = @()
if ($TestCase -gt 0) {
    $match = $tests | Where-Object { $_.id -eq $TestCase }
    if (-not $match) { Write-Host "Unknown test id $TestCase" -ForegroundColor Red; exit 1 }
    $r = & $match.fn
    $results += [pscustomobject]@{ id = $TestCase; pass = [bool]$r }
} else {
    foreach ($t in $tests) {
        $ok = & $t.fn
        $results += [pscustomobject]@{ id = $t.id; pass = [bool]$ok }
    }
}

Head "Summary"
foreach ($r in $results) {
    if ($r.pass) { Pass "Case $($r.id) passed" } else { Fail "Case $($r.id) failed" }
}
Info "Also inspect the Python server terminal for the full traceback."

$client.Dispose()