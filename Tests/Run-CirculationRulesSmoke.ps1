param()
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$rules = Join-Path $root "图书馆理模块\Services\CirculationRules.cs"
$advancedRules = Join-Path $root "图书馆理模块\Services\CirculationAdvancedRules.cs"
$access = Join-Path $root "图书馆理模块\Services\LocalAccess.cs"
$tests = Join-Path $PSScriptRoot "CirculationRulesSmoke.cs"
$advancedTests = Join-Path $PSScriptRoot "CirculationAdvancedRulesSmoke.cs"
$compiler = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (!(Test-Path $compiler)) { throw ".NET Framework C# compiler not found" }
$temp = Join-Path $env:TEMP ("BookspaceRules_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $exe = Join-Path $temp "CirculationRulesSmoke.exe"
    & $compiler /nologo /codepage:65001 /r:System.Web.dll "/out:$exe" $rules $advancedRules $access $tests $advancedTests
    if ($LASTEXITCODE -ne 0) { throw "C# compilation failed: $LASTEXITCODE" }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Business rules tests failed: $LASTEXITCODE" }
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}