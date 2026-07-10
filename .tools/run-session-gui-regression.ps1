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

$deterministicMethods = @(
    'SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.AuxiliaryPanels_AfterCloseAndReopen_RetainContentInsteadOfBlankSurface'
    'SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.SelectRemoteSessionWithSlowReplay_AutoScrollsToLatestMessageAfterHydration'
    'SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.HydratedRemoteSession_NavigateToDiscoverAndBack_ReturnsHotWithoutRemoteReload'
    'SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.HydratedRemoteSession_SwitchToOtherRemoteSessionAndBack_ReturnsHotWithoutRemoteReload'
    'SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.BackgroundRemoteSession_LiveAgentUpdate_ShowsUnreadAndClearsWhenActivated'
    'SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.SelectSessionWithMarkdownMessages_DoubleClickCodeBlock_DoesNotCrash'
    'SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.MarkdownSession_AfterDiscoverRoundTrip_RetainsRenderedCodeAndDoesNotCrash'
    'SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.MarkdownSession_AfterAcpSettingsRoundTrip_RetainsRenderedCodeAndDoesNotCrash'
)

Write-Host "Running deterministic session GUI regression suite..."
& dotnet test `
    --project $project `
    --configuration $Configuration `
    --filter-method $deterministicMethods `
    --timeout 20m `
    --output Normal `
    --report-xunit-trx `
    --report-xunit-trx-filename gui-session-regression-deterministic.trx
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($IncludeRealUser)
{
    $realUserMethods = @(
        'SalmonEgg.GuiTests.Windows.RealUserConfigSmokeTests.SelectRemoteBoundSession_AfterDiscoverRoundTrip_ReturnsWithoutStuckReload'
        'SalmonEgg.GuiTests.Windows.RealUserConfigSmokeTests.SelectRemoteBoundSession_AfterAcpSettingsRoundTrip_ReturnsWithoutCrash'
    )

    Write-Host "Running real-user ACP round-trip probes..."
    & dotnet test `
        --project $project `
        --configuration $Configuration `
        --filter-method $realUserMethods `
        --timeout 10m `
        --output Normal `
        --report-xunit-trx `
        --report-xunit-trx-filename gui-session-regression-realuser.trx
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
