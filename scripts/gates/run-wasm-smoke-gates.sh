#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Debug}"
PORT="${SALMONEGG_WASM_SMOKE_PORT:-5123}"
HOST="127.0.0.1"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="${REPO_ROOT}/SalmonEgg/SalmonEgg/SalmonEgg.csproj"
WWWROOT="${REPO_ROOT}/SalmonEgg/SalmonEgg/bin/${CONFIGURATION}/net10.0-browserwasm/wwwroot"
BASE_URL="http://${HOST}:${PORT}/"
SERVER_PID=""

cleanup() {
  if [ -n "${SERVER_PID}" ] && kill -0 "${SERVER_PID}" 2>/dev/null; then
    kill "${SERVER_PID}" 2>/dev/null || true
    wait "${SERVER_PID}" 2>/dev/null || true
  fi
}

trap cleanup EXIT

echo "[gate] Build browserwasm app"
dotnet build "${PROJECT}" -c "${CONFIGURATION}" -f net10.0-browserwasm --no-restore -v minimal

if [ ! -f "${WWWROOT}/index.html" ]; then
  echo "browserwasm wwwroot was not produced: ${WWWROOT}" >&2
  exit 1
fi

echo "[gate] Serve browserwasm app from ${WWWROOT}"
python3 -m http.server "${PORT}" --bind "${HOST}" --directory "${WWWROOT}" >/tmp/salmonegg-wasm-smoke-http.log 2>&1 &
SERVER_PID="$!"

for _ in {1..50}; do
  if curl -fsS "${BASE_URL}index.html" >/dev/null 2>&1; then
    break
  fi

  sleep 0.2
done

curl -fsS "${BASE_URL}index.html" >/dev/null

echo "[gate] Install Playwright Chromium"
npx --yes playwright install chromium

echo "[gate] Run WASM settings navigation smoke"
xvfb-run -a npx --yes --package playwright node \
  "${REPO_ROOT}/scripts/gates/wasm-settings-navigation-smoke.mjs" \
  "${BASE_URL}"

echo "[gate] WASM smoke gates passed"
