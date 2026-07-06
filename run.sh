#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")"
dotnet run --project src/BindProxy.Tui/BindProxy.Tui.csproj "$@"
