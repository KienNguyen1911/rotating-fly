Add-Type -AssemblyName System.Net.Http

$url = 'http://127.0.0.1:8787/v1/projects'
$body = '{"title":"repro-project-error"}'
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromMinutes(2)

$req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, $url)
$req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "flow-local-key")
$req.Content = New-Object System.Net.Http.StringContent($body, [System.Text.Encoding]::UTF8, "application/json")

Write-Host "POST $url"
Write-Host "body: $body"
try {
    $resp = $client.SendAsync($req).GetAwaiter().GetResult()
    $text = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Write-Host ("HTTP " + [int]$resp.StatusCode)
    Write-Host "headers:"
    foreach ($h in $resp.Headers) {
        Write-Host ("  " + $h.Key + ": " + ($h.Value -join ", "))
    }
    Write-Host "body:"
    Write-Host $text
}
catch {
    Write-Host ("EXCEPTION: " + $_.Exception.Message)
    Write-Host ("FULL: " + $_.Exception.ToString())
}
$client.Dispose()