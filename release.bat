@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

set RID=win-x64
set CONFIG=Release
set OUT=release
set VERSION=%1
if "%VERSION%"=="" set VERSION=dev

rem Native AOT linking needs vswhere.exe on PATH to find link.exe.
where vswhere >nul 2>nul
if errorlevel 1 (
  set "PATH=%PATH%;C:\Program Files (x86)\Microsoft Visual Studio\Installer"
)

if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%\avalonia"
mkdir "%OUT%\tui"

echo == Publishing BindProxy.Avalonia (%RID%, self-contained, AOT) ==
dotnet publish src\BindProxy.Avalonia\BindProxy.Avalonia.csproj -c %CONFIG% -r %RID% --self-contained -p:DebugType=none -o "%OUT%\avalonia"
if errorlevel 1 goto :error
del /q "%OUT%\avalonia\*.pdb" 2>nul

echo == Publishing BindProxy.Tui (%RID%, self-contained, AOT) ==
dotnet publish src\BindProxy.Tui\BindProxy.Tui.csproj -c %CONFIG% -r %RID% --self-contained -p:DebugType=none -o "%OUT%\tui"
if errorlevel 1 goto :error
del /q "%OUT%\tui\*.pdb" 2>nul

echo == Packaging zips ==
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%OUT%\avalonia\*' -DestinationPath '%OUT%\bindproxy-%VERSION%-%RID%.zip' -Force"
if errorlevel 1 goto :error
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%OUT%\tui\*' -DestinationPath '%OUT%\bindproxy-tui-%VERSION%-%RID%.zip' -Force"
if errorlevel 1 goto :error

echo.
echo Done. Artifacts:
dir /b "%OUT%\*.zip"
goto :eof

:error
echo Release build failed.
exit /b 1
