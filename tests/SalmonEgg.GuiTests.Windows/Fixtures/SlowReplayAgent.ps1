param(
    [string]$ScenarioJsonPath,
    [string]$ListenUrl,
    [string]$ReadySignalPath
)

$ErrorActionPreference = 'Stop'

$harnessPath = Join-Path $PSScriptRoot 'MockAcpHarness.ps1'
if (-not (Test-Path -LiteralPath $harnessPath))
{
    throw "MockAcpHarness.ps1 was not found at '$harnessPath'."
}

& $harnessPath @PSBoundParameters
