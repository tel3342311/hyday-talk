# Publishes a self-contained Hyday Talk for x64 Windows, with the recognition engines in runtime\,
# and zips it as windows\dist\Hyday-Talk-<version>-win-x64.zip.
param([switch]$SkipRuntime)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if (-not $SkipRuntime) { & (Join-Path $PSScriptRoot 'build-runtime.ps1') }
$Out = Join-Path $Root '.build\publish'
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
dotnet publish (Join-Path $Root 'src\HydayTalk\HydayTalk.csproj') -c Release -r win-x64 --self-contained true -o $Out
if ($LASTEXITCODE -ne 0) { throw '發佈失敗。' }
$Runtime = Join-Path $Root '.build\runtime'
if (Test-Path $Runtime) { Copy-Item $Runtime (Join-Path $Out 'runtime') -Recurse -Force }
[xml]$Project = Get-Content (Join-Path $Root 'src\HydayTalk\HydayTalk.csproj')
$Version = $Project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$Dist = Join-Path $Root 'dist'
New-Item -ItemType Directory -Force -Path $Dist | Out-Null
$Zip = Join-Path $Dist "Hyday-Talk-$Version-win-x64.zip"
if (Test-Path $Zip) { Remove-Item $Zip }
Compress-Archive -Path (Join-Path $Out '*') -DestinationPath $Zip
Write-Host "已建立 $Zip"
