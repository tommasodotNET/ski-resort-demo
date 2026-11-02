#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
version="$(sed -n 's/.*"version": "\([^"]*\)".*/\1/p' global.json)"
if [[ ! -x .dotnet/dotnet ]] || [[ "$(.dotnet/dotnet --version)" != "$version" ]]; then
    installer="$(mktemp "${TMPDIR:-/tmp}/alpineai-dotnet.XXXXXX")"
    trap 'rm -f "$installer"' EXIT
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
    bash "$installer" --version "$version" --install-dir "$PWD/.dotnet"
fi
.dotnet/dotnet restore AlpineAI.App
.dotnet/dotnet build AlpineAI.App --no-restore
