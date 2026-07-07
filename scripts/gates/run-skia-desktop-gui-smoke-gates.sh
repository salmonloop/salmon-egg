#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Debug}"
SCRIPT_SOURCE="${BASH_SOURCE[0]}"
SCRIPT_DIR="${SCRIPT_SOURCE%/*}"
if [ "${SCRIPT_DIR}" = "${SCRIPT_SOURCE}" ]; then
  SCRIPT_DIR="."
fi
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd -P)"
PROJECT="${REPO_ROOT}/SalmonEgg/SalmonEgg/SalmonEgg.csproj"
APP_PATH="${REPO_ROOT}/SalmonEgg/SalmonEgg/bin/${CONFIGURATION}/net10.0-desktop/SalmonEgg"
READY_MARKER="MainPage: initial shell content activated"

DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet || true)}"
GIT_BIN="${GIT_BIN:-$(command -v git || true)}"
UNAME_BIN="${UNAME_BIN:-$(command -v uname || true)}"

if [ "${CONFIGURATION}" != "Debug" ]; then
  echo "Skia Desktop GUI smoke requires Debug configuration because the XAML readiness probe is DEBUG-only boot.log output." >&2
  exit 2
fi

if [ -z "${DOTNET_BIN}" ]; then
  echo "Unable to locate dotnet in PATH." >&2
  exit 1
fi

if [ -z "${GIT_BIN}" ]; then
  echo "Unable to locate git in PATH." >&2
  exit 1
fi

if [ -z "${UNAME_BIN}" ]; then
  echo "Unable to locate uname in PATH." >&2
  exit 1
fi

OS_NAME="$("${UNAME_BIN}" -s)"
APPDATA_ROOT="$(mktemp -d -t salmonegg-skia-gui-appdata.XXXXXX)"
STDOUT_LOG="$(mktemp -t salmonegg-skia-gui-smoke.XXXXXX.log)"
BOOT_LOG="${APPDATA_ROOT}/boot.log"
APP_PID=""

cleanup() {
  if [ -n "${APP_PID}" ] && kill -0 "${APP_PID}" 2>/dev/null; then
    kill "${APP_PID}" 2>/dev/null || true
    wait "${APP_PID}" 2>/dev/null || true
  fi

  if [ -d "${APPDATA_ROOT}" ]; then
    rm -rf "${APPDATA_ROOT}"
  fi
}

trap cleanup EXIT INT TERM

case "${OS_NAME}" in
  Linux)
    XVFB_RUN_BIN="${XVFB_RUN_BIN:-$(command -v xvfb-run || true)}"
    if [ -z "${XVFB_RUN_BIN}" ]; then
      echo "Unable to locate xvfb-run in PATH. Install xvfb to run Linux Skia Desktop GUI smoke headlessly." >&2
      exit 1
    fi
    LAUNCH_COMMAND=("${XVFB_RUN_BIN}" -a env SALMONEGG_GUI=1 SALMONEGG_APPDATA_ROOT="${APPDATA_ROOT}" "${APP_PATH}")
    ;;
  Darwin)
    LAUNCH_COMMAND=(env SALMONEGG_GUI=1 SALMONEGG_APPDATA_ROOT="${APPDATA_ROOT}" "${APP_PATH}")
    ;;
  *)
    echo "Skia Desktop GUI smoke supports Linux and macOS. Use scripts/gates/run-gui-smoke-gates.ps1 for Windows WinUI/FlaUI." >&2
    exit 2
    ;;
esac

COMMIT="$("${GIT_BIN}" -C "${REPO_ROOT}" rev-parse HEAD)"

echo "[gate] Build Skia Desktop app"
"${DOTNET_BIN}" build "${PROJECT}" \
  -c "${CONFIGURATION}" \
  -f net10.0-desktop \
  -p:SalmonEggTargetFrameworks=net10.0-desktop \
  -p:SalmonEggAllTargetFrameworks=net10.0-desktop \
  -v minimal

if [ ! -x "${APP_PATH}" ]; then
  echo "Missing executable Skia Desktop artifact: ${APP_PATH}" >&2
  exit 1
fi

echo "[gate] Launch Skia Desktop GUI smoke"
echo "[gate] Runtime source commit=${COMMIT}"
echo "[gate] App artifact=${APP_PATH}"
echo "[gate] AppData root=${APPDATA_ROOT}"

"${LAUNCH_COMMAND[@]}" >"${STDOUT_LOG}" 2>&1 &
APP_PID="$!"

deadline=$((SECONDS + 35))
while [ "${SECONDS}" -lt "${deadline}" ]; do
  if ! kill -0 "${APP_PID}" 2>/dev/null; then
    cat "${STDOUT_LOG}" >&2
    if [ -f "${BOOT_LOG}" ]; then
      cat "${BOOT_LOG}" >&2
    fi
    echo "Skia Desktop GUI smoke exited before shell readiness." >&2
    exit 1
  fi

  if [ -f "${BOOT_LOG}" ] && grep -Fq "${READY_MARKER}" "${BOOT_LOG}"; then
    break
  fi

  sleep 0.2
done

if [ ! -f "${BOOT_LOG}" ] || ! grep -Fq "${READY_MARKER}" "${BOOT_LOG}"; then
  cat "${STDOUT_LOG}" >&2
  if [ -f "${BOOT_LOG}" ]; then
    cat "${BOOT_LOG}" >&2
  fi
  echo "Skia Desktop GUI smoke did not reach shell readiness marker '${READY_MARKER}'." >&2
  exit 1
fi

if grep -Eiq 'FT_Get_BDF_Property|DllNotFoundException|EntryPointNotFoundException|Segmentation fault|Unhandled exception|App.UnhandledException|AppDomain.UnhandledException' "${STDOUT_LOG}" "${BOOT_LOG}"; then
  cat "${STDOUT_LOG}" >&2
  cat "${BOOT_LOG}" >&2
  echo "Skia Desktop GUI smoke detected a native/runtime startup failure." >&2
  exit 1
fi

echo "[gate] Skia Desktop GUI smoke passed"
echo "[gate] Smoke log=${STDOUT_LOG}"
