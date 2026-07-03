#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <publish-directory>" >&2
  exit 2
fi

publish_dir="$1"
app_path="${publish_dir%/}/SalmonEgg"

missing=0
if [[ ! -x "$app_path" ]]; then
  echo "Missing executable publish artifact: $app_path" >&2
  missing=1
fi

if ! command -v xvfb-run >/dev/null 2>&1; then
  echo "Missing xvfb-run. Install xvfb to run Linux desktop smoke tests headlessly." >&2
  missing=1
fi

if ! ldconfig -p | grep -Eq 'libwebkit2gtk|libwebkitgtk'; then
  echo "Missing WebKitGTK runtime. Install the platform WebKitGTK package for Uno desktop WebView support." >&2
  missing=1
fi

if ! ldconfig -p | grep -Eq 'libjavascriptcoregtk|JavaScriptCore'; then
  echo "Missing JavaScriptCoreGTK runtime. Install the platform JavaScriptCoreGTK package for WebKitGTK." >&2
  missing=1
fi

if [[ "$missing" -ne 0 ]]; then
  exit 1
fi

log_path="$(mktemp -t salmonegg-linux-desktop-smoke.XXXXXX.log)"
set +e
timeout 20s xvfb-run -a "$app_path" >"$log_path" 2>&1
exit_code=$?
set -e

if [[ "$exit_code" -ne 0 && "$exit_code" -ne 124 ]]; then
  cat "$log_path" >&2
  echo "Linux desktop smoke failed with exit code $exit_code." >&2
  exit "$exit_code"
fi

if grep -Eiq 'FT_Get_BDF_Property|DllNotFoundException|EntryPointNotFoundException|Segmentation fault|Unhandled exception' "$log_path"; then
  cat "$log_path" >&2
  echo "Linux desktop smoke detected a native/runtime startup failure." >&2
  exit 1
fi

echo "Linux desktop smoke passed for $app_path"
echo "Smoke log: $log_path"
