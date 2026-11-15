$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Root 'runtimes/win-x64/native'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Source = Join-Path $PSScriptRoot 'phonestek_native.c'
$Library = Join-Path $Out 'phonestek_native.dll'
# Run from an x64 Native Tools Command Prompt for Visual Studio (MSVC).
& cl /nologo /O2 /LD $Source /link "/OUT:$Library"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Built $Library"
