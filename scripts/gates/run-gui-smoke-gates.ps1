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
        [string]$Filter
    )

    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        try {
            Write-Host "[gate] GUI test attempt $attempt/$Retries filter=$Filter"
            dotnet test tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj `
              -c $Configuration `
              --filter $Filter `
              -v minimal
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

Invoke-DotNetTestWithRetry -Filter "FullyQualifiedName~RandomSwitchWithOneSecondCadence_FinalSelectionAlwaysDrivesRightPane"
Invoke-DotNetTestWithRetry -Filter "FullyQualifiedName~SelectAcrossProfilesAndLocal_OneSecondCadence_FinalIntentAlwaysWins"
Invoke-DotNetTestWithRetry -Filter "FullyQualifiedName~SelectRemoteSession_RepeatedClicksWithLocalDetour_DoesNotHangAndHydratesLatestSelection"

Write-Host "[gate] GUI smoke gates passed"
