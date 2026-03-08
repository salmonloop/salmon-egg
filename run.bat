@echo off
echo Starting Uno ACP Client...

set "WINSDK_BIN=%ProgramFiles(x86)%\Windows Kits\10\bin"
if exist "%WINSDK_BIN%" goto :run
set "WINSDK_BIN=%ProgramFiles%\Windows Kits\10\bin"
if exist "%WINSDK_BIN%" goto :run

echo ERROR: Windows 10/11 SDK not found. WinUI 3 builds require the Windows SDK (10.0.19041.0 or newer).
echo Install it via Visual Studio Installer: Individual components: Windows 10 SDK.
exit /b 1

:run
dotnet run --project UnoAcpClient/UnoAcpClient/UnoAcpClient.csproj --framework net10.0-windows10.0.19041.0
