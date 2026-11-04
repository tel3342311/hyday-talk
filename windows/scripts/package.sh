#!/bin/bash
# Publishes a self-contained Hyday Talk for x64 Windows from Linux or macOS, with the recognition
# engines in runtime\, and zips it as windows/dist/Hyday-Talk-<version>-win-x64.zip.
# Run build-runtime-mingw.sh first (or pass --skip-runtime to package without engines).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/.build/publish"
rm -rf "$OUT"
dotnet publish "$ROOT/src/HydayTalk/HydayTalk.csproj" -c Release -r win-x64 --self-contained true -o "$OUT"
if [[ "${1:-}" != "--skip-runtime" ]]; then
    for engine in whisper-cli.exe transcribe-cli.exe; do
        [[ -f "$ROOT/.build/runtime/$engine" ]] || { printf '找不到 %s，請先執行 build-runtime-mingw.sh。\n' "$engine" >&2; exit 1; }
    done
    mkdir -p "$OUT/runtime" && cp "$ROOT"/.build/runtime/*.exe "$OUT/runtime/"
fi
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/HydayTalk/HydayTalk.csproj")"
mkdir -p "$ROOT/dist"
ZIP="$ROOT/dist/Hyday-Talk-$VERSION-win-x64.zip"
rm -f "$ZIP"
(cd "$OUT" && zip -qr "$ZIP" .)
printf '已建立 %s\n' "$ZIP"
