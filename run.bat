@echo off
setlocal
cd /d "%~dp0"
dotnet run --project src\BindProxy.Avalonia\BindProxy.Avalonia.csproj %*
