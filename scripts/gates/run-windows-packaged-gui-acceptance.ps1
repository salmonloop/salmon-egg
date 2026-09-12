param(
    [string] $ArtifactsDirectory = "artifacts/windows-packaged-gui"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This gate requires Windows and a real current MSIX installation.' }
New-Item -ItemType Directory -Force -Path $ArtifactsDirectory | Out-Null
$env:SALMONEGG_GUI = '1'
$env:DOTNET_PROCESSOR_COUNT = '2'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'
$env:SALMONEGG_GUI_ACCEPTANCE_ARTIFACTS = (Resolve-Path $ArtifactsDirectory).Path
$env:SALMONEGG_APPDATA_ROOT = Join-Path $env:SALMONEGG_GUI_ACCEPTANCE_ARTIFACTS 'appdata'

$log = Join-Path $ArtifactsDirectory 'packaged-gui-tests.log'
$arguments = @(
    'test', '--project', 'tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj',
    '--configuration', 'Debug', '--no-build', '-p:UseSharedCompilation=false',
    '--filter-class', 'SalmonEgg.GuiTests.Windows.AcpSettingsSmokeTests',
    '--filter-class', 'SalmonEgg.GuiTests.Windows.TerminalAuthenticationSmokeTests',
    '--minimum-expected-tests', '5', '--timeout', '5m', '--no-ansi', '--output', 'Detailed'
)
$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName = (Get-Command dotnet).Source
$info.UseShellExecute = $false
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
foreach ($argument in $arguments) { $info.ArgumentList.Add($argument) }
$test = [Diagnostics.Process]::Start($info)
$standardOutput = $test.StandardOutput.ReadToEndAsync()
$standardError = $test.StandardError.ReadToEndAsync()
try {
    if (-not $test.WaitForExit(330000)) {
        throw 'Installed GUI test process exceeded the 330-second runtime bound. See gui-stage.jsonl.'
    }
    $testExit = $test.ExitCode
}
finally {
    if (-not $test.HasExited) { $test.Kill($true); [void]$test.WaitForExit(5000) }
    [IO.File]::WriteAllText($log, $standardOutput.GetAwaiter().GetResult() + $standardError.GetAwaiter().GetResult())
    $profileData = $env:SALMONEGG_APPDATA_ROOT
    foreach ($source in @((Join-Path $profileData 'boot.log'), (Join-Path $profileData 'logs'))) {
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination $ArtifactsDirectory -Recurse -Force
        }
    }
    $test.Dispose()
}
$contents = Get-Content -LiteralPath $log -Raw
Write-Host $contents
$passed = [regex]::Match($contents, '(?m)^\s+succeeded:\s+(\d+)\s*$')
$skipped = [regex]::Match($contents, '(?m)^\s+skipped:\s+(\d+)\s*$')
if ($testExit -ne 0 -or -not $passed.Success -or [int]$passed.Groups[1].Value -lt 5 `
    -or -not $skipped.Success -or [int]$skipped.Groups[1].Value -ne 0) {
    throw "Installed WinUI GUI acceptance failed or skipped: exit=$testExit."
}

$marker = Get-Content -LiteralPath 'artifacts/msix/current-install.json' -Raw | ConvertFrom-Json
$provenance = [ordered]@{
    sourceCommit = (& git rev-parse HEAD).Trim()
    currentInstall = $marker
    acceptedTests = [int]$passed.Groups[1].Value
    skippedTests = [int]$skipped.Groups[1].Value
    scope = 'Current installed MSIX ACP settings and complete terminal consent/input/exit/reconnect/retry/cancellation.'
}
$provenance | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ArtifactsDirectory 'result.json')
Write-Host '[gate] Current installed MSIX accepted native settings and terminal sign-in with no skipped tests.'
