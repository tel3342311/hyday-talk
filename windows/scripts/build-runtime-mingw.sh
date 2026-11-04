#!/bin/bash
# Cross-builds the Windows recognition engines on Linux with mingw-w64 (posix threads), from the
# same pinned sources as the macOS app: whisper-cli.exe and transcribe-cli.exe, statically linked.
# Needs git, cmake and g++-mingw-w64-x86-64-posix. Output: windows/.build/runtime
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$ROOT/.build"
RUNTIME="$WORK/runtime"
mkdir -p "$RUNTIME"
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
    local name="$1" url="$2" commit="$3" target="$4"; shift 4
    local source="$WORK/$name-source" build="$WORK/$name-mingw"
    if [[ ! -d "$source/.git" ]]; then
        git clone "$url" "$source"
        git -C "$source" checkout --quiet "$commit"
    fi
    if [[ "$(git -C "$source" rev-parse HEAD)" != "$commit" ]]; then
        printf '%s 原始碼版本不符，請刪除 %s 後重試。\n' "$name" "$source" >&2; exit 1
    fi
    # Older mingw headers lack the Windows 8 thread power-throttling API that ggml uses when targeting 0x0602+.
    cmake -S "$source" -B "$build" -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN" -DCMAKE_BUILD_TYPE=Release \
        -DBUILD_SHARED_LIBS=OFF -DGGML_BACKEND_DL=OFF -DGGML_NATIVE=OFF -DGGML_OPENMP=OFF \
        -DCMAKE_C_FLAGS="-D_WIN32_WINNT=0x0601" -DCMAKE_CXX_FLAGS="-D_WIN32_WINNT=0x0601" \
        -DCMAKE_EXE_LINKER_FLAGS="-static -static-libgcc -static-libstdc++" "$@"
    cmake --build "$build" --target "$target" --parallel "$(nproc)"
    cp "$(find "$build" -name "$target.exe" -type f | head -1)" "$RUNTIME/$target.exe"
}

build whisper https://github.com/ggml-org/whisper.cpp.git 306c88f4d1286aec1bf96e544632897886af5501 whisper-cli \
    -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_SERVER=OFF -DWHISPER_CURL=OFF
build transcribe https://github.com/handy-computer/transcribe.cpp.git f1dcd15858c4bbba87ca97862b4e448b2cf15b8f transcribe-cli
printf '辨識引擎已建置到 %s\n' "$RUNTIME"
