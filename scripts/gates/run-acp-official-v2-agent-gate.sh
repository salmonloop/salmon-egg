#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output="${1:-$repo_root/artifacts/acp-official-v2-agent}"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
source_dir="$output/rust-sdk"
source_commit=3a6d0ae88dbaa09fcb74e26761e3643a75b2015e
if [ ! -d "$source_dir/.git" ]; then
  git init -q "$source_dir"
  git -C "$source_dir" remote add origin https://github.com/agentclientprotocol/rust-sdk.git
fi
git -C "$source_dir" fetch --depth 1 origin "$source_commit"
git -C "$source_dir" checkout --detach "$source_commit"
test "$(git -C "$source_dir" rev-parse HEAD)" = "$source_commit"
test -z "$(git -C "$source_dir" status --porcelain --untracked-files=no)"

(
  cd "$source_dir"
  CARGO_BUILD_JOBS=2 timeout --signal=TERM --kill-after=10s 600s cargo build --locked \
    -p agent-client-protocol --features unstable_protocol_v2 --example simple_agent_v2
) > "$output/agent-build.log" 2>&1

export SALMONEGG_OFFICIAL_V2_AGENT="$source_dir/target/debug/examples/simple_agent_v2"
export DOTNET_PROCESSOR_COUNT=2 DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1
cd "$repo_root"
timeout --signal=TERM --kill-after=10s 180s "${DOTNET_BIN:-dotnet}" test \
  --project tests/SalmonEgg.Acp.Desktop.Tests/SalmonEgg.Acp.Desktop.Tests.csproj \
  --configuration Release --no-ansi -p:UseSharedCompilation=false \
  --filter-class SalmonEgg.Acp.Desktop.Tests.OfficialV2AgentAcceptanceTests \
  --minimum-expected-tests 2 --output Detailed > "$output/client.log" 2>&1
cat "$output/client.log"
python3 - "$output/client.log" <<'PY'
from pathlib import Path
import re
import sys
text = Path(sys.argv[1]).read_text()
if not re.search(r'^\s+succeeded:\s+2\s*$', text, re.M) or not re.search(r'^\s+skipped:\s+0\s*$', text, re.M):
    raise SystemExit('The upstream v2 Agent must run through the production client without skips.')
PY
sha256sum "$SALMONEGG_OFFICIAL_V2_AGENT" > "$output/agent.sha256"
git rev-parse HEAD > "$output/client-commit.txt"
git -C "$source_dir" rev-parse HEAD > "$output/agent-commit.txt"
