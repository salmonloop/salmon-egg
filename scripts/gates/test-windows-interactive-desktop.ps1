param(
    [string] $ArtifactsDirectory = "artifacts/windows-packaged-gui"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This gate requires an actual Windows desktop.' }

New-Item -ItemType Directory -Force -Path $ArtifactsDirectory | Out-Null
$project = Join-Path $PSScriptRoot 'windows-desktop-probe/windows-desktop-probe.csproj'
$buildLog = Join-Path $ArtifactsDirectory 'desktop-build.log'
dotnet build $project --configuration Release -p:UseSharedCompilation=false -m:1 --disable-build-servers *> $buildLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $buildLog
    throw 'The standard WindowsDesktop probe did not compile.'
}

$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = (Get-Command dotnet).Source
$start.UseShellExecute = $false
$start.ArgumentList.Add((Join-Path $PSScriptRoot 'windows-desktop-probe/bin/Release/net10.0-windows/SalmonEgg.Gates.WindowsDesktopProbe.dll'))
$start.ArgumentList.Add((Resolve-Path $ArtifactsDirectory).Path)
$process = [Diagnostics.Process]::Start($start)
try {
    if (-not $process.WaitForExit(30000)) {
        throw 'The native desktop probe exceeded its 30-second process budget.'
    }
    if ($process.ExitCode -ne 0) {
        throw "Native input did not reach the hosted desktop window. Probe exit: $($process.ExitCode)."
    }
}
finally {
    if (-not $process.HasExited) {
        $process.Kill($true)
        if (-not $process.WaitForExit(5000)) { throw 'The owned desktop probe did not exit after termination.' }
    }
    $process.Dispose()
}
