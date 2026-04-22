param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $SkipMsixRefresh,

    [switch] $IncludeRealUser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj'

if (-not $SkipMsixRefresh)
{
    Write-Host "Refreshing MSIX install before session GUI regression..."
    & (Join-Path $repoRoot '.tools\run-winui3-msix.ps1') -Configuration $Configuration
}

$env:SALMONEGG_GUI = '1'

$deterministicFilters = @(
    'FullyQualifiedName~ChatSkeletonSmokeTests.SelectRemoteSessionWithSlowReplay_AutoScrollsToLatestMessageAfterHydration'
    'FullyQualifiedName~ChatSkeletonSmokeTests.HydratedRemoteSession_NavigateToDiscoverAndBack_ReturnsHotWithoutRemoteReload'
    'FullyQualifiedName~ChatSkeletonSmokeTests.HydratedRemoteSession_SwitchToOtherRemoteSessionAndBack_ReturnsHotWithoutRemoteReload'
    'FullyQualifiedName~ChatSkeletonSmokeTests.BackgroundRemoteSession_LiveAgentUpdate_ShowsUnreadAndClearsWhenActivated'
    'FullyQualifiedName~ChatSkeletonSmokeTests.SelectSessionWithMarkdownMessages_DoubleClickCodeBlock_DoesNotCrash'
    'FullyQualifiedName~ChatSkeletonSmokeTests.MarkdownSession_AfterDiscoverRoundTrip_RetainsRenderedCodeAndDoesNotCrash'
    'FullyQualifiedName~ChatSkeletonSmokeTests.MarkdownSession_AfterAcpSettingsRoundTrip_RetainsRenderedCodeAndDoesNotCrash'
) -join '|'

Write-Host "Running deterministic session GUI regression suite..."
& dotnet test $project `
    -c $Configuration `
    -m:1 `
    -nr:false `
    --filter $deterministicFilters `
    --logger "console;verbosity=minimal" `
    --logger "trx;LogFileName=gui-session-regression-deterministic.trx"

if ($IncludeRealUser)
{
    $realUserFilters = @(
        'FullyQualifiedName~RealUserConfigSmokeTests.SelectRemoteBoundSession_AfterDiscoverRoundTrip_ReturnsWithoutStuckReload'
        'FullyQualifiedName~RealUserConfigSmokeTests.SelectRemoteBoundSession_AfterAcpSettingsRoundTrip_ReturnsWithoutCrash'
    ) -join '|'

    Write-Host "Running real-user ACP round-trip probes..."
    & dotnet test $project `
        -c $Configuration `
        -m:1 `
        -nr:false `
        --filter $realUserFilters `
        --logger "console;verbosity=minimal" `
        --logger "trx;LogFileName=gui-session-regression-realuser.trx"
}
