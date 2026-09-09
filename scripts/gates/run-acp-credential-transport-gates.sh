#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

if [ "$(uname -s)" != "Linux" ]; then
  echo "This gate requires Linux to inspect the actual child environment and command line in /proc." >&2
  exit 1
fi

dotnet_bin="${DOTNET_BIN:-dotnet}"
configuration="${1:-Release}"
results_dir="${2:-artifacts/acp-credentials}"
mkdir -p "$results_dir"
log_file="$results_dir/transport-gates.log"

timeout --signal=TERM --kill-after=10s 180s "$dotnet_bin" test \
  --project tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj \
  --configuration "$configuration" \
  --no-ansi \
  -p:UseSharedCompilation=false \
  --filter-class SalmonEgg.Infrastructure.Tests.Transport.CredentialTransportCanaryTests \
  --minimum-expected-tests 8 \
  --output Detailed > "$log_file" 2>&1 || {
    cat "$log_file"
    exit 1
  }
cat "$log_file"

python3 - "$log_file" <<'PY'
import pathlib
import re
import sys

text = pathlib.Path(sys.argv[1]).read_text()
passed = re.findall(r'^\s+succeeded:\s+(\d+)\s*$', text, re.M)
skipped = re.findall(r'^\s+skipped:\s+(\d+)\s*$', text, re.M)
if not passed or int(passed[-1]) < 8 or not skipped or int(skipped[-1]) != 0:
    raise SystemExit('Credential transport gate requires every real process and endpoint case with zero skips.')
print('[gate] Credential destination, real child environment, HTTP/2 + SSE, WebSocket, and redirect isolation passed.')
PY
