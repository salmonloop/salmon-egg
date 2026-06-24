#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Debug}"
HOST="127.0.0.1"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="${REPO_ROOT}/SalmonEgg/SalmonEgg/SalmonEgg.csproj"
WWWROOT="${REPO_ROOT}/SalmonEgg/SalmonEgg/bin/${CONFIGURATION}/net10.0-browserwasm/wwwroot"
COMMIT="$(git -C "${REPO_ROOT}" rev-parse HEAD)"
SERVER_PID=""
PLAYWRIGHT_WORKDIR=""

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
    xvfb-run -a node "$@"
    return
  fi

  node "$@"
}

PORT="${SALMONEGG_WASM_SMOKE_PORT:-$(python3 - <<'PY'
import socket

with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
    sock.bind(("127.0.0.1", 0))
    print(sock.getsockname()[1])
PY
)}"
BASE_URL="http://${HOST}:${PORT}/"

echo "[gate] Clean browserwasm output"
dotnet clean "${PROJECT}" -c "${CONFIGURATION}" -f net10.0-browserwasm -v minimal

echo "[gate] Restore browserwasm dependencies"
dotnet restore "${PROJECT}"

echo "[gate] Build browserwasm app"
dotnet build "${PROJECT}" -c "${CONFIGURATION}" -f net10.0-browserwasm --no-restore -v minimal

if [ ! -f "${WWWROOT}/index.html" ]; then
  echo "browserwasm wwwroot was not produced: ${WWWROOT}" >&2
  exit 1
fi

echo "[gate] Serve browserwasm app from ${WWWROOT}"
echo "[gate] Runtime source commit=${COMMIT} port=${PORT}"
python3 -m http.server "${PORT}" --bind "${HOST}" --directory "${WWWROOT}" >/tmp/salmonegg-wasm-smoke-http.log 2>&1 &
SERVER_PID="$!"

for _ in {1..50}; do
  if ! kill -0 "${SERVER_PID}" 2>/dev/null; then
    echo "browserwasm static server exited before readiness." >&2
    cat /tmp/salmonegg-wasm-smoke-http.log >&2
    exit 1
  fi

  if curl -fsS "${BASE_URL}index.html" >/dev/null 2>&1; then
    break
  fi

  sleep 0.2
done

curl -fsS "${BASE_URL}index.html" >/dev/null
echo "[gate] Static server ready pid=${SERVER_PID} base=${BASE_URL}"

PLAYWRIGHT_WORKDIR="$(mktemp -d)"
cp "${REPO_ROOT}/scripts/gates/wasm-settings-navigation-smoke.mjs" "${PLAYWRIGHT_WORKDIR}/"
cp "${REPO_ROOT}/scripts/gates/wasm-file-system-availability-smoke.mjs" "${PLAYWRIGHT_WORKDIR}/"

echo "[gate] Install Playwright package"
npm --prefix "${PLAYWRIGHT_WORKDIR}" install --no-audit --no-fund --no-save playwright

echo "[gate] Install Playwright Chromium"
npm --prefix "${PLAYWRIGHT_WORKDIR}" exec -- playwright install chromium

echo "[gate] Run WASM settings navigation smoke"
run_playwright_smoke \
  "${PLAYWRIGHT_WORKDIR}/wasm-settings-navigation-smoke.mjs" \
  "${BASE_URL}"

echo "[gate] Run WASM file system availability smoke"
run_playwright_smoke \
  "${PLAYWRIGHT_WORKDIR}/wasm-file-system-availability-smoke.mjs" \
  "${BASE_URL}"

echo "[gate] WASM smoke gates passed"
