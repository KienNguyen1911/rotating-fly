$path = 'd:\Dev\AssetAutomator\Tools\test-batch-flow.ps1'
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
$emdash  = [char]0x2014
$count   = ([regex]::Matches($content, [regex]::Escape($emdash))).Count
Write-Host ("em-dash remaining: " + $count)
if ($count -gt 0) {
    $new = $content -replace [regex]::Escape($emdash), '--'
    [System.IO.File]::WriteAllText($path, $new, [System.Text.UTF8Encoding]::new($false))
    Write-Host 'Replaced.'
}