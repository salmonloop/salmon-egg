#!/usr/bin/env bash
# Verifies the browser Notification bridge used by the WASM notification service.
#
# The bridge is pure browser interaction, so a managed unit test would stub the very API under test.
# This gate builds the WASM target and runs the module that was actually produced by that build in a
# real browser, with notification permission genuinely granted or denied per case.
set -euo pipefail

configuration="${1:-Release}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="$repo_root/SalmonEgg/SalmonEgg/SalmonEgg.csproj"
module_name="salmon-egg-wasm-notifications.js"
smoke_script="$repo_root/scripts/gates/wasm-notification-module-smoke.mjs"

for tool in dotnet node npm; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "[wasm-notification-gate] Missing $tool in PATH." >&2
    exit 1
  fi
done

echo "[wasm-notification-gate] Build the WASM target so the module under test is this build's output"
dotnet build "$project" -f net10.0-browserwasm -c "$configuration" --nologo -v quiet

# Assert the module reached the published package, not just the repo. A module that exists in source
# but never ships would make every managed call fall back to Failed at runtime.
wwwroot="$repo_root/SalmonEgg/SalmonEgg/bin/$configuration/net10.0-browserwasm/wwwroot"
mapfile -t module_paths < <(find "$wwwroot" -name "$module_name" -type f 2>/dev/null | sort)
if [[ ${#module_paths[@]} -eq 0 ]]; then
  echo "[wasm-notification-gate] FAIL: $module_name is not in the WASM package under $wwwroot" >&2
  exit 1
fi
module_path="${module_paths[0]}"
echo "[wasm-notification-gate] Module under test: ${module_path#"$repo_root/"}"

work_dir="$(mktemp -d -t salmonegg-wasm-notification.XXXXXX)"
trap 'rm -rf "$work_dir"' EXIT

echo "[wasm-notification-gate] Install Playwright and Chromium"
npm --prefix "$work_dir" install --no-audit --no-fund --no-save playwright >"$work_dir/npm.log" 2>&1 || {
  echo "[wasm-notification-gate] Playwright install failed:" >&2
  tail -20 "$work_dir/npm.log" >&2
  exit 1
}
# The granted-permission path needs the full Chromium build: the headless shell always reports
# notification permission as denied regardless of the grant.
npm --prefix "$work_dir" exec -- playwright install chromium >"$work_dir/browser.log" 2>&1 || {
  echo "[wasm-notification-gate] Chromium install failed:" >&2
  tail -20 "$work_dir/browser.log" >&2
  exit 1
}

cp "$smoke_script" "$work_dir/smoke.mjs"
echo "[wasm-notification-gate] Run the browser notification module smoke"
(cd "$work_dir" && node smoke.mjs "$module_path")

echo "[wasm-notification-gate] PASS"
