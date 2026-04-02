param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Write-Host "[gate] Build app"
dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj -c $Configuration -v minimal

Write-Host "[gate] Core race/lifecycle contracts"
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj `
  -c $Configuration `
  --filter "FullyQualifiedName~NavigationCoordinatorTests|FullyQualifiedName~AcpChatCoordinatorTests|FullyQualifiedName~AcpConnectionSessionCleanerTests|FullyQualifiedName~AcpConnectionEvictionOptionsLoaderTests" `
  -v minimal

Write-Host "[gate] ACP protocol contracts"
dotnet test tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj `
  -c $Configuration `
  --filter "FullyQualifiedName~AcpClientTests|FullyQualifiedName~AppSettingsServiceTests" `
  -v minimal

Write-Host "[gate] UI conventions"
dotnet test tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj `
  -c $Configuration `
  --filter "FullyQualifiedName~UiConventionsTests" `
  -v minimal

Write-Host "[gate] Core gates passed"
