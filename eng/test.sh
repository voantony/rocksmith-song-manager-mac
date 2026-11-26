#!/usr/bin/env bash
# Build and test. Source must not contain publisher decryption constants
# or long embedded hex strings (conversion keys are loaded from a user file).
set -eu
ROOT="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if grep -R -n -E 'psarcKey|sngKey|profileKey|AES\.MODE' src tests; then
  echo "Refusing to build: publisher key or decryption constant found." >&2
  exit 1
fi

if grep -R -n -E 'FromHexString\(\s*"[0-9A-Fa-f]{32,}' src tests packaging; then
  echo "Refusing to build: embedded hex key material." >&2
  exit 1
fi

dotnet test "$ROOT/MacSongManager.sln" -c Release "$@"
dotnet build "$ROOT/src/MacSongManager.App/MacSongManager.App.csproj" -c Release --nologo
