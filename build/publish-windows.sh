#!/usr/bin/env bash
# Erstellt PropTax.App.exe (Windows x64, Single-File, self-contained) unter publish/win-x64.
# Aufruf: build/publish-windows.sh [Version]   z. B. build/publish-windows.sh 1.0.1
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:-}"
EXTRA=()
if [[ -n "$VERSION" ]]; then EXTRA+=("-p:Version=$VERSION"); fi
dotnet test -c Release
dotnet publish src/PropTax.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true "${EXTRA[@]}" -o publish/win-x64
rm -f publish/win-x64/*.pdb
ls -lh publish/win-x64
