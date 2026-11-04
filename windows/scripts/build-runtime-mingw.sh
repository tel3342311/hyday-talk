#!/bin/bash
# Cross-builds the Windows recognition engines on Linux with mingw-w64 (posix threads) into
# windows/runtime, from the same pinned sources as the macOS app:
#   runtime/whisper-cli.exe              whisper.cpp, with its ggml DLLs
#   runtime/transcribe/transcribe-cli.exe transcribe.cpp (Qwen3-ASR), with its own ggml DLLs
# ggml is built with one CPU library per instruction-set level (ggml-cpu-*.dll) and picks the
# best one at runtime, so the engines are fast on new CPUs and still run on old ones.
# Needs git, cmake and g++-mingw-w64-x86-64-posix.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$ROOT/.build"
RUNTIME="$ROOT/runtime"
GCC_DLLS="$(dirname "$(x86_64-w64-mingw32-g++-posix -print-libgcc-file-name)")"
mkdir -p "$WORK"
TOOLCHAIN="$WORK/mingw.cmake"
cat > "$TOOLCHAIN" <<'CMAKE'
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)
set(CMAKE_C_COMPILER x86_64-w64-mingw32-gcc-posix)
set(CMAKE_CXX_COMPILER x86_64-w64-mingw32-g++-posix)
set(CMAKE_RC_COMPILER x86_64-w64-mingw32-windres)
set(CMAKE_FIND_ROOT_PATH /usr/x86_64-w64-mingw32)
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
CMAKE

build() {
    local name="$1" url="$2" commit="$3" target="$4" output="$5"; shift 5
    local source="$WORK/$name-source" build="$WORK/$name-dl"
    if [[ ! -d "$source/.git" ]]; then
        git clone "$url" "$source"
        git -C "$source" checkout --quiet "$commit"
    fi
    if [[ "$(git -C "$source" rev-parse HEAD)" != "$commit" ]]; then
        printf '%s 原始碼版本不符，請刪除 %s 後重試。\n' "$name" "$source" >&2; exit 1
    fi
    # Older mingw headers lack the Windows 8 thread power-throttling API that ggml uses when targeting 0x0602+.
    cmake -S "$source" -B "$build" -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN" -DCMAKE_BUILD_TYPE=Release \
        -DGGML_CPU_ALL_VARIANTS=ON -DGGML_NATIVE=OFF -DGGML_OPENMP=OFF \
        -DCMAKE_C_FLAGS="-D_WIN32_WINNT=0x0601" -DCMAKE_CXX_FLAGS="-D_WIN32_WINNT=0x0601" "$@"
    cmake --build "$build" --target "$target" --parallel "$(nproc)"
    rm -rf "$output"; mkdir -p "$output"
    cp "$build"/bin/*.exe "$build"/bin/*.dll "$output/"
    # The mingw C++ runtime the engine and its DLLs link against.
    cp "$GCC_DLLS/libstdc++-6.dll" "$GCC_DLLS/libgcc_s_seh-1.dll" /usr/x86_64-w64-mingw32/lib/libwinpthread-1.dll "$output/"
    x86_64-w64-mingw32-strip --strip-unneeded "$output"/*.exe "$output"/*.dll
}

build transcribe https://github.com/handy-computer/transcribe.cpp.git f1dcd15858c4bbba87ca97862b4e448b2cf15b8f transcribe-cli \
    "$WORK/runtime-transcribe" -DTRANSCRIBE_BUILD_SHARED=ON -DTRANSCRIBE_GGML_BACKEND_DL=ON -DGGML_VULKAN=OFF
build whisper https://github.com/ggml-org/whisper.cpp.git 306c88f4d1286aec1bf96e544632897886af5501 whisper-cli \
    "$WORK/runtime-whisper" -DBUILD_SHARED_LIBS=ON -DGGML_BACKEND_DL=ON -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_SERVER=OFF -DWHISPER_CURL=OFF

# Replace the binaries in windows/runtime, keeping its README and license notices.
mkdir -p "$RUNTIME/transcribe"
rm -f "$RUNTIME"/*.exe "$RUNTIME"/*.dll "$RUNTIME"/transcribe/*.exe "$RUNTIME"/transcribe/*.dll
cp "$WORK"/runtime-whisper/whisper-cli.exe "$WORK"/runtime-whisper/*.dll "$RUNTIME/"
cp "$WORK"/runtime-transcribe/transcribe-cli.exe "$WORK"/runtime-transcribe/*.dll "$RUNTIME/transcribe/"
printf '辨識引擎已建置到 %s\n' "$RUNTIME"
