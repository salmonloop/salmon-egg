#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Debug}"
HOST="127.0.0.1"
SCRIPT_SOURCE="${BASH_SOURCE[0]}"
SCRIPT_DIR="${SCRIPT_SOURCE%/*}"
if [ "${SCRIPT_DIR}" = "${SCRIPT_SOURCE}" ]; then
  SCRIPT_DIR="."
fi
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd -P)"
PROJECT="${REPO_ROOT}/SalmonEgg/SalmonEgg/SalmonEgg.csproj"
WWWROOT="${REPO_ROOT}/SalmonEgg/SalmonEgg/bin/${CONFIGURATION}/net10.0-browserwasm/wwwroot"
SERVER_PID=""
PLAYWRIGHT_WORKDIR=""

GIT_BIN="${GIT_BIN:-$(command -v git || true)}"
DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet || true)}"
NODE_BIN="${NODE_BIN:-$(command -v node || true)}"
NPM_BIN="${NPM_BIN:-$(command -v npm || true)}"
PYTHON_BIN="${PYTHON_BIN:-$(command -v python3 || true)}"
CURL_BIN="${CURL_BIN:-$(command -v curl || true)}"

if [ -z "${GIT_BIN}" ]; then
  echo "Unable to locate git in PATH." >&2
  exit 1
fi

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

is_wsl_environment() {
  if [ -n "${WSL_INTEROP:-}" ]; then
    return 0
  fi

  if [ ! -r /proc/sys/kernel/osrelease ]; then
    return 1
  fi

  kernel_release="$(< /proc/sys/kernel/osrelease)"
  case "${kernel_release}" in
    *[Mm]icrosoft*|*[Ww][Ss][Ll]*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

is_windows_interop_binary() {
  case "$1" in
    *.exe|*.cmd|/mnt/[a-zA-Z]/*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

if is_wsl_environment; then
  for tool_name in GIT_BIN DOTNET_BIN NODE_BIN NPM_BIN PYTHON_BIN CURL_BIN; do
    tool_path="${!tool_name}"
    if is_windows_interop_binary "${tool_path}"; then
      echo "Refusing to run BrowserWasm smoke with Windows interop binary '${tool_path}' from WSL. Use a native Linux toolchain in WSL or run this gate from Windows Git Bash." >&2
      exit 1
    fi
  done
fi

COMMIT="$("${GIT_BIN}" -C "${REPO_ROOT}" rev-parse HEAD)"

cleanup() {
  if [ -n "${SERVER_PID}" ] && kill -0 "${SERVER_PID}" 2>/dev/null; then
    kill "${SERVER_PID}" 2>/dev/null || true
    wait "${SERVER_PID}" 2>/dev/null || true
  fi

  if [ -n "${PLAYWRIGHT_WORKDIR}" ] && [ -d "${PLAYWRIGHT_WORKDIR}" ]; then
    rm -rf "${PLAYWRIGHT_WORKDIR}"
  fi
}

trap cleanup EXIT

run_playwright_smoke() {
  if command -v xvfb-run >/dev/null 2>&1; then
    xvfb-run -a "${NODE_BIN}" "$@"
    return
  fi

  "${NODE_BIN}" "$@"
}

PORT="${SALMONEGG_WASM_SMOKE_PORT:-$("${PYTHON_BIN}" - <<'PY'
import socket

with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
    sock.bind(("127.0.0.1", 0))
    print(sock.getsockname()[1])
PY
)}"
BASE_URL="http://${HOST}:${PORT}/"

echo "[gate] Clean browserwasm output"
"${DOTNET_BIN}" clean "${PROJECT}" -c "${CONFIGURATION}" -f net10.0-browserwasm -v minimal

echo "[gate] Restore browserwasm dependencies"
"${DOTNET_BIN}" restore "${PROJECT}"

echo "[gate] Build browserwasm app"
"${DOTNET_BIN}" build "${PROJECT}" -c "${CONFIGURATION}" -f net10.0-browserwasm --no-restore -v minimal

if [ ! -f "${WWWROOT}/index.html" ]; then
  echo "browserwasm wwwroot was not produced: ${WWWROOT}" >&2
  exit 1
fi

echo "[gate] Serve browserwasm app from ${WWWROOT}"
echo "[gate] Runtime source commit=${COMMIT} port=${PORT}"
"${PYTHON_BIN}" -m http.server "${PORT}" --bind "${HOST}" --directory "${WWWROOT}" >/tmp/salmonegg-wasm-smoke-http.log 2>&1 &
SERVER_PID="$!"

for _ in {1..50}; do
  if ! kill -0 "${SERVER_PID}" 2>/dev/null; then
    echo "browserwasm static server exited before readiness." >&2
    cat /tmp/salmonegg-wasm-smoke-http.log >&2
    exit 1
  fi

  if "${CURL_BIN}" --noproxy '*' -fsS "${BASE_URL}index.html" >/dev/null 2>&1; then
    break
  fi

  sleep 0.2
done

"${CURL_BIN}" --noproxy '*' -fsS "${BASE_URL}index.html" >/dev/null
echo "[gate] Static server ready pid=${SERVER_PID} base=${BASE_URL}"

PLAYWRIGHT_WORKDIR="$(mktemp -d)"
cp "${REPO_ROOT}/scripts/gates/wasm-settings-navigation-smoke.mjs" "${PLAYWRIGHT_WORKDIR}/"
cp "${REPO_ROOT}/scripts/gates/wasm-start-visibility-smoke.mjs" "${PLAYWRIGHT_WORKDIR}/"
cp "${REPO_ROOT}/scripts/gates/wasm-focus-boundary-smoke.mjs" "${PLAYWRIGHT_WORKDIR}/"
cp "${REPO_ROOT}/scripts/gates/wasm-settings-persistence-smoke.mjs" "${PLAYWRIGHT_WORKDIR}/"
cp "${REPO_ROOT}/scripts/gates/wasm-capability-boundary-smoke.mjs" "${PLAYWRIGHT_WORKDIR}/"
cp "${REPO_ROOT}/scripts/gates/wasm-gamepad-boundary-smoke.mjs" "${PLAYWRIGHT_WORKDIR}/"
cp "${REPO_ROOT}/scripts/gates/wasm-acp-full-chain-smoke.mjs" "${PLAYWRIGHT_WORKDIR}/"
cp -R "${REPO_ROOT}/scripts/gates/wasm-smoke-lib" "${PLAYWRIGHT_WORKDIR}/"

echo "[gate] Install Playwright package"
"${NPM_BIN}" --prefix "${PLAYWRIGHT_WORKDIR}" install --no-audit --no-fund --no-save playwright

echo "[gate] Install Playwright Chromium"
"${NPM_BIN}" --prefix "${PLAYWRIGHT_WORKDIR}" exec -- playwright install chromium

echo "[gate] Run WASM settings navigation smoke"
run_playwright_smoke \
  "${PLAYWRIGHT_WORKDIR}/wasm-settings-navigation-smoke.mjs" \
  "${BASE_URL}"

echo "[gate] Run WASM start visibility smoke"
run_playwright_smoke \
  "${PLAYWRIGHT_WORKDIR}/wasm-start-visibility-smoke.mjs" \
  "${BASE_URL}"

echo "[gate] Run WASM focus boundary smoke"
run_playwright_smoke \
  "${PLAYWRIGHT_WORKDIR}/wasm-focus-boundary-smoke.mjs" \
  "${BASE_URL}"

echo "[gate] Run WASM settings persistence smoke"
run_playwright_smoke \
  "${PLAYWRIGHT_WORKDIR}/wasm-settings-persistence-smoke.mjs" \
  "${BASE_URL}"

echo "[gate] Run WASM capability boundary smoke"
run_playwright_smoke \
  "${PLAYWRIGHT_WORKDIR}/wasm-capability-boundary-smoke.mjs" \
  "${BASE_URL}"

echo "[gate] Run WASM gamepad boundary smoke"
run_playwright_smoke \
  "${PLAYWRIGHT_WORKDIR}/wasm-gamepad-boundary-smoke.mjs" \
  "${BASE_URL}"

echo "[gate] Run WASM ACP full-chain smoke"
run_playwright_smoke \
  "${PLAYWRIGHT_WORKDIR}/wasm-acp-full-chain-smoke.mjs" \
  "${BASE_URL}"

echo "[gate] WASM smoke gates passed"
