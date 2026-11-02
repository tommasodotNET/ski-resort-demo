#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
if [[ ! -x .dotnet/dotnet ]]; then
    echo "Desktop SDK missing. Run: bash src/desktop/setup.sh from the repository root." >&2
    exit 1
fi
export DOTNET_ROOT="$PWD/.dotnet"
# Aspire may supply architecture-specific roots, which take precedence over DOTNET_ROOT.
export DOTNET_ROOT_X64="$DOTNET_ROOT"
export DOTNET_ROOT_ARM64="$DOTNET_ROOT"
exec .dotnet/dotnet run --project AlpineAI.App --no-launch-profile "$@"
