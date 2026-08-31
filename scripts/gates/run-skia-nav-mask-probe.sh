#!/usr/bin/env bash
set -euo pipefail

# Decisive runtime probe for the left-nav multi-mask defect.
# Seeds N sessions, launches the Skia desktop app under Xvfb with
# SALMONEGG_NAV_MASK_PROBE=1 so the DEBUG self-driven stress loop cycles ActivateSessionAsync
# across sessions and audits the realized NavigationViewItem tree (IsSelected counts) into
# boot.log after every round + after idle. Then asserts the audit observations.

CONFIGURATION="${1:-Debug}"
SCRIPT_SOURCE="${BASH_SOURCE[0]}"
SCRIPT_DIR="${SCRIPT_SOURCE%/*}"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd -P)"

PROJECT="${REPO_ROOT}/SalmonEgg/SalmonEgg/SalmonEgg.csproj"
APP_PATH="${REPO_ROOT}/SalmonEgg/SalmonEgg/bin/${CONFIGURATION}/net10.0-desktop/SalmonEgg"
SEED_WRITER_PROJECT="${REPO_ROOT}/tests/SalmonEgg.TestSupport/SalmonEgg.TestSupport.csproj"
X11_PROBE="${REPO_ROOT}/scripts/gates/skia-desktop-x11-window-probe.py"

DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet || true)}"
PYTHON_BIN="${PYTHON_BIN:-$(command -v python3 || true)}"
GIT_BIN="${GIT_BIN:-$(command -v git || true)}"
SESSION_COUNT="${SALMONEGG_NAV_MASK_SESSIONS:-6}"

if [ -z "${DOTNET_BIN}" ] || [ -z "${PYTHON_BIN}" ] || [ -z "${GIT_BIN}" ]; then
  echo "Missing dotnet/python3/git" >&2; exit 1
fi

APPDATA_ROOT="$(mktemp -d -t salmonegg-nav-mask-probe.XXXXXX)"
STDOUT_LOG="$(mktemp -t salmonegg-nav-mask-stdout.XXXXXX.log)"
XVFB_LOG="$(mktemp -t salmonegg-nav-mask-xvfb.XXXXXX.log)"
X11_PROBE_LOG="$(mktemp -t salmonegg-nav-mask-x11.XXXXXX.log)"
BOOT_LOG="${APPDATA_ROOT}/boot.log"
APP_PID=""
XVFB_PID=""
SMOKE_DISPLAY=""

cleanup() {
  [ -n "${APP_PID}" ] && kill -0 "${APP_PID}" 2>/dev/null && { kill "${APP_PID}" 2>/dev/null || true; wait "${APP_PID}" 2>/dev/null || true; }
  [ -n "${XVFB_PID}" ] && kill -0 "${XVFB_PID}" 2>/dev/null && { kill "${XVFB_PID}" 2>/dev/null || true; wait "${XVFB_PID}" 2>/dev/null || true; }
  # Preserve AppData/boot.log for offline analysis; only the temp stdout/xvfb logs are dropped.
}
trap cleanup EXIT INT TERM
PERSIST_DIR="${SALMONEGG_NAV_MASK_PERSIST_DIR:-$(mktemp -d -t salmonegg-nav-mask-result.XXXXXX)}"
mkdir -p "${PERSIST_DIR}"

start_xvfb() {
  for offset in $(seq 0 49); do
    SMOKE_DISPLAY=":$((${SALMONEGG_SKIA_GUI_DISPLAY_BASE:-90} + offset))"
    Xvfb "${SMOKE_DISPLAY}" -screen 0 1920x1080x24 -nolisten tcp >"${XVFB_LOG}" 2>&1 &
    XVFB_PID="$!"; sleep 0.5
    if kill -0 "${XVFB_PID}" 2>/dev/null; then export DISPLAY="${SMOKE_DISPLAY}"; return 0; fi
    wait "${XVFB_PID}" 2>/dev/null || true; XVFB_PID=""
  done
  echo "Unable to start Xvfb" >&2; return 1
}

if [ -z "${DISPLAY:-}" ]; then start_xvfb; else SMOKE_DISPLAY="${DISPLAY}"; fi

echo "[probe] Build Skia Desktop app"
"${DOTNET_BIN}" build "${PROJECT}" -c "${CONFIGURATION}" -f net10.0-desktop \
  -p:SalmonEggTargetFrameworks=net10.0-desktop -p:SalmonEggAllTargetFrameworks=net10.0-desktop -v minimal

echo "[probe] Seed ${SESSION_COUNT} sessions"
seed_dir="$(mktemp -d -t salmonegg-nav-mask-seed.XXXXXX)"
cat >"${seed_dir}/Seed.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
 <ItemGroup><ProjectReference Include="${SEED_WRITER_PROJECT}" /></ItemGroup>
</Project>
EOF
cat >"${seed_dir}/Program.cs" <<EOF
using SalmonEgg.TestSupport;
_ = SkiaDesktopGuiSeedWriter.WriteMultiSessionStressSeed("${APPDATA_ROOT}", ${SESSION_COUNT});
EOF
"${DOTNET_BIN}" run --project "${seed_dir}/Seed.csproj" -c "${CONFIGURATION}" >/dev/null
rm -rf "${seed_dir}"

if [ ! -f "${APPDATA_ROOT}/conversations/conversations.v1.json" ]; then
  echo "Seed did not write conversations" >&2; exit 1
fi

echo "[probe] Launch app under Xvfb (probe enabled)"
env DISPLAY="${SMOKE_DISPLAY}" SALMONEGG_GUI=1 SALMONEGG_APPDATA_ROOT="${APPDATA_ROOT}" SALMONEGG_NAV_MASK_PROBE=1 \
  "${APP_PATH}" >"${STDOUT_LOG}" 2>&1 &
APP_PID="$!"

if ! "${PYTHON_BIN}" "${X11_PROBE}" --display "${SMOKE_DISPLAY}" --pid "${APP_PID}" --timeout 25 >"${X11_PROBE_LOG}" 2>&1; then
  cat "${STDOUT_LOG}" >&2; cat "${X11_PROBE_LOG}" >&2
  [ -f "${BOOT_LOG}" ] && cat "${BOOT_LOG}" >&2
  echo "App did not expose a mapped X11 window" >&2; exit 1
fi

echo "[probe] Waiting for stress run completion marker"
deadline=$((SECONDS + 60))
loop_done=0
while [ "${SECONDS}" -lt "${deadline}" ]; do
  if ! kill -0 "${APP_PID}" 2>/dev/null; then
    cat "${STDOUT_LOG}" >&2; [ -f "${BOOT_LOG}" ] && cat "${BOOT_LOG}" >&2
    echo "App exited mid-probe" >&2; exit 1
  fi
  if grep -Fq "NavMaskProbe: stress run complete" "${BOOT_LOG}" 2>/dev/null; then
    loop_done=1; break
  fi
  sleep 0.5
done

# Give the final audit a moment to flush after the completion marker.
sleep 2

echo "[probe] Stress metadata:"
grep -aE "NavMaskProbe: " "${BOOT_LOG}" || true

echo "[probe] Runtime errors (if any):"
grep -aEi "FT_Get_BDF_Property|DllNotFoundException|EntryPointNotFoundException|Segmentation fault|Unhandled exception|App.UnhandledException|AppDomain.UnhandledException" "${STDOUT_LOG}" "${BOOT_LOG}" || echo "(none)"

if [ "${loop_done}" -ne 1 ]; then
  echo "[probe] Stress loop did not finish within timeout" >&2; exit 2
fi

# Persist artifacts for offline analysis.
cp -f "${BOOT_LOG}" "${PERSIST_DIR}/boot.log" 2>/dev/null || true
cp -f "${STDOUT_LOG}" "${PERSIST_DIR}/stdout.log" 2>/dev/null || true
cp -f "${X11_PROBE_LOG}" "${PERSIST_DIR}/x11-probe.log" 2>/dev/null || true

echo "${PERSIST_DIR}" > /tmp/salmonegg-nav-mask-latest-result.txt

# NavigationView guarantees at most one selection indicator across the whole pane, so more than
# one selected container at any observed instant is the stranded-mask defect.
audit_count=$(grep -acE "NavSelectionAudit " "${BOOT_LOG}" || true)
max_selected=$(grep -aoE "selectedCount=[0-9]+" "${BOOT_LOG}" | sed -E 's/selectedCount=//' | sort -n | tail -1)
max_selected="${max_selected:-0}"

echo "[probe] Audits observed: ${audit_count}"
echo "[probe] Max simultaneously selected containers: ${max_selected}"
echo "[probe] Artifacts: ${PERSIST_DIR}"

if [ "${audit_count}" -lt 2 ]; then
  echo "Probe collected ${audit_count} audit(s); the stress run did not exercise the pane." >&2
  exit 2
fi

if [ "${max_selected}" -gt 1 ]; then
  echo "[probe] Stranded selection masks detected:" >&2
  grep -aE "NavSelectionAudit " "${BOOT_LOG}" | grep -aE "selectedCount=[2-9]" | head -5 >&2
  echo "Left navigation showed ${max_selected} selected containers at once (expected at most 1)." >&2
  exit 1
fi

echo "[probe] Passed: the pane never showed more than one selected container."
