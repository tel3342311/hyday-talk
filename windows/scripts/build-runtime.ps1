# Builds the Windows recognition engines with Visual Studio into windows\runtime, from the same
# pinned sources as the macOS app (the Linux equivalent is build-runtime-mingw.sh):
#   runtime\whisper-cli.exe               whisper.cpp, with its ggml DLLs
#   runtime\transcribe\transcribe-cli.exe transcribe.cpp (Qwen3-ASR), with its own ggml DLLs
# ggml gets one CPU library per instruction-set level and picks the best at runtime; the C runtime is
# linked statically, so no Visual C++ Redistributable is needed. Requires Git, CMake and the
# Visual Studio C++ build tools.
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Work = Join-Path $Root '.build'
$Runtime = Join-Path $Root 'runtime'

function Build-Engine($Name, $Url, $Commit, $Target, $Output, $Extra) {
    $Source = Join-Path $Work "$Name-source"
    $Build = Join-Path $Work "$Name-msvc"
    if (-not (Test-Path (Join-Path $Source '.git'))) {
        git clone $Url $Source
        git -C $Source checkout --quiet $Commit
    }
    if ((git -C $Source rev-parse HEAD) -ne $Commit) { throw "$Name 原始碼版本不符，請刪除 $Source 後重試。" }
    cmake -S $Source -B $Build -A x64 -DGGML_CPU_ALL_VARIANTS=ON -DGGML_NATIVE=OFF -DGGML_OPENMP=OFF `
        -DCMAKE_POLICY_DEFAULT_CMP0091=NEW -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded @Extra
    if ($LASTEXITCODE -ne 0) { throw "$Name 設定失敗。" }
    cmake --build $Build --config Release --target $Target --parallel
    if ($LASTEXITCODE -ne 0) { throw "$Name 建置失敗。" }
    $Bin = Split-Path -Parent (Get-ChildItem -Path $Build -Recurse -Filter "$Target.exe" | Select-Object -First 1).FullName
    if (Test-Path $Output) { Get-ChildItem $Output -File | Remove-Item -Force }
    New-Item -ItemType Directory -Force -Path $Output | Out-Null
    Copy-Item (Join-Path $Bin "$Target.exe") $Output -Force
    Get-ChildItem $Bin -Filter *.dll | Copy-Item -Destination $Output -Force
}

Build-Engine 'whisper' 'https://github.com/ggml-org/whisper.cpp.git' '306c88f4d1286aec1bf96e544632897886af5501' 'whisper-cli' $Runtime `
    @('-DBUILD_SHARED_LIBS=ON', '-DGGML_BACKEND_DL=ON', '-DWHISPER_BUILD_TESTS=OFF', '-DWHISPER_BUILD_SERVER=OFF', '-DWHISPER_CURL=OFF')
Build-Engine 'transcribe' 'https://github.com/handy-computer/transcribe.cpp.git' 'f1dcd15858c4bbba87ca97862b4e448b2cf15b8f' 'transcribe-cli' `
    (Join-Path $Runtime 'transcribe') @('-DTRANSCRIBE_BUILD_SHARED=ON', '-DTRANSCRIBE_GGML_BACKEND_DL=ON', '-DGGML_VULKAN=OFF')
Write-Host "辨識引擎已建置到 $Runtime"
