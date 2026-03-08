@echo off
echo ========================================
echo Uno ACP Client Build Script
echo ========================================
echo.

echo [1/4] Restoring dependencies...
dotnet restore UnoAcpClient.sln
if %errorlevel% neq 0 exit /b %errorlevel%

set "WINSDK_BIN=%ProgramFiles(x86)%\Windows Kits\10\bin"
if exist "%WINSDK_BIN%" goto :sdkok
set "WINSDK_BIN=%ProgramFiles%\Windows Kits\10\bin"
if exist "%WINSDK_BIN%" goto :sdkok

echo.
echo ERROR: Windows 10/11 SDK not found. WinUI 3 builds require the Windows SDK (10.0.19041.0 or newer).
echo Install it via Visual Studio Installer: Individual components: Windows 10 SDK.
exit /b 1

:sdkok

echo.
echo [2/4] Building project...
dotnet build UnoAcpClient.sln --configuration Release --no-restore
if %errorlevel% neq 0 exit /b %errorlevel%

echo.
echo [3/4] Running tests...
dotnet test UnoAcpClient.sln --configuration Release --no-build
if %errorlevel% neq 0 exit /b %errorlevel%

echo.
echo [4/4] Publishing application...
dotnet publish UnoAcpClient/UnoAcpClient/UnoAcpClient.csproj ^
  --configuration Release ^
  --framework net10.0-windows10.0.19041.0 ^
  --output publish/windows-desktop ^
  --no-build

echo.
echo ========================================
echo Build completed successfully!
echo Output: publish/windows-desktop/
echo ========================================
pause
