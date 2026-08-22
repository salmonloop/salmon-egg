<#
.SYNOPSIS
    Runs the core contract gates: a restricted single-TFM app build plus the race, persistence, and
    UI-convention contract suites.

.PARAMETER SkipSolutionRestore
    Skip `dotnet restore SalmonEgg.sln`. Intended for a CI job that already restored the same solution in
    the same workspace; running it a second time re-resolves every package for no new information.

.PARAMETER SkipContractSuites
    Skip the three contract test suites. Intended for a CI job that already ran the full solution test
    pass, which includes every one of these classes. What is NOT skippable is the app build above: it
    constrains SalmonEggTargetFrameworks to net10.0-desktop, and that restricted target graph is the one
    thing here a full-solution build does not exercise.

    Locally the defaults run everything, because a developer invoking this script has usually not just
    completed a full solution restore and test pass.
#>
param(
    [string]$Configuration = "Debug",
    [switch]$SkipSolutionRestore,
    [switch]$SkipContractSuites
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-GateCommand
{
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    & $Command
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

if ($SkipSolutionRestore)
{
    Write-Host "[gate] Restore solution: skipped (already restored by the caller)"
}
else
{
    Write-Host "[gate] Restore solution"
    Invoke-GateCommand { dotnet restore SalmonEgg.sln }
}

Write-Host "[gate] Build app"
Invoke-GateCommand { dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj `
  -c $Configuration `
  -f net10.0-desktop `
  -p:SalmonEggTargetFrameworks=net10.0-desktop `
  -p:SalmonEggAllTargetFrameworks=net10.0-desktop `
  --no-restore `
  -v minimal }

if ($SkipContractSuites)
{
    Write-Host "[gate] Contract suites: skipped (covered by the caller's full solution test pass)"
}
else
{
    Write-Host "[gate] Core race/lifecycle contracts"
    Invoke-GateCommand { dotnet test `
      --project tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj `
      --configuration $Configuration `
      --filter-class SalmonEgg.Presentation.Core.Tests.Navigation.NavigationCoordinatorTests `
      --filter-class SalmonEgg.Presentation.Core.Tests.Chat.AcpChatCoordinatorTests `
      --filter-class SalmonEgg.Presentation.Core.Tests.Chat.AcpConnectionSessionCleanerTests `
      --filter-class SalmonEgg.Presentation.Core.Tests.Chat.AcpConnectionEvictionOptionsLoaderTests `
      --timeout 5m `
      --output Normal }

    Write-Host "[gate] Configuration persistence contracts"
    Invoke-GateCommand { dotnet test `
      --project tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj `
      --configuration $Configuration `
      --filter-class SalmonEgg.Infrastructure.Tests.Storage.AppSettingsServiceTests `
      --timeout 5m `
      --output Normal }

    Write-Host "[gate] UI conventions"
    Invoke-GateCommand { dotnet test `
      --project tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj `
      --configuration $Configuration `
      --filter-class SalmonEgg.Application.Tests.UiConventionsTests `
      --timeout 3m `
      --output Normal }
}

Write-Host "[gate] Core gates passed"
