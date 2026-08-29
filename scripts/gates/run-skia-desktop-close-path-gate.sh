#!/usr/bin/env bash
# Gate for the window-close path (issue #126). The GUI smoke gates only ever SIGTERM the app,
# which is why a multi-second block between "close clicked" and "process exited" went unnoticed:
# SIGTERM is not the close button. This gate sends a real WM_DELETE_WINDOW (exactly what a titlebar
# X produces), then hard-asserts:
#   1. close -> exit within CLOSE_BUDGET_SECONDS,
#   2. exit code 0 (teardown, not a crash),
#   3. every child process recorded before the close is gone after exit (no leak reparented to init).
# The appdata is seeded with telemetry pointed at a blackhole address, which is the worst case:
# a waiting telemetry shutdown used to block the close path for ~10s there.
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
CLOSE_SENDER="${REPO_ROOT}/scripts/gates/skia-desktop-x11-close-sender.py"
SEED_WRITER_PROJECT="${REPO_ROOT}/tests/SalmonEgg.TestSupport/SalmonEgg.TestSupport.csproj"
READY_MARKER="MainPage: initial shell content activated"
READY_TIMEOUT_SECONDS=120
# Budget rationale: fixed code closes in 0.7-2.6s under the blackhole endpoint; the blocking
# regression measured 5.5s (one provider) up to ~10s (serial). 4s separates both with margin.
# The wait loop uses nanosecond timestamps on purpose: SECONDS is whole-second granularity and
# already let a 5.55s regression slip through a "5s" budget once.
CLOSE_BUDGET_SECONDS=4
CHILD_EXIT_GRACE_SECONDS=5
# TEST-NET-1: routed-to-nowhere, so a synchronous OTLP export stalls instead of failing fast.
BLACKHOLE_ENDPOINT="http://192.0.2.1:4318"

DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet || true)}"
PYTHON_BIN="${PYTHON_BIN:-$(command -v python3 || true)}"

if [ "${CONFIGURATION}" != "Debug" ]; then
  echo "Close-path gate requires Debug configuration because the readiness marker is DEBUG-only boot.log output." >&2
  exit 2
fi

if [ -z "${DOTNET_BIN}" ]; then
  echo "Unable to locate dotnet in PATH." >&2
  exit 1
fi

if [ -z "${PYTHON_BIN}" ]; then
  echo "Unable to locate python3 in PATH." >&2
  exit 1
fi

OS_NAME="$(uname -s)"
if [ "${OS_NAME}" != "Linux" ]; then
  echo "Close-path gate covers the X11 close path; run it on Linux." >&2
  exit 2
fi

XVFB_BIN="${XVFB_BIN:-$(command -v Xvfb || true)}"
if [ -z "${XVFB_BIN}" ]; then
  echo "Unable to locate Xvfb in PATH. Install xvfb to run the close-path gate headlessly." >&2
  exit 1
fi

APPDATA_ROOT="$(mktemp -d -t salmonegg-close-path-appdata.XXXXXX)"
STDOUT_LOG="$(mktemp -t salmonegg-close-path.XXXXXX.log)"
XVFB_LOG="$(mktemp -t salmonegg-close-path-xvfb.XXXXXX.log)"
CLOSE_SENDER_LOG="$(mktemp -t salmonegg-close-path-sender.XXXXXX.log)"
CHILDREN_FILE="$(mktemp -t salmonegg-close-path-children.XXXXXX.log)"
BOOT_LOG="${APPDATA_ROOT}/boot.log"
APP_PID=""
XVFB_PID=""
DISPLAY_NUMBER="${SALMONEGG_CLOSE_PATH_DISPLAY_BASE:-140}"

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

fail() {
  local message="$1"
  cat "${STDOUT_LOG}" >&2
  if [ -f "${BOOT_LOG}" ]; then
    cat "${BOOT_LOG}" >&2
  fi
  echo "[close-path] ${message}" >&2
  exit 1
}

start_xvfb() {
  for offset in $(seq 0 49); do
    local display_number=$((DISPLAY_NUMBER + offset))
    CLOSE_DISPLAY=":${display_number}"
    "${XVFB_BIN}" "${CLOSE_DISPLAY}" -screen 0 1920x1080x24 -nolisten tcp >"${XVFB_LOG}" 2>&1 &
    XVFB_PID="$!"
    sleep 0.5

    if kill -0 "${XVFB_PID}" 2>/dev/null; then
      return 0
    fi

    wait "${XVFB_PID}" 2>/dev/null || true
    XVFB_PID=""
  done

  cat "${XVFB_LOG}" >&2
  echo "Unable to start Xvfb for the close-path gate." >&2
  return 1
}

# Recursive descendants via /proc/<pid>/task/<tid>/children — no ps dependency games, and
# grandchildren are included, which matters because npm-style launchers put the real agent
# one level below the process the app spawned.
snapshot_descendants() {
  local pid="$1"
  local output="$2"
  : >"${output}"
  local queue=("${pid}")
  while [ "${#queue[@]}" -gt 0 ]; do
    local current="${queue[0]}"
    queue=("${queue[@]:1}")
    local children_file
    for children_file in /proc/"${current}"/task/*/children; do
      [ -f "${children_file}" ] || continue
      for child in $(cat "${children_file}"); do
        echo "${child}" >>"${output}"
        queue+=("${child}")
      done
    done
  done
  sort -u -o "${output}" "${output}"
}

seed_appdata() {
  local seed_dir
  seed_dir="$(mktemp -d -t salmonegg-close-path-seed.XXXXXX)"
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
    echo "Close-path gate failed to write the AppData seed." >&2
    exit 1
  fi

  rm -rf "${seed_dir}"

  # Force the worst case the issue reported: telemetry enabled and exporting into a blackhole.
  # A shutdown that waits on export used to stall the close path for ~10s here.
  # Keys follow the yaml store's UnderscoredNamingConvention (AppSettingsYamlV1.TelemetrySharingEnabled
  # serializes as telemetry_sharing_enabled) — PascalCase here would be silently ignored.
  if ! grep -Eq "^telemetry_sharing_enabled:[[:space:]]+true$" "${APPDATA_ROOT}/config/app.yaml"; then
    printf '\ntelemetry_sharing_enabled: true\n' >>"${APPDATA_ROOT}/config/app.yaml"
  fi
  printf 'telemetry_custom_endpoint: %s\n' "${BLACKHOLE_ENDPOINT}" >>"${APPDATA_ROOT}/config/app.yaml"

  if ! grep -Fq "telemetry_custom_endpoint: ${BLACKHOLE_ENDPOINT}" "${APPDATA_ROOT}/config/app.yaml"; then
    echo "Close-path gate seed is missing the blackhole telemetry endpoint." >&2
    exit 1
  fi
}

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

echo "[gate] Seed appdata with blackhole telemetry endpoint"
seed_appdata

echo "[gate] Launch Skia Desktop app under Xvfb"
start_xvfb
env DISPLAY="${CLOSE_DISPLAY}" SALMONEGG_GUI=1 SALMONEGG_APPDATA_ROOT="${APPDATA_ROOT}" "${APP_PATH}" >"${STDOUT_LOG}" 2>&1 &
APP_PID="$!"
echo "[close-path] app pid=${APP_PID} display=${CLOSE_DISPLAY}"

ready_deadline=$((SECONDS + READY_TIMEOUT_SECONDS))
while [ "${SECONDS}" -lt "${ready_deadline}" ]; do
  kill -0 "${APP_PID}" 2>/dev/null || fail "App exited before reaching '${READY_MARKER}'."
  if [ -f "${BOOT_LOG}" ] && grep -Fq "${READY_MARKER}" "${BOOT_LOG}"; then
    break
  fi
  sleep 0.2
done
if ! grep -Fq "${READY_MARKER}" "${BOOT_LOG}" 2>/dev/null; then
  fail "App did not log '${READY_MARKER}' within ${READY_TIMEOUT_SECONDS}s."
fi

snapshot_descendants "${APP_PID}" "${CHILDREN_FILE}"
descendant_count="$(grep -c . "${CHILDREN_FILE}" || true)"
echo "[close-path] descendants recorded before close: ${descendant_count}"

echo "[gate] Send WM_DELETE_WINDOW and measure close -> exit"
if ! "${PYTHON_BIN}" "${CLOSE_SENDER}" \
    --display "${CLOSE_DISPLAY}" \
    --pid "${APP_PID}" \
    --timeout 20 \
    >"${CLOSE_SENDER_LOG}" 2>&1; then
  cat "${CLOSE_SENDER_LOG}" >&2
  fail "Failed to deliver WM_DELETE_WINDOW to the app window."
fi
cat "${CLOSE_SENDER_LOG}"

close_start="$(date +%s%N)"
deadline_ns=$((close_start + CLOSE_BUDGET_SECONDS * 1000000000))
app_exited=0
while [ "$(date +%s%N)" -le "${deadline_ns}" ]; do
  if ! kill -0 "${APP_PID}" 2>/dev/null; then
    app_exited=1
    break
  fi
  sleep 0.05
done

if [ "${app_exited}" -ne 1 ]; then
  fail "App still alive ${CLOSE_BUDGET_SECONDS}s after WM_DELETE_WINDOW (issue #126 regression)."
fi

exit_code=0
# 不写 `if ! wait`：取反会把非零状态翻成零，退出码断言就永远假绿。
wait "${APP_PID}" || exit_code=$?
close_elapsed="$(awk -v s="${close_start}" -v e="$(date +%s%N)" 'BEGIN { printf "%.2f", (e - s) / 1000000000 }')"
echo "[close-path] app exited after ${close_elapsed}s, exit code ${exit_code}"

if [ "${exit_code}" -ne 0 ]; then
  fail "App exited with code ${exit_code}; teardown must be a clean exit, not a crash."
fi

echo "[gate] Assert no descendant survived the exit"
leaked=0
child_grace_deadline=$((SECONDS + CHILD_EXIT_GRACE_SECONDS))
while :; do
  leaked=0
  while IFS= read -r child; do
    [ -n "${child}" ] || continue
    if kill -0 "${child}" 2>/dev/null; then
      leaked=$((leaked + 1))
      echo "[close-path] descendant still alive after app exit: pid=${child} cmd=$(tr '\0' ' ' < /proc/"${child}"/cmdline 2>/dev/null || echo '?')"
    fi
  done <"${CHILDREN_FILE}"
  if [ "${leaked}" -eq 0 ]; then
    break
  fi
  if [ "${SECONDS}" -ge "${child_grace_deadline}" ]; then
    break
  fi
  sleep 0.2
done

if [ "${leaked}" -ne 0 ]; then
  fail "${leaked} descendant process(es) survived the app exit (leak regression)."
fi

if grep -Eiq 'Segmentation fault|Unhandled exception|App.UnhandledException|AppDomain.UnhandledException' "${STDOUT_LOG}" "${BOOT_LOG}"; then
  fail "Runtime failure signature found in the app logs."
fi

echo "[gate] Skia Desktop close-path gate passed (${close_elapsed}s close -> exit, code 0, no surviving descendants)"
