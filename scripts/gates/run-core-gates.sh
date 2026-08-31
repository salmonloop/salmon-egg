#!/usr/bin/env bash
#
# Core contract gates: a restricted single-TFM app build plus the race, persistence, and UI-convention
# contract suites.
#
# Usage: run-core-gates.sh [Configuration] [--skip-solution-restore] [--skip-contract-suites]
#
# The two skip flags exist for a CI job that already restored the same solution and already ran the full
# solution test pass (which includes every class filtered below). The app build is never skippable: it
# constrains SalmonEggTargetFrameworks to net10.0-desktop, and that restricted target graph is the one
# thing here a full-solution build does not exercise. Defaults run everything, because a developer
# invoking this directly has usually not just completed a full restore and test pass.
set -euo pipefail

CONFIGURATION="Debug"
SKIP_SOLUTION_RESTORE=0
SKIP_CONTRACT_SUITES=0

for arg in "$@"; do
  case "$arg" in
    --skip-solution-restore) SKIP_SOLUTION_RESTORE=1 ;;
    --skip-contract-suites) SKIP_CONTRACT_SUITES=1 ;;
    -*) echo "Unknown option: $arg" >&2; exit 2 ;;
    *) CONFIGURATION="$arg" ;;
  esac
done

if [ "$SKIP_SOLUTION_RESTORE" -eq 1 ]; then
  echo "[gate] Restore solution: skipped (already restored by the caller)"
else
  echo "[gate] Restore solution"
  dotnet restore SalmonEgg.sln
fi

echo "[gate] Build app"
dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj \
  -c "$CONFIGURATION" \
  -f net10.0-desktop \
  -p:SalmonEggTargetFrameworks=net10.0-desktop \
  -p:SalmonEggAllTargetFrameworks=net10.0-desktop \
  --no-restore \
  -v minimal

if [ "$SKIP_CONTRACT_SUITES" -eq 1 ]; then
  echo "[gate] Contract suites: skipped (covered by the caller's full solution test pass)"
else
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

  echo "[gate] Configuration persistence contracts"
  dotnet test \
    --project tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj \
    --configuration "$CONFIGURATION" \
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
fi

echo "[gate] Core gates passed"

