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
WINDOW_CREATED_MARKER="OnLaunched: window created"
WINDOW_CREATED_TIMEOUT_SECONDS=120
TRANSCRIPT_SEED_CONVERSATION_ID="skia-mixed-session-01"
TRANSCRIPT_SEED_MARKER="SKIA_MD_MARKER_7f3a"
NUMBERBOX_PROBE_COMPLETE_MARKER="NumberBoxThemeProbe: complete"

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

# Waits for a boot.log marker, failing fast if the app exits first. Startup duration and window
# paint latency are separate facts: a shared deadline lets slow startup consume the paint budget and
# report as "window never painted", which is the wrong diagnosis and the wrong thing to fix.
wait_for_boot_marker() {
  local marker="$1"
  local timeout_seconds="$2"
  local deadline=$((SECONDS + timeout_seconds))

  while [ "${SECONDS}" -lt "${deadline}" ]; do
    if ! kill -0 "${APP_PID}" 2>/dev/null; then
      return 2
    fi

    if [ -f "${BOOT_LOG}" ] && grep -Fq "${marker}" "${BOOT_LOG}"; then
      return 0
    fi

    sleep 0.2
  done

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

  if ! grep -Eq '^theme:[[:space:]]+Dark$' "${APPDATA_ROOT}/config/app.yaml"; then
    echo "Skia Desktop GUI smoke seed did not force the dark theme required by the NumberBox contrast probe." >&2
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
    LAUNCH_COMMAND=(env DISPLAY="${SMOKE_DISPLAY}" SALMONEGG_GUI=1 SALMONEGG_NUMBERBOX_THEME_PROBE=1 SALMONEGG_APPDATA_ROOT="${APPDATA_ROOT}" "${APP_PATH}")
    ;;
  Darwin)
    LAUNCH_COMMAND=(env SALMONEGG_GUI=1 SALMONEGG_NUMBERBOX_THEME_PROBE=1 SALMONEGG_APPDATA_ROOT="${APPDATA_ROOT}" "${APP_PATH}")
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
  # A hosted runner has been measured taking 15s from process start to window creation, which left
  # 5s of a shared 20s budget for map and paint and failed with distinctPixels=1 on an app that went
  # on to render correctly. Spend the startup wait here so the probe budget measures only the paint.
  wait_for_boot_marker "${WINDOW_CREATED_MARKER}" "${WINDOW_CREATED_TIMEOUT_SECONDS}"
  window_created_status="$?"
  if [ "${window_created_status}" -ne 0 ]; then
    cat "${STDOUT_LOG}" >&2
    if [ -f "${BOOT_LOG}" ]; then
      cat "${BOOT_LOG}" >&2
    fi
    if [ "${window_created_status}" -eq 2 ]; then
      echo "Skia Desktop GUI smoke exited before creating its window." >&2
    else
      echo "Skia Desktop GUI smoke did not log '${WINDOW_CREATED_MARKER}' within ${WINDOW_CREATED_TIMEOUT_SECONDS}s." >&2
    fi
    exit 1
  fi

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

deadline=$((SECONDS + 60))
shell_ready=0
transcript_ready=0
numberbox_probe_complete=0
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

    if [ "${numberbox_probe_complete}" -eq 0 ] \
      && grep -Fq "${NUMBERBOX_PROBE_COMPLETE_MARKER}" "${BOOT_LOG}"; then
      numberbox_probe_complete=1
    fi
  fi

  if [ "${shell_ready}" -eq 1 ] \
    && [ "${transcript_ready}" -eq 1 ] \
    && [ "${numberbox_probe_complete}" -eq 1 ]; then
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

if [ "${numberbox_probe_complete}" -ne 1 ]; then
  cat "${STDOUT_LOG}" >&2
  if [ -f "${BOOT_LOG}" ]; then
    cat "${BOOT_LOG}" >&2
  fi
  echo "Skia Desktop GUI smoke did not complete the focused NumberBox theme probe." >&2
  exit 1
fi

numberbox_sample_count="$(grep -acE 'NumberBoxThemeProbe: sample=[0-9]+' "${BOOT_LOG}" || true)"
if [ "${numberbox_sample_count}" -lt 3 ]; then
  cat "${BOOT_LOG}" >&2
  echo "Skia Desktop GUI smoke collected only ${numberbox_sample_count} focused NumberBox sample(s); expected at least 3." >&2
  exit 1
fi

if ! awk '
  /NumberBoxThemeProbe: sample=/ {
    samples++
    visible = focusTransition = focused = numberBoxTheme = inputTheme = contentTheme = borderTheme = passed = ""
    contrast = -1
    for (fieldIndex = 1; fieldIndex <= NF; fieldIndex++) {
      split($fieldIndex, field, "=")
      if (field[1] == "visible") visible = field[2]
      else if (field[1] == "focusTransition") focusTransition = field[2]
      else if (field[1] == "focused") focused = field[2]
      else if (field[1] == "numberBoxTheme") numberBoxTheme = field[2]
      else if (field[1] == "inputTheme") inputTheme = field[2]
      else if (field[1] == "contentTheme") contentTheme = field[2]
      else if (field[1] == "borderTheme") borderTheme = field[2]
      else if (field[1] == "contrast") contrast = field[2] + 0
      else if (field[1] == "passed") passed = field[2]
    }
    if (visible != "True" ||
        focusTransition != "True" ||
        focused != "True" ||
        numberBoxTheme != "Dark" ||
        inputTheme != "Dark" ||
        contentTheme != "Dark" ||
        borderTheme != "Dark" ||
        contrast < 4.5 ||
        passed != "True") {
      failures++
    }
  }
  END { exit !(samples >= 3 && failures == 0) }
' "${BOOT_LOG}"; then
  cat "${BOOT_LOG}" >&2
  echo "Skia Desktop GUI smoke observed an unreadable or theme-mismatched focused NumberBox sample." >&2
  exit 1
fi

if ! grep -Eq 'NumberBoxThemeProbe: complete samples=3 valueUnchanged=True passed=True reason=none' "${BOOT_LOG}"; then
  cat "${BOOT_LOG}" >&2
  echo "Skia Desktop GUI smoke NumberBox probe did not finish with three passing, non-mutating samples." >&2
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
