@echo off
setlocal
cd /d "%~dp0"
dotnet run --project src\BindProxy.Tui\BindProxy.Tui.csproj %*
