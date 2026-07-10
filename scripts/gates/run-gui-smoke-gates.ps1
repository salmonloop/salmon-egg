param(
    [string]$Configuration = "Debug",
    [int]$Retries = 3
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not $IsWindows) {
    throw "GUI smoke gates require Windows (WinUI/FlaUI). Current platform is not Windows."
}

function Stop-StaleSalmonEggProcesses {
    $names = @("SalmonEgg", "SalmonEgg.GuiTests.Windows")
    foreach ($name in $names) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-DotNetTestWithRetry {
    param(
        [string]$Method
    )

    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        try {
            Write-Host "[gate] GUI test attempt $attempt/$Retries method=$Method"
            dotnet test `
              --project tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj `
              --configuration $Configuration `
              --no-build `
              --filter-method $Method `
              --timeout 5m `
              --output Normal
            if ($LASTEXITCODE -ne 0) {
                throw "GUI test failed with exit code $LASTEXITCODE."
            }

            return
        }
        catch {
            if ($attempt -eq $Retries) {
                throw
            }

            Write-Warning "[gate] GUI test failed on attempt $attempt. Retrying after preflight cleanup."
            Stop-StaleSalmonEggProcesses
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
}

$env:SALMONEGG_GUI = "1"
Stop-StaleSalmonEggProcesses

Write-Host "[gate] Build GUI tests"
dotnet build tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Invoke-DotNetTestWithRetry -Method "SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.RandomSwitchWithOneSecondCadence_FinalSelectionAlwaysDrivesRightPane"
Invoke-DotNetTestWithRetry -Method "SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.SelectAcrossProfilesAndLocal_OneSecondCadence_FinalIntentAlwaysWins"
Invoke-DotNetTestWithRetry -Method "SalmonEgg.GuiTests.Windows.ChatSkeletonSmokeTests.SelectRemoteSession_RepeatedClicksWithLocalDetour_DoesNotHangAndHydratesLatestSelection"
Invoke-DotNetTestWithRetry -Method "SalmonEgg.GuiTests.Windows.NavigationSmokeTests.SearchOverflowSession_MaterializesNativeNavSelection_AndSubsequentNavigationWorks"

Write-Host "[gate] GUI smoke gates passed"
