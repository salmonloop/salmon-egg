#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Debug}"
PACKAGE_OUTPUT="${2:-artifacts/acp-sdk-pack}"

DOTNET_BIN="${DOTNET_BIN:-dotnet}"

rm -rf "$PACKAGE_OUTPUT"
mkdir -p "$PACKAGE_OUTPUT"

echo "[gate] Restore ACP SDK"
"$DOTNET_BIN" restore tests/SalmonEgg.Acp.Tests/SalmonEgg.Acp.Tests.csproj

echo "[gate] Check ACP SDK formatting"
"$DOTNET_BIN" format src/SalmonEgg.Acp/SalmonEgg.Acp.csproj \
  --verify-no-changes \
  --no-restore
"$DOTNET_BIN" format tests/SalmonEgg.Acp.Tests/SalmonEgg.Acp.Tests.csproj \
  --verify-no-changes \
  --no-restore

echo "[gate] Build ACP SDK with analyzers"
"$DOTNET_BIN" build src/SalmonEgg.Acp/SalmonEgg.Acp.csproj \
  --configuration "$CONFIGURATION" \
  --no-restore \
  -p:EnforceCodeStyleInBuild=true \
  -v minimal

echo "[gate] Build ACP SDK tests with analyzers"
"$DOTNET_BIN" build tests/SalmonEgg.Acp.Tests/SalmonEgg.Acp.Tests.csproj \
  --configuration "$CONFIGURATION" \
  --no-restore \
  -p:EnforceCodeStyleInBuild=true \
  -v minimal

echo "[gate] ACP SDK contracts"
"$DOTNET_BIN" test \
  --project tests/SalmonEgg.Acp.Tests/SalmonEgg.Acp.Tests.csproj \
  --configuration "$CONFIGURATION" \
  --no-build \
  --timeout 5m \
  --output Normal

echo "[gate] Pack ACP SDK"
"$DOTNET_BIN" pack src/SalmonEgg.Acp/SalmonEgg.Acp.csproj \
  --configuration "$CONFIGURATION" \
  --no-build \
  --output "$PACKAGE_OUTPUT" \
  -v minimal

echo "[gate] ACP SDK gates passed"
