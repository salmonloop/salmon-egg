#!/usr/bin/env bash
set -euo pipefail

# Launch the production bridge with fixtures/cancellation-peer.py as its agent first. The project
# cannot invent that deployment command. This gate requires its endpoint and fresh peer stdin log.
: "${SALMONEGG_ACP_CANCELLATION_BRIDGE_URL:?Set the production bridge WebSocket endpoint}"
: "${SALMONEGG_ACP_CANCELLATION_PEER_LOG:?Set the fresh absolute peer stdin log path}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
dotnet_bin="${DOTNET_BIN:-dotnet}"
results_dir="${1:-artifacts/acp-cancellation-bridge}"
mkdir -p "$results_dir"
log_file="$results_dir/bridge-gate.log"

timeout --signal=TERM --kill-after=10s 180s "$dotnet_bin" test \
  --project tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj \
  --configuration Release -p:UseSharedCompilation=false \
  --filter-class SalmonEgg.Infrastructure.Tests.Transport.ProductionBridgeCancellationTests \
  --minimum-expected-tests 1 --output Detailed > "$log_file" 2>&1 || {
    cat "$log_file"
    exit 1
  }
cat "$log_file"
python3 - "$log_file" <<'PY'
import pathlib
import re
import sys

text = pathlib.Path(sys.argv[1]).read_text()
if not re.search(r'^\s+succeeded:\s+1\s*$', text, re.M) or not re.search(r'^\s+skipped:\s+0\s*$', text, re.M):
    raise SystemExit('Production bridge was not verified; missing endpoint/peer is not a passing gate.')
print('[gate] Production bridge forwarded cancellation to the controlled stdio peer and received its terminal response.')
PY
