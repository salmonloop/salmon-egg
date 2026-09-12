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
GROUPING="${SALMONEGG_NAV_MASK_GROUPING:-Status}"
SESSION_COUNT="${SALMONEGG_NAV_MASK_SESSIONS:-3}"
SKIP_BUILD="${SALMONEGG_NAV_MASK_SKIP_BUILD:-0}"
INTERACTION_PROBE="${SALMONEGG_NAV_INTERACTION_PROBE:-0}"
SEED_DIR=""

if [ "${CONFIGURATION}" != "Debug" ]; then
  echo "The navigation runtime probe requires a Debug artifact." >&2; exit 2
fi
if [ "${GROUPING}" != "Status" ] && [ "${GROUPING}" != "Project" ]; then
  echo "SALMONEGG_NAV_MASK_GROUPING must be Project or Status." >&2; exit 2
fi
if [ "${GROUPING}" = "Status" ] && [ "${SESSION_COUNT}" != "3" ]; then
  echo "The status fixture requires exactly three sessions." >&2; exit 2
fi

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
WM_PID=""
SMOKE_DISPLAY=""
PERSIST_DIR="${SALMONEGG_NAV_MASK_PERSIST_DIR:-$(mktemp -d -t salmonegg-nav-mask-result.XXXXXX)}"
mkdir -p "${PERSIST_DIR}"

cleanup() {
  for pid in "${APP_PID}" "${WM_PID}" "${XVFB_PID}"; do
    if [ -n "${pid}" ] && kill -0 "${pid}" 2>/dev/null; then
      kill "${pid}" 2>/dev/null || true
      for attempt in $(seq 1 30); do
        kill -0 "${pid}" 2>/dev/null || break
        sleep 0.1
      done
      if kill -0 "${pid}" 2>/dev/null; then kill -KILL "${pid}" 2>/dev/null || true; fi
      wait "${pid}" 2>/dev/null || true
    fi
  done
  cp -f "${BOOT_LOG}" "${PERSIST_DIR}/boot.log" 2>/dev/null || true
  cp -f "${STDOUT_LOG}" "${PERSIST_DIR}/stdout.log" 2>/dev/null || true
  cp -f "${XVFB_LOG}" "${PERSIST_DIR}/xvfb.log" 2>/dev/null || true
  cp -f "${X11_PROBE_LOG}" "${PERSIST_DIR}/x11-probe.log" 2>/dev/null || true
  [ -z "${SEED_DIR}" ] || rm -rf "${SEED_DIR}"
  rm -rf "${APPDATA_ROOT}"
  rm -f "${STDOUT_LOG}" "${XVFB_LOG}" "${X11_PROBE_LOG}"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

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

if [ "${SKIP_BUILD}" != "1" ]; then
  echo "[probe] Build Skia Desktop app"
  timeout 600 "${DOTNET_BIN}" build "${PROJECT}" -c "${CONFIGURATION}" -f net10.0-desktop \
    -p:SalmonEggTargetFrameworks=net10.0-desktop -p:SalmonEggAllTargetFrameworks=net10.0-desktop \
    -p:BuildInParallel=false -m:1 -nr:false -v minimal
fi
if [ ! -x "${APP_PATH}" ]; then echo "Missing current Desktop artifact: ${APP_PATH}" >&2; exit 2; fi
"${GIT_BIN}" -C "${REPO_ROOT}" rev-parse HEAD > "${PERSIST_DIR}/source-commit.txt"
"${GIT_BIN}" -C "${REPO_ROOT}" diff HEAD --binary | sha256sum > "${PERSIST_DIR}/source-diff.sha256"
"${GIT_BIN}" -C "${REPO_ROOT}" status --porcelain=v1 > "${PERSIST_DIR}/source-status.txt"
sha256sum "${APP_PATH}" "${APP_PATH}.dll" > "${PERSIST_DIR}/artifact.sha256"
printf '%s\n' "${APP_PATH}" > "${PERSIST_DIR}/artifact-path.txt"

echo "[probe] Seed ${SESSION_COUNT} sessions"
SEED_DIR="$(mktemp -d -t salmonegg-nav-mask-seed.XXXXXX)"
cat >"${SEED_DIR}/Seed.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
 <ItemGroup><ProjectReference Include="${SEED_WRITER_PROJECT}" /></ItemGroup>
</Project>
EOF
cat >"${SEED_DIR}/Program.cs" <<'EOF'
using SalmonEgg.TestSupport;
_ = SkiaDesktopGuiSeedWriter.WriteMultiSessionStressSeed(args[0], int.Parse(args[1]), args[2]);
EOF
timeout 120 "${DOTNET_BIN}" run --project "${SEED_DIR}/Seed.csproj" -c "${CONFIGURATION}" \
  -p:BuildInParallel=false -p:UseSharedCompilation=false --disable-build-servers \
  -- "${APPDATA_ROOT}" "${SESSION_COUNT}" "${GROUPING}" >/dev/null
rm -rf "${SEED_DIR}"
SEED_DIR=""

if [ ! -f "${APPDATA_ROOT}/conversations/conversations.v1.json" ]; then
  echo "Seed did not write conversations" >&2; exit 1
fi

if [ -z "${DISPLAY:-}" ]; then
  start_xvfb
  if [ "${INTERACTION_PROBE}" = "1" ]; then
    openbox >"${PERSIST_DIR}/window-manager.log" 2>&1 &
    WM_PID="$!"
  fi
else
  SMOKE_DISPLAY="${DISPLAY}"
fi

echo "[probe] Launch app under Xvfb (probe enabled)"
env DISPLAY="${SMOKE_DISPLAY}" SALMONEGG_GUI=1 SALMONEGG_APPDATA_ROOT="${APPDATA_ROOT}" SALMONEGG_NAV_MASK_PROBE=1 \
  SALMONEGG_NAV_INTERACTION_PROBE="${INTERACTION_PROBE}" \
  SALMONEGG_NAV_INTERACTION_EXCHANGE="${APPDATA_ROOT}/interaction" \
  "${APP_PATH}" >"${STDOUT_LOG}" 2>&1 &
APP_PID="$!"

if ! "${PYTHON_BIN}" "${X11_PROBE}" --display "${SMOKE_DISPLAY}" --pid "${APP_PID}" --timeout 25 >"${X11_PROBE_LOG}" 2>&1; then
  cat "${STDOUT_LOG}" >&2; cat "${X11_PROBE_LOG}" >&2
  [ -f "${BOOT_LOG}" ] && cat "${BOOT_LOG}" >&2
  echo "App did not expose a mapped X11 window" >&2; exit 1
fi

if [ "${INTERACTION_PROBE}" = "1" ]; then
  INTERACTION_WINDOW="$(sed -n 's/^window=\(0x[[:xdigit:]]*\).*/\1/p' "${X11_PROBE_LOG}" | head -1)"
  if [ -z "${INTERACTION_WINDOW}" ]; then echo "X11 probe did not identify the owned app window." >&2; exit 1; fi
  "${PYTHON_BIN}" "${REPO_ROOT}/scripts/gates/skia-nav-interaction-probe.py" \
    --display "${SMOKE_DISPLAY}" --pid "${APP_PID}" --window "${INTERACTION_WINDOW}" --boot-log "${BOOT_LOG}" \
    --exchange "${APPDATA_ROOT}/interaction" --artifacts "${PERSIST_DIR}"
fi

echo "[probe] Waiting for stress run completion marker"
COMPLETE_MARKER="NavMaskProbe: stress run complete"
if [ "${GROUPING}" = "Status" ]; then COMPLETE_MARKER="NavMaskProbe: status run complete"; fi
deadline=$((SECONDS + 60))
loop_done=0
while [ "${SECONDS}" -lt "${deadline}" ]; do
  if ! kill -0 "${APP_PID}" 2>/dev/null; then
    cat "${STDOUT_LOG}" >&2; [ -f "${BOOT_LOG}" ] && cat "${BOOT_LOG}" >&2
    echo "App exited mid-probe" >&2; exit 1
  fi
  if rg -Fq "${COMPLETE_MARKER}" "${BOOT_LOG}" 2>/dev/null; then
    loop_done=1; break
  fi
  sleep 0.5
done

# Give the final audit a moment to flush after the completion marker.
sleep 2

echo "[probe] Stress metadata:"
rg -a "NavMaskProbe: |NavStatusProbe " "${BOOT_LOG}" || true

echo "[probe] Runtime errors (if any):"
if rg -ai "FT_Get_BDF_Property|DllNotFoundException|EntryPointNotFoundException|Segmentation fault|Unhandled exception|App.UnhandledException|AppDomain.UnhandledException|NavMaskProbe: .*faulted" "${STDOUT_LOG}" "${BOOT_LOG}"; then
  echo "Runtime failure during navigation probe." >&2; exit 1
fi

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
"${PYTHON_BIN}" - "${BOOT_LOG}" "${GROUPING}" <<'PY'
import collections
from pathlib import Path
import re
import sys

text = Path(sys.argv[1]).read_text(errors="replace")
start_marker = "NavMaskProbe: status run started" if sys.argv[2] == "Status" else "NavMaskProbe: stress run started"
assert start_marker in text, "The stress driver never started"
stress_text = text.split(start_marker, 1)[1]
audits = re.findall(r"NavSelectionAudit realized=(\d+) selectedCount=(\d+)", stress_text)
minimum = 12 if sys.argv[2] == "Status" else 2
assert len(audits) >= minimum, f"Only {len(audits)} native samples; expected at least {minimum}"
assert all(int(realized) > 0 for realized, _ in audits), "A native sample had no realized containers"
maximum = max(int(selected) for _, selected in audits)
assert maximum <= 1, f"Found {maximum} simultaneously selected native containers"
assert any(int(selected) == 1 for _, selected in audits), "No native sample observed a selection"
if sys.argv[2] == "Status":
    steps = re.findall(r"NavStatusProbe step=(\d+) conversationId=(\S+) expectedGroup=(\S+) actualGroup=(\S+) countTotal=(\d+) unique=(\d+) selectedStable=(\S+)", text)
    assert len(steps) == 27, f"Expected 27 distinct status steps, got {len(steps)}"
    assert [int(row[0]) for row in steps] == list(range(1, 28)), "Status steps skipped or repeated"
    visited = collections.defaultdict(set)
    for _, session, expected, actual, total, unique, stable in steps:
        assert expected == actual, f"{session}: {expected} != {actual}"
        assert total == unique == "3", f"Missing or duplicated conversation rows at {session}"
        assert stable == "True", f"Selection changed while moving {session}"
        visited[session].add(actual)
    assert len(visited) == 3, "The probe did not exercise all three fixture conversations"
    assert all(groups == {"NeedsAttention", "Working", "Other"} for groups in visited.values()), visited
    initial_status_content = stress_text.split("NavInteractionProbe ready=ancestor", 1)[0]
    content_samples = re.findall(r"NavContentAudit rows=(\d+) visibleTitles=(\d+) sessions=\[([^\]]*)\]", initial_status_content)
    assert content_samples, "No native rendered-content audit was collected"
    rows, visible, sessions = content_samples[-1]
    assert rows == visible == "3", f"Final native titles are missing or transparent: {content_samples[-1]}"
    assert {entry.rsplit(":", 1)[0] for entry in sessions.split(",")} == set(visited), "Rendered titles belong to the wrong sessions"
print(f"[probe] {len(audits)} native audits; maximum selected containers={maximum}")
PY

echo "[probe] Artifacts: ${PERSIST_DIR}"

echo "[probe] Passed: the pane never showed more than one selected container."
