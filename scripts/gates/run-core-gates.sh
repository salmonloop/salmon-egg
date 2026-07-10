#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Debug}"

echo "[gate] Restore solution"
dotnet restore SalmonEgg.sln

echo "[gate] Build app"
dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  -c "$CONFIGURATION" \
  -f net10.0-desktop \
  -p:SalmonEggTargetFrameworks=net10.0-desktop \
  -p:SalmonEggAllTargetFrameworks=net10.0-desktop \
  --no-restore \
  -v minimal

echo "[gate] Core race/lifecycle contracts"
dotnet test \
  --project tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj \
  --configuration "$CONFIGURATION" \
  --filter-class SalmonEgg.Presentation.Core.Tests.Navigation.NavigationCoordinatorTests \
  --filter-class SalmonEgg.Presentation.Core.Tests.Chat.AcpChatCoordinatorTests \
  --filter-class SalmonEgg.Presentation.Core.Tests.Chat.AcpConnectionSessionCleanerTests \
  --filter-class SalmonEgg.Presentation.Core.Tests.Chat.AcpConnectionEvictionOptionsLoaderTests \
  --timeout 5m \
  --output Normal

echo "[gate] ACP protocol contracts"
dotnet test \
  --project tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj \
  --configuration "$CONFIGURATION" \
  --filter-class SalmonEgg.Infrastructure.Tests.Client.AcpClientTests \
  --filter-class SalmonEgg.Infrastructure.Tests.Storage.AppSettingsServiceTests \
  --timeout 5m \
  --output Normal

echo "[gate] UI conventions"
dotnet test \
  --project tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj \
  --configuration "$CONFIGURATION" \
  --filter-class SalmonEgg.Application.Tests.UiConventionsTests \
  --timeout 3m \
  --output Normal

echo "[gate] Core gates passed"

