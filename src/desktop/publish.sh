#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
rid="${1:?Usage: bash publish.sh linux-x64|linux-arm64|osx-arm64|osx-x64}"
case "$rid" in
    linux-x64|linux-arm64) output="$PWD/artifacts/$rid" ;;
    osx-arm64|osx-x64) output="$PWD/artifacts/$rid/AlpineAI.app/Contents/MacOS" ;;
    *) echo "Unsupported desktop runtime: $rid" >&2; exit 1 ;;
esac
if [[ ! -x .dotnet/dotnet ]]; then
    echo "Run bash src/desktop/setup.sh first." >&2
    exit 1
fi
.dotnet/dotnet publish AlpineAI.App -c Release -r "$rid" --self-contained true \
    -p:PublishTrimmed=false -o "$output"
if [[ "$rid" == osx-* ]]; then
    cp macos/Info.plist "$output/../Info.plist"
    if [[ "$(uname -s)" == Darwin ]]; then
        codesign --force --deep --sign - "$output/../.."
    else
        echo "macOS bundle created. Ad-hoc sign on macOS before use: codesign --force --deep --sign - AlpineAI.app"
    fi
fi
echo "Published to $output"
