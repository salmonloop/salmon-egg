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

Write-Host "[gate] Build app"
Invoke-GateCommand { dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj -c $Configuration -v minimal }

Write-Host "[gate] Core race/lifecycle contracts"
Invoke-GateCommand { dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj `
  -c $Configuration `
  --filter "FullyQualifiedName~NavigationCoordinatorTests|FullyQualifiedName~AcpChatCoordinatorTests|FullyQualifiedName~AcpConnectionSessionCleanerTests|FullyQualifiedName~AcpConnectionEvictionOptionsLoaderTests" `
  -v minimal }

Write-Host "[gate] ACP protocol contracts"
Invoke-GateCommand { dotnet test tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj `
  -c $Configuration `
  --filter "FullyQualifiedName~AcpClientTests|FullyQualifiedName~AppSettingsServiceTests" `
  -v minimal }

Write-Host "[gate] UI conventions"
Invoke-GateCommand { dotnet test tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj `
  -c $Configuration `
  --filter "FullyQualifiedName~UiConventionsTests" `
  -v minimal }

Write-Host "[gate] Core gates passed"
