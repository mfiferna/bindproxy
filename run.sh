#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")"
dotnet run --project src/BindProxy.Avalonia/BindProxy.Avalonia.csproj "$@"
