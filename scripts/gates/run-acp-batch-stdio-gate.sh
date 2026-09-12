#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
if [ "$(uname -s)" != "Linux" ]; then
  echo "This gate requires Linux; real stdio cases must not be skipped." >&2
  exit 1
fi

dotnet_bin="${DOTNET_BIN:-dotnet}"
configuration="${1:-Release}"
results_dir="${2:-artifacts/acp-batch-stdio}"
mkdir -p "$results_dir"
log_file="$results_dir/stdio.log"

timeout --signal=TERM --kill-after=10s 180s "$dotnet_bin" test \
  --project tests/SalmonEgg.Acp.Desktop.Tests/SalmonEgg.Acp.Desktop.Tests.csproj \
  --configuration "$configuration" \
  -p:UseSharedCompilation=false \
  --filter-class SalmonEgg.Acp.Desktop.Tests.StdioBatchTests \
  --timeout 1m --no-ansi --minimum-expected-tests 2 --output Detailed > "$log_file" 2>&1 || {
    cat "$log_file"
    exit 1
  }
cat "$log_file"
python3 - "$log_file" <<'PY'
from pathlib import Path
import re
import sys

text = Path(sys.argv[1]).read_text()
counts = {name: re.findall(r'^\s+' + name + r':\s+(\d+)\s*$', text, re.M)
          for name in ('succeeded', 'failed', 'skipped')}
if any(not values for values in counts.values()) or int(counts['succeeded'][-1]) < 2 \
        or int(counts['failed'][-1]) != 0 or int(counts['skipped'][-1]) != 0:
    raise SystemExit('Both real stdio version contracts must execute successfully with zero skips.')
print('[gate] Real stdio v1 refusal and v2 batch dispatch/cancellation/response passed.')
PY
