#!/usr/bin/env bash
set -euo pipefail

# Linux-host gamepad approximation gates:
# 1) Core unit matrix for multi-brand identity / face / trigger / dual-path policy
# 2) BrowserWasm multi-brand inject smoke (Playwright + getGamepads override)
#
# Skia Desktop GUI smoke is intentionally NOT claimed as gamepad evidence here:
# net10.0-desktop registers NoOpGamepad* services, so physical Linux pads / XTest
# shell probes do not exercise the authoritative gamepad semantic chain.
#
# This gate does NOT replace Windows MSIX + physical PS/Xbox/Switch Diagnostics.

CONFIGURATION="${1:-Debug}"
SCRIPT_SOURCE="${BASH_SOURCE[0]}"
SCRIPT_DIR="${SCRIPT_SOURCE%/*}"
if [ "${SCRIPT_DIR}" = "${SCRIPT_SOURCE}" ]; then
  SCRIPT_DIR="."
fi
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd -P)"
PROJECT="${REPO_ROOT}/SalmonEgg/SalmonEgg/SalmonEgg.csproj"
CORE_TESTS="${REPO_ROOT}/tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj"
WWWROOT="${REPO_ROOT}/SalmonEgg/SalmonEgg/bin/${CONFIGURATION}/net10.0-browserwasm/wwwroot"
SERVER_PID=""
PLAYWRIGHT_WORKDIR=""

DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet || true)}"
NODE_BIN="${NODE_BIN:-$(command -v node || true)}"
NPM_BIN="${NPM_BIN:-$(command -v npm || true)}"
PYTHON_BIN="${PYTHON_BIN:-$(command -v python3 || true)}"
CURL_BIN="${CURL_BIN:-$(command -v curl || true)}"
export DOTNET_ROOT="${DOTNET_ROOT:-/home/ubuntu/.dotnet}"

if [ -z "${DOTNET_BIN}" ]; then
  echo "Unable to locate dotnet in PATH." >&2
  exit 1
fi
if [ -z "${NODE_BIN}" ]; then
  echo "Unable to locate node in PATH." >&2
  exit 1
fi
if [ -z "${NPM_BIN}" ]; then
  echo "Unable to locate npm in PATH." >&2
  exit 1
fi
if [ -z "${PYTHON_BIN}" ]; then
  echo "Unable to locate python3 in PATH." >&2
  exit 1
fi
if [ -z "${CURL_BIN}" ]; then
  echo "Unable to locate curl in PATH." >&2
  exit 1
fi

cleanup() {
  if [ -n "${SERVER_PID}" ] && kill -0 "${SERVER_PID}" 2>/dev/null; then
    kill "${SERVER_PID}" 2>/dev/null || true
    wait "${SERVER_PID}" 2>/dev/null || true
  fi
  if [ -n "${PLAYWRIGHT_WORKDIR}" ] && [ -d "${PLAYWRIGHT_WORKDIR}" ]; then
    rm -rf "${PLAYWRIGHT_WORKDIR}"
  fi
}
trap cleanup EXIT INT TERM

run_playwright_smoke() {
  if command -v xvfb-run >/dev/null 2>&1; then
    xvfb-run -a "${NODE_BIN}" "$@"
    return
  fi
  "${NODE_BIN}" "$@"
}

echo "[linux-gamepad] Core multi-brand unit matrix"
"${DOTNET_BIN}" test \
  --project "${CORE_TESTS}" \
  --configuration "${CONFIGURATION}" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.GamepadHidMaestroProfileCatalogTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.GamepadControllerIdentityTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.GamepadActiveReadingSelectorTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.GamepadInputPathTrackerTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.BrowserStandardGamepadBrandSemanticsTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.BrowserGamepadIdentityParserTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.BrowserGamepadInputReadingMapperTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.BrowserStandardGamepadPressedButtonsTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.StandardGamepadInputReadingMapperTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.RawGameControllerTriggerAxisPolicyTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.RawGameControllerUnlabeledFaceIndexPolicyTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.RawGameControllerFaceButtonLayoutResolverTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.RawGameControllerInputReadingMapperTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.RawGameControllerAxisNormalizerTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.RawGameControllerButtonLabelMapperTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Input.GamepadAdaptationPipelineTests" \
  --filter-class "SalmonEgg.Presentation.Core.Tests.Settings.GamepadDiagnosticsViewModelTests" \
  --timeout 3m \
  --output Normal

echo "[linux-gamepad] Clean browserwasm output (avoid stale HotReload / package base assets)"
"${DOTNET_BIN}" clean "${PROJECT}" -c "${CONFIGURATION}" -f net10.0-browserwasm -v minimal

echo "[linux-gamepad] Restore browserwasm dependencies"
"${DOTNET_BIN}" restore "${PROJECT}"

echo "[linux-gamepad] Build browserwasm app for gamepad inject smoke"
"${DOTNET_BIN}" build "${PROJECT}" -c "${CONFIGURATION}" -f net10.0-browserwasm --no-restore -v minimal

if [ ! -f "${WWWROOT}/index.html" ]; then
  echo "browserwasm wwwroot was not produced: ${WWWROOT}" >&2
  exit 1
fi

HOST="127.0.0.1"
PORT="${SALMONEGG_WASM_SMOKE_PORT:-$("${PYTHON_BIN}" - <<'PY'
import socket
with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
    sock.bind(("127.0.0.1", 0))
    print(sock.getsockname()[1])
PY
)}"
BASE_URL="http://${HOST}:${PORT}/"

echo "[linux-gamepad] Serve browserwasm wwwroot port=${PORT}"
"${PYTHON_BIN}" -m http.server "${PORT}" --bind "${HOST}" --directory "${WWWROOT}" >/tmp/salmonegg-linux-gamepad-wasm-http.log 2>&1 &
SERVER_PID="$!"

for _ in {1..50}; do
  if ! kill -0 "${SERVER_PID}" 2>/dev/null; then
    echo "browserwasm static server exited before readiness." >&2
    cat /tmp/salmonegg-linux-gamepad-wasm-http.log >&2
    exit 1
  fi
  if "${CURL_BIN}" --noproxy '*' -fsS "${BASE_URL}index.html" >/dev/null 2>&1; then
    break
  fi
  sleep 0.2
done
"${CURL_BIN}" --noproxy '*' -fsS "${BASE_URL}index.html" >/dev/null

PLAYWRIGHT_WORKDIR="$(mktemp -d)"
cp "${REPO_ROOT}/scripts/gates/wasm-gamepad-boundary-smoke.mjs" "${PLAYWRIGHT_WORKDIR}/"
cp -R "${REPO_ROOT}/scripts/gates/wasm-smoke-lib" "${PLAYWRIGHT_WORKDIR}/"

echo "[linux-gamepad] Install Playwright + Chromium"
"${NPM_BIN}" --prefix "${PLAYWRIGHT_WORKDIR}" install --no-audit --no-fund --no-save playwright
"${NPM_BIN}" --prefix "${PLAYWRIGHT_WORKDIR}" exec -- playwright install chromium

echo "[linux-gamepad] Run WASM multi-brand gamepad boundary smoke"
run_playwright_smoke \
  "${PLAYWRIGHT_WORKDIR}/wasm-gamepad-boundary-smoke.mjs" \
  "${BASE_URL}"

echo "[linux-gamepad] PASS"
echo "[linux-gamepad] Covered on Linux:"
echo "  - Core PS/Xbox/Nintendo identity, unlabeled face, analog LT/RT policy, dual-path selector/path tracker, mapper matrix (incl. Sony standard-path), Diagnostics VM projection"
echo "  - BrowserWasm Playwright inject for Xbox/DualSense/Switch Pro ids + standard-position intents + Diagnostics ActiveInputs"
echo "[linux-gamepad] Not covered here (need Windows host):"
echo "  - HIDMaestro multi-profile OS-path runner: scripts/gates/run-hidmaestro-multiprofile-native-smoke.ps1"
echo "  - FlaUI native-device Diagnostics smoke / MSIX physical matrix"
echo "  - Skia Desktop shell (NoOpGamepad* on Linux desktop — not gamepad semantic evidence)"
