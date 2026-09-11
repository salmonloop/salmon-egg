#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

if [ "$(uname -s)" != "Linux" ]; then
  echo "This gate requires Linux so the real POSIX stdio peer cannot be skipped." >&2
  exit 1
fi

dotnet_bin="${DOTNET_BIN:-dotnet}"
configuration="${1:-Release}"
results_dir="${2:-artifacts/acp-cancellation}"
mkdir -p "$results_dir"
log_file="$results_dir/transport-gates.log"

timeout --signal=TERM --kill-after=10s 180s "$dotnet_bin" test \
  --project tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj \
  --configuration "$configuration" \
  --no-ansi \
  -p:UseSharedCompilation=false \
  --filter-class SalmonEgg.Infrastructure.Tests.Transport.StdioCancelRequestFullStackTests \
  --filter-class SalmonEgg.Infrastructure.Tests.Transport.WebSocketCancelRequestFullStackTests \
  --filter-class SalmonEgg.Infrastructure.Tests.Transport.StreamableHttpCancelRequestFullStackTests \
  --filter-class SalmonEgg.Infrastructure.Tests.Transport.StreamableHttpPermissionRetryFullStackTests \
  --filter-class SalmonEgg.Infrastructure.Tests.Transport.NetworkCancellationErrorOwnershipTests \
  --minimum-expected-tests 8 \
  --output Detailed > "$log_file" 2>&1 || {
    cat "$log_file"
    exit 1
  }
cat "$log_file"

# A skip is not evidence of traffic crossing a real pipe/socket. MTP's count includes skips, so
# assert successful executions and zero skips separately instead of relying on the minimum alone.
python3 - "$log_file" <<'PY'
import pathlib
import re
import sys

text = pathlib.Path(sys.argv[1]).read_text()
passed = re.findall(r'^\s+succeeded:\s+(\d+)\s*$', text, re.M)
skipped = re.findall(r'^\s+skipped:\s+(\d+)\s*$', text, re.M)
if not passed or int(passed[-1]) < 8 or not skipped or int(skipped[-1]) != 0:
    raise SystemExit('Cancellation transport gate did not execute every real transport case without skips.')
print('[gate] Real stdio, WebSocket, HTTP/2 + SSE cancellation and permission retries passed with no skipped cases.')
PY
