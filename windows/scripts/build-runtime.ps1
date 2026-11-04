# Builds the local recognition engines for Windows from the same pinned sources the macOS app uses:
# whisper.cpp (whisper-cli.exe) and transcribe.cpp (transcribe-cli.exe for Qwen3-ASR / TEA-ASR).
# Requires Git, CMake and the Visual Studio C++ build tools. Output: windows/.build/runtime
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Work = Join-Path $Root '.build'
$Runtime = Join-Path $Work 'runtime'
New-Item -ItemType Directory -Force -Path $Runtime | Out-Null

function Build-Engine($Name, $Url, $Commit, $Target, $Extra) {
    $Source = Join-Path $Work "$Name-source"
    $Build = Join-Path $Work "$Name-build"
    if (-not (Test-Path (Join-Path $Source '.git'))) {
        git clone $Url $Source
        git -C $Source checkout --quiet $Commit
    }
    if ((git -C $Source rev-parse HEAD) -ne $Commit) { throw "$Name 原始碼版本不符，請刪除 $Source 後重試。" }
    # Static, CPU-portable build with the C runtime linked in, so the exe runs on any x64 Windows.
    cmake -S $Source -B $Build -A x64 -DBUILD_SHARED_LIBS=OFF -DGGML_BACKEND_DL=OFF -DGGML_NATIVE=OFF -DGGML_OPENMP=OFF `
        -DCMAKE_POLICY_DEFAULT_CMP0091=NEW -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded @Extra
    if ($LASTEXITCODE -ne 0) { throw "$Name 設定失敗。" }
    cmake --build $Build --config Release --target $Target --parallel
    if ($LASTEXITCODE -ne 0) { throw "$Name 建置失敗。" }
    $Exe = Get-ChildItem -Path $Build -Recurse -Filter "$Target.exe" | Select-Object -First 1
    if (-not $Exe) { throw "找不到 $Target.exe。" }
    Copy-Item $Exe.FullName (Join-Path $Runtime "$Target.exe") -Force
}

Build-Engine 'whisper' 'https://github.com/ggml-org/whisper.cpp.git' '306c88f4d1286aec1bf96e544632897886af5501' 'whisper-cli' `
    @('-DWHISPER_BUILD_TESTS=OFF', '-DWHISPER_BUILD_SERVER=OFF', '-DWHISPER_CURL=OFF')
Build-Engine 'transcribe' 'https://github.com/handy-computer/transcribe.cpp.git' 'f1dcd15858c4bbba87ca97862b4e448b2cf15b8f' 'transcribe-cli' @()
Write-Host "辨識引擎已建置到 $Runtime"
