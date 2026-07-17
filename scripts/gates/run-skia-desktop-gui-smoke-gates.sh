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
X11_PROBE="${REPO_ROOT}/scripts/gates/skia-desktop-x11-window-probe.py"
SEED_WRITER_PROJECT="${REPO_ROOT}/tests/SalmonEgg.TestSupport/SalmonEgg.TestSupport.csproj"
READY_MARKER="MainPage: initial shell content activated"
TRANSCRIPT_SEED_CONVERSATION_ID="skia-mixed-session-01"
TRANSCRIPT_SEED_MARKER="SKIA_MD_MARKER_7f3a"

DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet || true)}"
GIT_BIN="${GIT_BIN:-$(command -v git || true)}"
UNAME_BIN="${UNAME_BIN:-$(command -v uname || true)}"
PYTHON_BIN="${PYTHON_BIN:-$(command -v python3 || true)}"

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

if [ -z "${PYTHON_BIN}" ]; then
  echo "Unable to locate python3 in PATH." >&2
  exit 1
fi

OS_NAME="$("${UNAME_BIN}" -s)"
APPDATA_ROOT="$(mktemp -d -t salmonegg-skia-gui-appdata.XXXXXX)"
STDOUT_LOG="$(mktemp -t salmonegg-skia-gui-smoke.XXXXXX.log)"
XVFB_LOG="$(mktemp -t salmonegg-skia-gui-xvfb.XXXXXX.log)"
X11_PROBE_LOG="$(mktemp -t salmonegg-skia-gui-x11-probe.XXXXXX.log)"
X11_INPUT_PROBE_LOG="$(mktemp -t salmonegg-skia-gui-x11-input-probe.XXXXXX.log)"
BOOT_LOG="${APPDATA_ROOT}/boot.log"
APP_PID=""
XVFB_PID=""
SMOKE_DISPLAY="${DISPLAY:-}"

cleanup() {
  if [ -n "${APP_PID}" ] && kill -0 "${APP_PID}" 2>/dev/null; then
    kill "${APP_PID}" 2>/dev/null || true
    wait "${APP_PID}" 2>/dev/null || true
  fi

  if [ -n "${XVFB_PID}" ] && kill -0 "${XVFB_PID}" 2>/dev/null; then
    kill "${XVFB_PID}" 2>/dev/null || true
    wait "${XVFB_PID}" 2>/dev/null || true
  fi

  if [ -d "${APPDATA_ROOT}" ]; then
    rm -rf "${APPDATA_ROOT}"
  fi
}

trap cleanup EXIT INT TERM

start_xvfb() {
  local xvfb_bin="$1"
  local base_display="${SALMONEGG_SKIA_GUI_DISPLAY_BASE:-90}"
  local screen_config="${XVFB_SCREEN:-0 1920x1080x24}"

  for offset in $(seq 0 49); do
    local display_number=$((base_display + offset))
    SMOKE_DISPLAY=":${display_number}"
    "${xvfb_bin}" "${SMOKE_DISPLAY}" -screen ${screen_config} -nolisten tcp >"${XVFB_LOG}" 2>&1 &
    XVFB_PID="$!"
    sleep 0.5

    if kill -0 "${XVFB_PID}" 2>/dev/null; then
      export DISPLAY="${SMOKE_DISPLAY}"
      return 0
    fi

    wait "${XVFB_PID}" 2>/dev/null || true
    XVFB_PID=""
  done

  cat "${XVFB_LOG}" >&2
  echo "Unable to start Xvfb for Skia Desktop GUI smoke." >&2
  return 1
}

seed_mixed_transcript_appdata() {
  # Portable production AppData seed (conversations.v1.json + app.yaml). No UI hooks.
  # Seed ownership lives in SalmonEgg.TestSupport; the gate only invokes it.
  local seed_dir
  seed_dir="$(mktemp -d -t salmonegg-skia-seed-writer.XXXXXX)"
  cat >"${seed_dir}/Seed.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="${SEED_WRITER_PROJECT}" />
  </ItemGroup>
</Project>
EOF
  cat >"${seed_dir}/Program.cs" <<'EOF'
using SalmonEgg.TestSupport;
var root = args.Length > 0 ? args[0] : throw new ArgumentException("appdata root required");
_ = SkiaDesktopGuiSeedWriter.WriteMixedTranscriptSeed(root);
EOF

  if ! "${DOTNET_BIN}" run --project "${seed_dir}/Seed.csproj" -c "${CONFIGURATION}" -- "${APPDATA_ROOT}" >/dev/null; then
    rm -rf "${seed_dir}"
    echo "Skia Desktop GUI smoke failed to write mixed-transcript AppData seed." >&2
    exit 1
  fi

  rm -rf "${seed_dir}"

  if [ ! -f "${APPDATA_ROOT}/conversations/conversations.v1.json" ]; then
    echo "Skia Desktop GUI smoke seed did not write conversations.v1.json under ${APPDATA_ROOT}." >&2
    exit 1
  fi

  if ! grep -Fq "${TRANSCRIPT_SEED_MARKER}" "${APPDATA_ROOT}/conversations/conversations.v1.json"; then
    echo "Skia Desktop GUI smoke seed is missing markdown marker '${TRANSCRIPT_SEED_MARKER}'." >&2
    exit 1
  fi
}

case "${OS_NAME}" in
  Linux)
    XVFB_BIN="${XVFB_BIN:-$(command -v Xvfb || true)}"
    if [ -z "${XVFB_BIN}" ]; then
      echo "Unable to locate Xvfb in PATH. Install xvfb to run Linux Skia Desktop GUI smoke headlessly." >&2
      exit 1
    fi
    if [ -z "${SMOKE_DISPLAY}" ]; then
      start_xvfb "${XVFB_BIN}"
    fi
    LAUNCH_COMMAND=(env DISPLAY="${SMOKE_DISPLAY}" SALMONEGG_GUI=1 SALMONEGG_APPDATA_ROOT="${APPDATA_ROOT}" "${APP_PATH}")
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

echo "[gate] Seed mixed transcript AppData"
seed_mixed_transcript_appdata

echo "[gate] Launch Skia Desktop GUI smoke"
echo "[gate] Runtime source commit=${COMMIT}"
echo "[gate] App artifact=${APP_PATH}"
echo "[gate] AppData root=${APPDATA_ROOT}"
if [ -n "${SMOKE_DISPLAY}" ]; then
  echo "[gate] Display=${SMOKE_DISPLAY}"
fi

"${LAUNCH_COMMAND[@]}" >"${STDOUT_LOG}" 2>&1 &
APP_PID="$!"

if [ "${OS_NAME}" = "Linux" ]; then
  if ! "${PYTHON_BIN}" "${X11_PROBE}" \
      --display "${SMOKE_DISPLAY}" \
      --pid "${APP_PID}" \
      --timeout 20 \
      >"${X11_PROBE_LOG}" 2>&1; then
    cat "${STDOUT_LOG}" >&2
    cat "${X11_PROBE_LOG}" >&2
    if [ -f "${BOOT_LOG}" ]; then
      cat "${BOOT_LOG}" >&2
    fi
    echo "Skia Desktop GUI smoke did not expose a mapped, nonblank X11 window." >&2
    exit 1
  fi
fi

deadline=$((SECONDS + 45))
shell_ready=0
transcript_ready=0
while [ "${SECONDS}" -lt "${deadline}" ]; do
  if ! kill -0 "${APP_PID}" 2>/dev/null; then
    cat "${STDOUT_LOG}" >&2
    if [ -f "${BOOT_LOG}" ]; then
      cat "${BOOT_LOG}" >&2
    fi
    echo "Skia Desktop GUI smoke exited before shell readiness." >&2
    exit 1
  fi

  if [ -f "${BOOT_LOG}" ]; then
    if [ "${shell_ready}" -eq 0 ] && grep -Fq "${READY_MARKER}" "${BOOT_LOG}"; then
      shell_ready=1
    fi

    if [ "${transcript_ready}" -eq 0 ] \
      && grep -Eq "ChatTranscript: projected conversation=${TRANSCRIPT_SEED_CONVERSATION_ID} count=[1-9][0-9]* history=[1-9][0-9]*" "${BOOT_LOG}"; then
      transcript_ready=1
    fi
  fi

  if [ "${shell_ready}" -eq 1 ] && [ "${transcript_ready}" -eq 1 ]; then
    break
  fi

  sleep 0.2
done

if [ "${shell_ready}" -ne 1 ]; then
  cat "${STDOUT_LOG}" >&2
  if [ -f "${BOOT_LOG}" ]; then
    cat "${BOOT_LOG}" >&2
  fi
  echo "Skia Desktop GUI smoke did not reach shell readiness marker '${READY_MARKER}'." >&2
  exit 1
fi

if [ "${transcript_ready}" -ne 1 ]; then
  cat "${STDOUT_LOG}" >&2
  if [ -f "${BOOT_LOG}" ]; then
    cat "${BOOT_LOG}" >&2
  fi
  echo "Skia Desktop GUI smoke did not project seeded mixed transcript '${TRANSCRIPT_SEED_CONVERSATION_ID}'." >&2
  exit 1
fi

if grep -Eiq 'FT_Get_BDF_Property|DllNotFoundException|EntryPointNotFoundException|Segmentation fault|Unhandled exception|App.UnhandledException|AppDomain.UnhandledException' "${STDOUT_LOG}" "${BOOT_LOG}"; then
  cat "${STDOUT_LOG}" >&2
  cat "${BOOT_LOG}" >&2
  echo "Skia Desktop GUI smoke detected a native/runtime startup failure." >&2
  exit 1
fi

if [ "${OS_NAME}" = "Linux" ]; then
  if ! "${PYTHON_BIN}" "${X11_PROBE}" \
      --display "${SMOKE_DISPLAY}" \
      --pid "${APP_PID}" \
      --timeout 10 \
      --require-focus-input \
      >"${X11_INPUT_PROBE_LOG}" 2>&1; then
    cat "${STDOUT_LOG}" >&2
    cat "${X11_INPUT_PROBE_LOG}" >&2
    cat "${BOOT_LOG}" >&2
    echo "Skia Desktop GUI smoke did not expose a focusable X11 window that accepts synthetic keyboard input." >&2
    exit 1
  fi
fi

echo "[gate] Skia Desktop GUI smoke passed"
echo "[gate] Smoke log=${STDOUT_LOG}"
if [ "${OS_NAME}" = "Linux" ]; then
  echo "[gate] X11 probe log=${X11_PROBE_LOG}"
  echo "[gate] X11 input probe log=${X11_INPUT_PROBE_LOG}"
fi
