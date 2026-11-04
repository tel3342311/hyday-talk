#!/bin/bash
# Publishes a self-contained Hyday Talk for x64 Windows from Linux or macOS and zips it as
# windows/dist/Hyday-Talk-<version>-win-x64.zip. The engines in windows/runtime are copied in by
# the project file; rebuild them with build-runtime-mingw.sh when their sources change.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/.build/publish"
for engine in whisper-cli.exe transcribe/transcribe-cli.exe; do
    [[ -f "$ROOT/runtime/$engine" ]] || { printf '找不到 runtime/%s，請先執行 build-runtime-mingw.sh。\n' "$engine" >&2; exit 1; }
done
rm -rf "$OUT"
dotnet publish "$ROOT/src/HydayTalk/HydayTalk.csproj" -c Release -r win-x64 --self-contained true -o "$OUT"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/HydayTalk/HydayTalk.csproj")"
mkdir -p "$ROOT/dist"
ZIP="$ROOT/dist/Hyday-Talk-$VERSION-win-x64.zip"
rm -f "$ZIP"
(cd "$OUT" && zip -qr "$ZIP" .)
printf '已建立 %s\n' "$ZIP"
