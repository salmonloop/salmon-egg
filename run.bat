@echo off
chcp 65001 >nul
echo Starting SalmonEgg...

set "REPO_ROOT=%~dp0"
pushd "%REPO_ROOT%" >nul

if /I "%1"=="desktop" goto :desktop
if /I "%1"=="msix" goto :msix
if /I "%1"=="wasm" goto :wasm
if /I "%1"=="-h" goto :usage
if /I "%1"=="--help" goto :usage
if /I "%1"=="/?" goto :usage

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
for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -prerelease -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%I"
if defined VSINSTALL goto :runapp
echo ERROR: MSVC C++ toolchain not installed.
echo Install Visual Studio 2022 (or Build Tools 2022) workload "Desktop development with C++", and ensure "MSVC v143 - VS 2022 C++ x64/x86 build tools" is selected.
exit /b 1

:runapp
:msix
call :parse_config "%~2"
if errorlevel 1 (
  popd >nul
  exit /b 1
)
set "PWSH_EXE="
for /f "usebackq delims=" %%I in (`where pwsh 2^>nul`) do (
  set "PWSH_EXE=%%I"
  goto :gotpwsh
)
:gotpwsh
if defined PWSH_EXE (
  "%PWSH_EXE%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%REPO_ROOT%.tools\run-winui3-msix.ps1" -Configuration %SALMON_CONFIG%
) else (
  powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%REPO_ROOT%.tools\run-winui3-msix.ps1" -Configuration %SALMON_CONFIG%
)
set "EC=%errorlevel%"
popd >nul
exit /b %EC%

:desktop
call :parse_config "%~2"
if errorlevel 1 (
  popd >nul
  exit /b 1
)
if /I "%OS%"=="Windows_NT" call :require_vcredist_x64
if errorlevel 1 (
  set "EC=%errorlevel%"
  popd >nul
  exit /b %EC%
)
dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj --framework net10.0-desktop -c %SALMON_CONFIG%
set "EC=%errorlevel%"
popd >nul
exit /b %EC%

:wasm
call :parse_config "%~2"
if errorlevel 1 (
  popd >nul
  exit /b 1
)
echo ========================================
echo SalmonEgg WebAssembly Run
echo ========================================
echo.
echo Starting dev server at http://localhost:5000
echo.
dotnet run --project SalmonEgg/SalmonEgg/SalmonEgg.csproj --framework net10.0-browserwasm -c %SALMON_CONFIG%
set "EC=%errorlevel%"
popd >nul
exit /b %EC%

:usage
echo.
echo Usage:
echo   run.bat                    ^(default: msix Debug^)
echo   run.bat msix [config]      ^(build/install/run WinUI 3 MSIX^)
echo   run.bat desktop [config]   ^(dotnet run net10.0-desktop^)
echo   run.bat wasm [config]      ^(dotnet run net10.0-browserwasm^)
echo.
echo   [config] is optional: Debug ^(default^) or Release.
echo   e.g. run.bat msix release
echo.
popd >nul
exit /b 0

:parse_config
set "SALMON_CONFIG=Debug"
if "%~1"=="" exit /b 0
if /I "%~1"=="debug" exit /b 0
if /I "%~1"=="release" (
  set "SALMON_CONFIG=Release"
  exit /b 0
)
echo ERROR: Unknown configuration "%~1". Valid values: Debug, Release.
exit /b 1

:require_vcredist_x64
if exist "%SystemRoot%\System32\vcruntime140.dll" if exist "%SystemRoot%\System32\vcruntime140_1.dll" if exist "%SystemRoot%\System32\msvcp140.dll" exit /b 0
set "VCREDIST_INSTALLED="
for /f "tokens=3" %%I in ('reg query "HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64" /v Installed 2^>nul ^| findstr /R /C:"Installed"') do set "VCREDIST_INSTALLED=%%I"
if /I "%VCREDIST_INSTALLED%"=="0x1" exit /b 0

echo ERROR: Visual C++ x64 runtime is not installed.
echo The Skia Desktop target can fail with "side-by-side configuration is incorrect" when this runtime is missing.
echo Install Microsoft Visual C++ Redistributable 2015-2022 x64:
echo   https://aka.ms/vs/17/release/vc_redist.x64.exe
echo.
echo For Windows native development, prefer the MSIX path:
echo   run.bat
exit /b 1
