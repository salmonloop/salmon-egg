@echo off
chcp 65001 >nul
echo Starting SalmonEgg...

set "REPO_ROOT=%~dp0"
pushd "%REPO_ROOT%" >nul

if /I "%1"=="desktop" goto :desktop

set "WINSDK_BIN=%ProgramFiles(x86)%\Windows Kits\10\bin"
if exist "%WINSDK_BIN%" goto :run
set "WINSDK_BIN=%ProgramFiles%\Windows Kits\10\bin"
if exist "%WINSDK_BIN%" goto :run

echo ERROR: Windows 10/11 SDK not found. WinUI 3 builds require the Windows SDK (10.0.19041.0 or newer).
echo Install it via Visual Studio Installer: Individual components: Windows 10 SDK.
exit /b 1

:run
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" goto :vsok
echo ERROR: Visual Studio Build Tools not found (vswhere.exe missing).
echo WinUI 3 builds require Visual Studio 2022 (or Build Tools 2022) with MSBuild and C++ build tools.
echo Install: Visual Studio Installer -^> Workloads: "Desktop development with C++" (includes MSBuild + MSVC).
exit /b 1

:vsok
for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%I"
if defined VSINSTALL goto :runapp
echo ERROR: MSVC C++ toolchain not installed.
echo Install Visual Studio 2022 (or Build Tools 2022) workload "Desktop development with C++", and ensure "MSVC v143 - VS 2022 C++ x64/x86 build tools" is selected.
exit /b 1

:runapp
set "PWSH_EXE="
for /f "usebackq delims=" %%I in (`where pwsh 2^>nul`) do (
  set "PWSH_EXE=%%I"
  goto :gotpwsh
)
:gotpwsh
if defined PWSH_EXE (
  "%PWSH_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%REPO_ROOT%.tools\run-winui3-msix.ps1" -Configuration Debug
) else (
  powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%REPO_ROOT%.tools\run-winui3-msix.ps1" -Configuration Debug
)
set "EC=%errorlevel%"
popd >nul
exit /b %EC%

:desktop
dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj --framework net10.0-desktop
set "EC=%errorlevel%"
popd >nul
exit /b %EC%
