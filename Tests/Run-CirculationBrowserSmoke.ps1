param()
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$chrome = "C:\Program Files\Google\Chrome\Application\chrome.exe"
if (!(Test-Path $chrome)) { throw "Chrome not found; no installer is invoked" }
if (!(Get-Command node -ErrorAction SilentlyContinue)) { throw "Node is not available; no installer is invoked" }

$demo = Join-Path $root "__circulation_demo.html"
$desktop = Join-Path $root "assets\circulation-v2-desktop.png"
$mobile = Join-Path $root "assets\circulation-v2-mobile.png"
$script = Join-Path $PSScriptRoot "CirculationBrowserSmoke.cjs"
if (!(Test-Path $demo) -or !(Test-Path $script)) { throw "Required demo or test source missing" }
$profile = Join-Path $env:TEMP ("BookspaceBrowserQA_" + [Guid]::NewGuid().ToString("N"))
$process = $null
try {
    $process = Start-Process -FilePath $chrome -ArgumentList @(
        "--headless=new", "--remote-debugging-port=0",
        "--no-first-run", "--disable-gpu", "--user-data-dir=$profile",
        "about:blank"
    ) -WindowStyle Hidden -PassThru
    $endpoint = Join-Path $profile "DevToolsActivePort"
    $ready = $false
    for ($n=0; $n -lt 100; ++$n) {
        if (Test-Path $endpoint) { $ready = $true; break }
        Start-Sleep -Milliseconds 100
    }
    if (!$ready) { throw "Chrome DevTools did not start" }
    $port = (Get-Content -LiteralPath $endpoint -TotalCount 1).Trim()
    $url = ([Uri]::new($demo)).AbsoluteUri
    & node $script $port $url $desktop $mobile
    if ($LASTEXITCODE -ne 0) { throw "Browser regression failed: $LASTEXITCODE" }
} finally {
    if ($process -and !$process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $profile) {
        Remove-Item -LiteralPath $profile -Recurse -Force -ErrorAction SilentlyContinue
    }
}