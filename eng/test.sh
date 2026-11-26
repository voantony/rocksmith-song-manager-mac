#!/usr/bin/env bash
# Build and test. Source must not contain publisher decryption constants.
set -eu
ROOT="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if grep -R -n -E 'psarcKey|sngKey|profileKey|AES\.MODE' src tests; then
  echo "Refusing to build: publisher key or decryption constant found." >&2
  exit 1
fi

dotnet test "$ROOT/MacSongManager.sln" -c Release "$@"
dotnet build "$ROOT/src/MacSongManager.App/MacSongManager.App.csproj" -c Release --nologo
