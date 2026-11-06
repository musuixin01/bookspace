param()
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$demo = Join-Path $root "__bookspace_demo.html"
$output = Join-Path $root "assets\bookspace-interaction.gif"
$chrome = "C:\Program Files\Google\Chrome\Application\chrome.exe"
$ffmpeg = (Get-Command ffmpeg.exe -ErrorAction Stop).Source
$node = (Get-Command node.exe -ErrorAction Stop).Source
$capturer = Join-Path $PSScriptRoot "Record-ReadmeGif.cjs"
if (!(Test-Path $demo) -or !(Test-Path $chrome)) { throw "Existing demo or Chrome missing" }
$temp = Join-Path $env:TEMP ("BookspaceGif_" + [Guid]::NewGuid().ToString("N"))
$profile = Join-Path $temp "profile"
$frames = Join-Path $temp "frames"
New-Item -ItemType Directory -Path $frames -Force | Out-Null
$process = $null
try {
    $process = Start-Process -FilePath $chrome -ArgumentList @(
        "--headless=new", "--remote-debugging-port=0", "--no-first-run",
        "--disable-gpu", "--user-data-dir=$profile", "about:blank"
    ) -WindowStyle Hidden -PassThru
    $portFile = Join-Path $profile "DevToolsActivePort"
    $ready = $false
    for ($i=0; $i -lt 100; ++$i) {
        if (Test-Path $portFile) {$ready = $true; break}
        Start-Sleep -Milliseconds 100
    }
    if (!$ready) { throw "Chrome debugging endpoint did not start" }
    $port = (Get-Content -LiteralPath $portFile -TotalCount 1).Trim()
    & $node $capturer $port ([Uri]::new($demo)).AbsoluteUri $frames
    if ($LASTEXITCODE -ne 0) { throw "Browser capture failed" }

    $count = @(Get-ChildItem -LiteralPath $frames -Filter "*.png").Count
    if ($count -lt 30) { throw "Not enough screenshot frames ($count)" }
    Write-Output ("CAPTURED_FRAMES=" + $count)
    $inputPattern = Join-Path $frames "%03d.png"
    $palette = Join-Path $temp "palette.png"
    & $ffmpeg -v error -y -framerate 4 -i $inputPattern -vf "scale=960:-1:flags=lanczos,palettegen=stats_mode=diff:max_colors=192" $palette
    if ($LASTEXITCODE -ne 0) { throw "GIF palette generation failed" }
    & $ffmpeg -v error -y -framerate 4 -i $inputPattern -i $palette -lavfi "scale=960:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=3" -loop 0 $output
    if ($LASTEXITCODE -ne 0) { throw "GIF encoding failed" }
    $size = (Get-Item -LiteralPath $output).Length
    if ($size -lt 30000 -or $size -gt 12MB) {throw "Unexpected GIF size ($size)"}
    Write-Output ("GIF_FILE=" + $output)
    Write-Output ("GIF_BYTES=" + $size)
} finally {
    if ($process -and !$process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}