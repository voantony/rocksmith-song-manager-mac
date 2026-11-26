#!/usr/bin/env bash
# Assemble a self-contained Mac Song Manager.app. The bundle does not contain
# archive decryption code. Ad-hoc codesign runs only when the macOS codesign
# tool is present; the app is not notarized.
set -eu
RID="${1:-osx-arm64}"
ROOT="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$ROOT/Directory.Build.props" | head -n 1)"
OUT="$ROOT/artifacts/$RID"
APP="$OUT/Mac Song Manager.app"
PUB="$OUT/publish"
ZIP="$OUT/MacSongManager-${VERSION}-${RID}.zip"

rm -rf "$APP" "$PUB"
mkdir -p "$OUT"

dotnet publish "$ROOT/src/MacSongManager.App/MacSongManager.App.csproj" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:UseAppHost=true \
  -p:PublishSingleFile=false \
  -o "$PUB"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
sed "s/__VERSION__/${VERSION}/g" "$ROOT/packaging/Info.plist.template" > "$APP/Contents/Info.plist"
cp -R "$PUB"/. "$APP/Contents/MacOS/"
if [[ -f "$ROOT/packaging/AppIcon.icns" ]]; then
  cp "$ROOT/packaging/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
fi
cp "$ROOT/THIRD_PARTY_NOTICES.md" "$APP/Contents/Resources/THIRD_PARTY_NOTICES.md"
cp "$ROOT/NOTICE" "$APP/Contents/Resources/NOTICE"
cp "$ROOT/LICENSE" "$APP/Contents/Resources/LICENSE"

if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$APP" || true
fi

rm -f "$ZIP"
if command -v ditto >/dev/null 2>&1; then
  ditto -c -k --keepParent "$APP" "$ZIP"
else
  (cd "$OUT" && zip -r -y "$(basename "$ZIP")" "Mac Song Manager.app")
fi

echo "Wrote $ZIP"
