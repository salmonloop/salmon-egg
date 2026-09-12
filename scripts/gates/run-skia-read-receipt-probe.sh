#!/usr/bin/env bash
set -euo pipefail

# Uses the current Debug artifact and the ordinary production conversation seed.
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
APP_PATH="${SALMONEGG_READ_RECEIPT_APP_PATH:-${REPO_ROOT}/SalmonEgg/SalmonEgg/bin/Debug/net10.0-desktop/SalmonEgg}"
RESULT_DIR="${SALMONEGG_READ_RECEIPT_RESULT_DIR:-$(mktemp -d -t salmonegg-read-receipt-result.XXXXXX)}"
APPDATA_ROOT="$(mktemp -d -t salmonegg-read-receipt-appdata.XXXXXX)"
SEED_DIR="$(mktemp -d -t salmonegg-read-receipt-seed.XXXXXX)"
APP_PID=""
XVFB_PID=""
WM_PID=""
mkdir -p "${RESULT_DIR}"

cleanup() {
    for pid in "${APP_PID}" "${WM_PID}" "${XVFB_PID}"; do
        if [ -n "${pid}" ] && kill -0 "${pid}" 2>/dev/null; then
            kill "${pid}" 2>/dev/null || true
            for attempt in $(seq 1 30); do
                kill -0 "${pid}" 2>/dev/null || break
                sleep 0.1
            done
            kill -KILL "${pid}" 2>/dev/null || true
            wait "${pid}" 2>/dev/null || true
        fi
    done
    cp "${APPDATA_ROOT}/boot.log" "${RESULT_DIR}/boot.log" 2>/dev/null || true
    rm -rf "${APPDATA_ROOT}" "${SEED_DIR}"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

test -x "${APP_PATH}" || { echo "Missing current Debug artifact: ${APP_PATH}" >&2; exit 2; }
for tool in dotnet python3 xdotool xprop Xvfb openbox rg; do command -v "${tool}" >/dev/null; done
git -C "${REPO_ROOT}" rev-parse HEAD > "${RESULT_DIR}/source-commit.txt"
git -C "${REPO_ROOT}" diff HEAD --binary | sha256sum > "${RESULT_DIR}/source-diff.sha256"
git -C "${REPO_ROOT}" status --porcelain=v1 > "${RESULT_DIR}/source-status.txt"
sha256sum "${APP_PATH}" "${APP_PATH}.dll" > "${RESULT_DIR}/artifact.sha256"
printf '%s\n' "${APP_PATH}" > "${RESULT_DIR}/artifact-path.txt"

cat > "${SEED_DIR}/Seed.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="${REPO_ROOT}/tests/SalmonEgg.TestSupport/SalmonEgg.TestSupport.csproj" /></ItemGroup>
</Project>
EOF
cat > "${SEED_DIR}/Program.cs" <<'EOF'
using SalmonEgg.TestSupport;
SkiaDesktopGuiSeedWriter.WriteMultiSessionStressSeed(args[0], 2, "Project");
EOF
timeout 120 dotnet run --project "${SEED_DIR}/Seed.csproj" -p:BuildInParallel=false -p:UseSharedCompilation=false \
    --disable-build-servers -- "${APPDATA_ROOT}" > "${RESULT_DIR}/seed.log" 2>&1

if [ -z "${DISPLAY:-}" ]; then
    for number in $(seq 130 149); do
        [ ! -S "/tmp/.X11-unix/X${number}" ] || continue
        export DISPLAY=":${number}"
        Xvfb "${DISPLAY}" -screen 0 1600x1000x24 -nolisten tcp > "${RESULT_DIR}/xvfb.log" 2>&1 &
        XVFB_PID="$!"
        deadline=$((SECONDS + 5))
        while [ ! -S "/tmp/.X11-unix/X${number}" ] && [ "${SECONDS}" -lt "${deadline}" ]; do
            kill -0 "${XVFB_PID}" 2>/dev/null || break
            sleep 0.1
        done
        if [ -S "/tmp/.X11-unix/X${number}" ] && kill -0 "${XVFB_PID}" 2>/dev/null; then break; fi
        wait "${XVFB_PID}" 2>/dev/null || true
        XVFB_PID=""
    done
    test -n "${XVFB_PID}" || { echo "Could not start isolated virtual display" >&2; exit 2; }
    openbox > "${RESULT_DIR}/openbox.log" 2>&1 &
    WM_PID="$!"
fi

env SALMONEGG_GUI=1 SALMONEGG_APPDATA_ROOT="${APPDATA_ROOT}" SALMONEGG_READ_RECEIPT_PROBE=1 \
    "${APP_PATH}" > "${RESULT_DIR}/stdout.log" 2>&1 &
APP_PID="$!"
printf '%s\n' "${APP_PID}" > "${RESULT_DIR}/app-pid.txt"
timeout 25 python3 "${REPO_ROOT}/scripts/gates/skia-desktop-x11-window-probe.py" \
    --display "${DISPLAY}" --pid "${APP_PID}" --timeout 20 > "${RESULT_DIR}/window.log" 2>&1
window="$(python3 - "${RESULT_DIR}/window.log" <<'PY'
from pathlib import Path
import re
import sys

match = re.search(r"\bwindow=(0x[0-9a-fA-F]+)\b", Path(sys.argv[1]).read_text(errors="replace"))
if match is None:
    raise SystemExit("The X11 probe succeeded but did not report its confirmed window ID; inspect window.log.")
print(match[1])
PY
)"
if ! timeout 10 xdotool windowactivate --sync "${window}"; then
    echo "Could not activate the X11 probe's window ${window}; inspect ${RESULT_DIR}/window.log and openbox.log." >&2
    exit 1
fi

python3 - "${APP_PID}" "${window}" "${APPDATA_ROOT}/boot.log" "${RESULT_DIR}" <<'PY'
import os
from pathlib import Path
import subprocess
import sys
import time

pid, window, log, results = int(sys.argv[1]), sys.argv[2], Path(sys.argv[3]), Path(sys.argv[4])
deadline = time.monotonic() + 65

def command(*args):
    return subprocess.check_output(args, text=True, timeout=5).strip()

def wait_for(marker):
    while time.monotonic() < deadline:
        os.kill(pid, 0)
        text = log.read_text(errors="replace") if log.exists() else ""
        if "ReadReceiptProbe: faulted" in text:
            raise AssertionError("The native read probe failed; inspect boot.log")
        if marker in text:
            return
        time.sleep(0.05)
    raise TimeoutError(f"Missing marker: {marker}")

wait_for("ReadReceiptProbe ready=minimize")
command("xdotool", "windowminimize", "--sync", window)
state = command("xprop", "-id", window, "_NET_WM_STATE")
(results / "minimized-window-state.txt").write_text(state + "\n")
assert "_NET_WM_STATE_HIDDEN" in state, f"Window was not really minimized: {state}"
wait_for("ReadReceiptProbe ready=restore")
command("xdotool", "windowmap", "--sync", window)
command("xdotool", "windowactivate", "--sync", window)
state = command("xprop", "-id", window, "_NET_WM_STATE")
(results / "restored-window-state.txt").write_text(state + "\n")
assert "_NET_WM_STATE_HIDDEN" not in state, f"Window is still minimized: {state}"
wait_for("ReadReceiptProbe: complete ")
PY
cp "${APPDATA_ROOT}/boot.log" "${RESULT_DIR}/boot.log"
python3 - "${RESULT_DIR}/boot.log" <<'PY'
from pathlib import Path
import re
import sys

text = Path(sys.argv[1]).read_text(errors="replace")
assert "ReadReceiptProbe: started" in text, "Driver never started"
assert "ReadReceiptProbe: complete cases=9 passed=True" in text, "Driver did not pass all nine cases"
assert "ReadReceiptProbe: faulted" not in text, "Driver faulted"
samples = re.findall(r"ReadReceiptProbe case=(\S+) version=(\d+) unread=(\S+) active=(\S+).*passed=True", text)
assert [row[0] for row in samples] == ["plain", "same-size", "modal", "modal-closed", "minimized", "restored", "markdown", "detached", "scroll-end"], samples
assert [row[2] for row in samples] == ["False", "False", "True", "False", "True", "False", "False", "True", "False"], samples
assert [row[3] for row in samples] == ["True", "True", "True", "True", "False", "True", "True", "True", "True"], samples
versions = [int(row[1]) for row in samples]
assert versions[0] < versions[1] < versions[2] == versions[3] < versions[4] == versions[5] < versions[6] < versions[7] == versions[8], samples
print("Read receipt probe passed: nine actual-observer cases including modal and minimized windows.")
PY
echo "Artifacts: ${RESULT_DIR}"
