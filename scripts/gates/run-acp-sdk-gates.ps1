param(
    [string]$Configuration = "Debug",
    [string]$PackageOutput = "artifacts/acp-sdk-pack"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$dotnet = if ([string]::IsNullOrWhiteSpace($env:DOTNET_BIN)) { "dotnet" } else { $env:DOTNET_BIN }

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

if (Test-Path $PackageOutput)
{
    Remove-Item -Recurse -Force $PackageOutput
}
New-Item -ItemType Directory -Force -Path $PackageOutput | Out-Null

Write-Host "[gate] Restore ACP SDK"
Invoke-GateCommand { & $dotnet restore tests/SalmonEgg.Acp.Tests/SalmonEgg.Acp.Tests.csproj }

Write-Host "[gate] Build ACP SDK"
Invoke-GateCommand { & $dotnet build src/SalmonEgg.Acp/SalmonEgg.Acp.csproj `
  --configuration $Configuration `
  --no-restore `
  -v minimal }

Write-Host "[gate] Build ACP SDK tests"
Invoke-GateCommand { & $dotnet build tests/SalmonEgg.Acp.Tests/SalmonEgg.Acp.Tests.csproj `
  --configuration $Configuration `
  --no-restore `
  -v minimal }

Write-Host "[gate] ACP SDK contracts"
Invoke-GateCommand { & $dotnet test `
  --project tests/SalmonEgg.Acp.Tests/SalmonEgg.Acp.Tests.csproj `
  --configuration $Configuration `
  --no-build `
  --timeout 5m `
  --output Normal }

Write-Host "[gate] Pack ACP SDK"
Invoke-GateCommand { & $dotnet pack src/SalmonEgg.Acp/SalmonEgg.Acp.csproj `
  --configuration $Configuration `
  --no-build `
  --output $PackageOutput `
  -v minimal }

Write-Host "[gate] ACP SDK gates passed"
