#!/bin/bash
# Builds a single self-contained mSlide.exe (Windows x64) from macOS/Linux/Windows.
# Requires the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
set -euo pipefail
cd "$(dirname "$0")"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
"$DOTNET" publish mSlide/mSlide.csproj -c Release -o dist
echo "Built: $(pwd)/dist/mSlide.exe"
