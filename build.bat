@echo off
chcp 65001 >nul
if /I "%1"=="msix" goto :msix
if /I "%1"=="desktop" goto :desktop
if /I "%1"=="-h" goto :usage
if /I "%1"=="--help" goto :usage
if /I "%1"=="/?" goto :usage

echo ========================================
echo SalmonEgg Build Script
echo ========================================
echo.

echo [1/4] Restoring dependencies...
dotnet restore SalmonEgg.sln
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
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" goto :vsok
echo.
echo ERROR: Visual Studio Build Tools not found (vswhere.exe missing).
echo WinUI 3 builds require Visual Studio 2022 (or Build Tools 2022) with MSBuild and C++ build tools.
echo Install: Visual Studio Installer -^> Workloads: "Desktop development with C++" (includes MSBuild + MSVC).
exit /b 1

:vsok
for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%I"
if defined VSINSTALL goto :build
echo.
echo ERROR: MSVC C++ toolchain not installed.
echo Install Visual Studio 2022 (or Build Tools 2022) workload "Desktop development with C++", and ensure "MSVC v143 - VS 2022 C++ x64/x86 build tools" is selected.
exit /b 1

 :build
echo.
echo [2/4] Building project...
dotnet build SalmonEgg.sln --configuration Release --no-restore
if %errorlevel% neq 0 exit /b %errorlevel%

echo.
echo [3/4] Running tests...
dotnet test SalmonEgg.sln --configuration Release --no-build
if %errorlevel% neq 0 exit /b %errorlevel%

echo.
echo [4/4] Publishing application...
dotnet publish SalmonEgg/SalmonEgg/SalmonEgg.csproj ^
  --configuration Release ^
  --framework net10.0-desktop ^
  --output publish/windows-desktop ^
  --no-build

echo.
echo ========================================
echo Build completed successfully!
echo Output: publish/windows-desktop/
echo ========================================
pause
exit /b 0

:msix
echo ========================================
echo SalmonEgg MSIX Build
echo ========================================
echo.

set "WINSDK_BIN=%ProgramFiles(x86)%\Windows Kits\10\bin"
if exist "%WINSDK_BIN%" goto :msix_sdkok
set "WINSDK_BIN=%ProgramFiles%\Windows Kits\10\bin"
if exist "%WINSDK_BIN%" goto :msix_sdkok

echo.
echo ERROR: Windows 10/11 SDK not found. WinUI 3 MSIX builds require the Windows SDK (10.0.19041.0 or newer).
echo Install it via Visual Studio Installer: Individual components: Windows 10 SDK.
exit /b 1

:msix_sdkok
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" goto :msix_vsok
echo.
echo ERROR: Visual Studio Build Tools not found (vswhere.exe missing).
echo WinUI 3 MSIX builds require Visual Studio 2022 (or Build Tools 2022) with MSBuild and C++ build tools.
echo Install: Visual Studio Installer -^> Workloads: "Desktop development with C++" (includes MSBuild + MSVC).
exit /b 1

:msix_vsok
for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%I"
if defined VSINSTALL goto :msix_build
echo.
echo ERROR: MSVC C++ toolchain not installed.
echo Install Visual Studio 2022 (or Build Tools 2022) workload "Desktop development with C++", and ensure "MSVC v143 - VS 2022 C++ x64/x86 build tools" is selected.
exit /b 1

:msix_build
set "REPO_ROOT=%~dp0"
pushd "%REPO_ROOT%" >nul
set "PWSH_EXE="
for /f "usebackq delims=" %%I in (`where pwsh 2^>nul`) do (
  set "PWSH_EXE=%%I"
  goto :msix_gotpwsh
)
:msix_gotpwsh
if defined PWSH_EXE (
  "%PWSH_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%REPO_ROOT%.tools\run-winui3-msix.ps1" -Configuration Release -SkipInstall
) else (
  powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%REPO_ROOT%.tools\run-winui3-msix.ps1" -Configuration Release -SkipInstall
)
set "EC=%errorlevel%"
popd >nul
exit /b %EC%

:desktop
goto :build

:usage
echo.
echo Usage:
echo   build.bat            ^(default: desktop release build^)
echo   build.bat desktop    ^(restore/build/test/publish desktop^)
echo   build.bat msix       ^(build MSIX package, no install^)
echo.
exit /b 0
