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

$log = Join-Path $ArtifactsDirectory 'packaged-gui-tests.log'
$arguments = @(
    'test', '--project', 'tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj',
    '--configuration', 'Debug', '-p:UseSharedCompilation=false',
    '--filter-class', 'SalmonEgg.GuiTests.Windows.AcpSettingsSmokeTests',
    '--minimum-expected-tests', '1', '--timeout', '3m', '--no-ansi', '--output', 'Detailed'
)
& dotnet @arguments *> $log
$testExit = $LASTEXITCODE
$contents = Get-Content -LiteralPath $log -Raw
Write-Host $contents
$passed = [regex]::Match($contents, '(?m)^\s+succeeded:\s+(\d+)\s*$')
$skipped = [regex]::Match($contents, '(?m)^\s+skipped:\s+(\d+)\s*$')
if ($testExit -ne 0 -or -not $passed.Success -or [int]$passed.Groups[1].Value -lt 1 `
    -or -not $skipped.Success -or [int]$skipped.Groups[1].Value -ne 0) {
    throw "Installed WinUI GUI acceptance failed or skipped: exit=$testExit."
}

$marker = Get-Content -LiteralPath 'artifacts/msix/current-install.json' -Raw | ConvertFrom-Json
$provenance = [ordered]@{
    sourceCommit = (& git rev-parse HEAD).Trim()
    currentInstall = $marker
    acceptedTests = [int]$passed.Groups[1].Value
    skippedTests = [int]$skipped.Groups[1].Value
    scope = 'Actual installed MSIX launch and native ACP settings navigation; terminal sign-in remains separately gated.'
}
$provenance | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ArtifactsDirectory 'result.json')
Write-Host '[gate] Current installed MSIX accepted native ACP settings input with no skipped tests.'
