#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Debug}"
PACKAGE_OUTPUT="${2:-artifacts/acp-sdk-pack}"

DOTNET_BIN="${DOTNET_BIN:-dotnet}"

rm -rf "$PACKAGE_OUTPUT"
mkdir -p "$PACKAGE_OUTPUT"

echo "[gate] Restore ACP SDK"
"$DOTNET_BIN" restore tests/SalmonEgg.Acp.Tests/SalmonEgg.Acp.Tests.csproj
# The package-validation baseline is a real nupkg on disk: restoring the SDK project with the
# baseline property set is what downloads it into the global packages folder, which is where the
# ApiCompat task looks for it during pack. Set the same property the project will derive later so
# restore and validation agree on the baseline version.
if [ -n "${ACP_PACKAGE_BASELINE_VERSION:-}" ]; then
  "$DOTNET_BIN" restore src/SalmonEgg.Acp/SalmonEgg.Acp.csproj \
    -p:PackageValidationBaselineVersion="$ACP_PACKAGE_BASELINE_VERSION"
fi

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
baseline_args=()
# Versions past the first release must package-validate against the last published package; the
# project's AcpBaselineRequired target errors out when the baseline is missing. The variable is
# optional here so the first release (and rehearsed workflow_dispatch runs without tags) still pack.
if [ -n "${ACP_PACKAGE_BASELINE_VERSION:-}" ]; then
  baseline_args+=("-p:AcpPackageBaselineVersion=$ACP_PACKAGE_BASELINE_VERSION")
fi
"$DOTNET_BIN" pack src/SalmonEgg.Acp/SalmonEgg.Acp.csproj \
  --configuration "$CONFIGURATION" \
  --no-build \
  --output "$PACKAGE_OUTPUT" \
  "${baseline_args[@]}" \
  -v minimal

echo "[gate] ACP SDK gates passed"
