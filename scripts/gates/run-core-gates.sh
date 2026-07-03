#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Debug}"

echo "[gate] Build app"
dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj -c "$CONFIGURATION" -v minimal

echo "[gate] Core race/lifecycle contracts"
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj \
  -c "$CONFIGURATION" \
  --filter "FullyQualifiedName~NavigationCoordinatorTests|FullyQualifiedName~AcpChatCoordinatorTests|FullyQualifiedName~AcpConnectionSessionCleanerTests|FullyQualifiedName~AcpConnectionEvictionOptionsLoaderTests" \
  -v minimal

echo "[gate] ACP protocol contracts"
dotnet test tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj \
  -c "$CONFIGURATION" \
  --filter "FullyQualifiedName~AcpClientTests|FullyQualifiedName~AppSettingsServiceTests" \
  -v minimal

echo "[gate] UI conventions"
dotnet test tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj \
  -c "$CONFIGURATION" \
  --filter "FullyQualifiedName~UiConventionsTests" \
  -v minimal

echo "[gate] Core gates passed"

