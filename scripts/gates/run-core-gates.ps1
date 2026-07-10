param(
    [string]$Configuration = "Debug"
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

Write-Host "[gate] Restore solution"
Invoke-GateCommand { dotnet restore SalmonEgg.sln }

Write-Host "[gate] Build app"
Invoke-GateCommand { dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj `
  -c $Configuration `
  -f net10.0-desktop `
  -p:SalmonEggTargetFrameworks=net10.0-desktop `
  -p:SalmonEggAllTargetFrameworks=net10.0-desktop `
  --no-restore `
  -v minimal }

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

Write-Host "[gate] ACP protocol contracts"
Invoke-GateCommand { dotnet test `
  --project tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj `
  --configuration $Configuration `
  --filter-class SalmonEgg.Infrastructure.Tests.Client.AcpClientTests `
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

Write-Host "[gate] Core gates passed"
