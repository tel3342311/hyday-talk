# Publishes a self-contained Hyday Talk for x64 Windows and zips it as
# windows\dist\Hyday-Talk-<version>-win-x64.zip. The engines in windows\runtime are copied in by
# the project file; rebuild them with build-runtime.ps1 when their sources change.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
foreach ($Engine in @('whisper-cli.exe', 'transcribe\transcribe-cli.exe')) {
    if (-not (Test-Path (Join-Path $Root "runtime\$Engine"))) { throw "找不到 runtime\$Engine，請先執行 build-runtime.ps1。" }
}
$Out = Join-Path $Root '.build\publish'
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
dotnet publish (Join-Path $Root 'src\HydayTalk\HydayTalk.csproj') -c Release -r win-x64 --self-contained true -o $Out
if ($LASTEXITCODE -ne 0) { throw '發佈失敗。' }
[xml]$Project = Get-Content (Join-Path $Root 'src\HydayTalk\HydayTalk.csproj')
$Version = $Project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$Dist = Join-Path $Root 'dist'
New-Item -ItemType Directory -Force -Path $Dist | Out-Null
$Zip = Join-Path $Dist "Hyday-Talk-$Version-win-x64.zip"
if (Test-Path $Zip) { Remove-Item $Zip }
Compress-Archive -Path (Join-Path $Out '*') -DestinationPath $Zip
Write-Host "已建立 $Zip"
