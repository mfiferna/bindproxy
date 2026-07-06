#!/usr/bin/env sh
# Builds compact, self-contained, Native AOT release packages for both
# BindProxy.Avalonia and BindProxy.Tui (win-x64). Intended to be run on
# Windows (native AOT for win-x64 requires the MSVC linker toolchain).
set -eu
cd "$(dirname "$0")"

RID=win-x64
CONFIG=Release
OUT=release
VERSION="${1:-dev}"

rm -rf "$OUT"
mkdir -p "$OUT/avalonia" "$OUT/tui"

echo "== Publishing BindProxy.Avalonia ($RID, self-contained, AOT) =="
dotnet publish src/BindProxy.Avalonia/BindProxy.Avalonia.csproj -c "$CONFIG" -r "$RID" --self-contained -p:DebugType=none -o "$OUT/avalonia"
find "$OUT/avalonia" -name '*.pdb' -delete

echo "== Publishing BindProxy.Tui ($RID, self-contained, AOT) =="
dotnet publish src/BindProxy.Tui/BindProxy.Tui.csproj -c "$CONFIG" -r "$RID" --self-contained -p:DebugType=none -o "$OUT/tui"
find "$OUT/tui" -name '*.pdb' -delete

echo "== Packaging zips =="
# Avalonia zip has a stable, version-less name so releases/latest/download/ always
# resolves to the newest build; the version still lives in the release tag/title.
(cd "$OUT/avalonia" && zip -qr "../bindproxy-$RID.zip" .)
(cd "$OUT/tui" && zip -qr "../bindproxy-tui-$VERSION-$RID.zip" .)

echo
echo "Done. Artifacts:"
ls "$OUT"/*.zip
