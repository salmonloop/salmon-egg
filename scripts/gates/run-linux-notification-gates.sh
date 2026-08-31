#!/usr/bin/env bash
# Verifies the Linux freedesktop notification service against a real session bus.
#
# The behaviour under test is only observable on the wire: whether a repeated turn asks the
# notification server to replace an existing notification, and whether an absent notification server
# is reported as an absent capability rather than a failure. A unit test with a mocked D-Bus layer
# would assert our own test double, so this gate runs the real service against a real bus and asserts
# on what the notification server received.
#
# Three cases are checked:
#   1. No DBUS_SESSION_BUS_ADDRESS at all  -> unsupported, no attempt made.
#   2. A session bus with no notification server -> unsupported, not a failure.
#   3. A session bus with a notification server  -> shown, and replaces_id proves per-turn identity.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
probe_project="$repo_root/scripts/gates/linux-notification-probe/linux-notification-probe.csproj"
server_stub="$repo_root/scripts/gates/linux-notification-server-stub.py"
configuration="${1:-Release}"

if [[ "$(uname -s)" != "Linux" ]]; then
  echo "[linux-notification-gate] Not Linux; skipped." >&2
  exit 0
fi

for tool in dbus-run-session python3; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "[linux-notification-gate] Missing $tool. Install dbus and python3 to run this gate." >&2
    exit 1
  fi
done

if ! python3 -c "import jeepney" >/dev/null 2>&1; then
  echo "[linux-notification-gate] Missing the python 'jeepney' package, needed by the notification server stub." >&2
  exit 1
fi

work_dir="$(mktemp -d -t salmonegg-linux-notification.XXXXXX)"
trap 'rm -rf "$work_dir"' EXIT

echo "[linux-notification-gate] Build probe against the app's own notification service"
dotnet build "$probe_project" -c "$configuration" --nologo -v quiet >"$work_dir/build.log" 2>&1 || {
  echo "[linux-notification-gate] Probe build failed:" >&2
  cat "$work_dir/build.log" >&2
  exit 1
}
probe_binary="$repo_root/scripts/gates/linux-notification-probe/bin/$configuration/net10.0/SalmonEgg.Gates.LinuxNotificationProbe"
if [[ ! -x "$probe_binary" ]]; then
  echo "[linux-notification-gate] Probe binary missing: $probe_binary" >&2
  exit 1
fi

fail() {
  echo "[linux-notification-gate] FAIL: $1" >&2
  exit 1
}

assert_line() {
  local label="$1" expected="$2" output_file="$3"
  grep -qxF "$expected" "$output_file" \
    || fail "$label: expected '$expected'. Got:$(printf '\n  %s' "$(cat "$output_file")")"
}

echo "[linux-notification-gate] Case 1: no session bus address"
env -u DBUS_SESSION_BUS_ADDRESS "$probe_binary" >"$work_dir/no-bus.txt" 2>&1 \
  || fail "probe exited non-zero without a session bus"
assert_line "no session bus" "IsSupported=False" "$work_dir/no-bus.txt"
assert_line "no session bus" "Permission=Unsupported" "$work_dir/no-bus.txt"
assert_line "no session bus" "ShowFirstTurn=Unsupported" "$work_dir/no-bus.txt"

echo "[linux-notification-gate] Case 2: session bus with no notification server"
dbus-run-session -- "$probe_binary" >"$work_dir/no-server.txt" 2>&1 \
  || fail "probe exited non-zero on a bus with no notification server"
assert_line "no notification server" "IsSupported=True" "$work_dir/no-server.txt"
assert_line "no notification server" "Permission=Unsupported" "$work_dir/no-server.txt"
assert_line "no notification server" "ShowFirstTurn=Unsupported" "$work_dir/no-server.txt"

echo "[linux-notification-gate] Case 3: session bus with a notification server"
records="$work_dir/records.jsonl"
: >"$records"
cat >"$work_dir/with-server.sh" <<INNER
#!/usr/bin/env bash
set -euo pipefail
python3 "$server_stub" "$records" >"$work_dir/server.log" 2>&1 &
server_pid=\$!
trap 'kill \$server_pid 2>/dev/null || true' EXIT
for _ in \$(seq 1 100); do
  grep -q READY "$work_dir/server.log" 2>/dev/null && break
  sleep 0.1
done
grep -q READY "$work_dir/server.log" || { echo "notification server stub failed to start:"; cat "$work_dir/server.log"; exit 1; }
"$probe_binary"
INNER
chmod +x "$work_dir/with-server.sh"
dbus-run-session -- "$work_dir/with-server.sh" >"$work_dir/with-server.txt" 2>&1 \
  || fail "probe exited non-zero against a notification server:$(printf '\n  %s' "$(cat "$work_dir/with-server.txt")")"

assert_line "with notification server" "IsSupported=True" "$work_dir/with-server.txt"
assert_line "with notification server" "Permission=Granted" "$work_dir/with-server.txt"
assert_line "with notification server" "ShowFirstTurn=Shown" "$work_dir/with-server.txt"
assert_line "with notification server" "ShowSameTurnAgain=Shown" "$work_dir/with-server.txt"
assert_line "with notification server" "ShowSecondTurn=Shown" "$work_dir/with-server.txt"
# A malformed request is the app's fault, not a missing platform capability.
assert_line "with notification server" "ShowBlankTitle=Failed" "$work_dir/with-server.txt"

echo "[linux-notification-gate] Assert what the notification server actually received"
python3 - "$records" <<'PY'
import json
import sys

records = [json.loads(line) for line in open(sys.argv[1], encoding="utf-8") if line.strip()]
notifies = [entry for entry in records if entry["call"] == "Notify"]

def fail(message):
    print(f"[linux-notification-gate] FAIL: {message}", file=sys.stderr)
    print(f"  records: {json.dumps(records)}", file=sys.stderr)
    raise SystemExit(1)

# A blank title never reaches the bus, so exactly three Notify calls are expected.
if len(notifies) != 3:
    fail(f"expected 3 Notify calls on the bus, got {len(notifies)}")

first, repeat, second = notifies

if first["replaces_id"] != 0:
    fail(f"first notification for a turn must not replace anything, got replaces_id={first['replaces_id']}")

# The spec replaces by the numeric id the server previously returned. Re-notifying one turn must
# reuse that id, otherwise the desktop stacks a duplicate for a turn that completed once.
if repeat["replaces_id"] != first["assigned_id"]:
    fail(
        "re-notifying the same turn must replace the server id it was given "
        f"({first['assigned_id']}), got replaces_id={repeat['replaces_id']}"
    )

if second["replaces_id"] != 0:
    fail(f"a different turn must be its own notification, got replaces_id={second['replaces_id']}")

if second["assigned_id"] == first["assigned_id"]:
    fail("a different turn must not reuse another turn's server id")

if first["body"] == second["body"]:
    fail("the two turns should carry distinguishable bodies; the probe inputs changed")

for entry in notifies:
    if entry["app_name"] != "Salmon Egg":
        fail(f"unexpected app_name on the bus: {entry['app_name']!r}")
    # -1 means "expires according to the server's settings": the desktop owns the timeout.
    if entry["timeout"] != -1:
        fail(f"expected the server default expire timeout (-1), got {entry['timeout']}")
    # Actions are a flat list of (key, label) pairs. The "default" key is the de-facto convention for
    # "a plain click on the body"; a spec-literal server renders every action as a button, so a missing
    # or blank label would show a blank button.
    if len(entry["actions"]) != 2 or entry["actions"][0] != "default":
        fail(f"expected exactly one ('default', label) action pair; got {entry['actions']}")
    if not entry["actions"][1].strip():
        fail("the default action needs a user-visible label; a spec-literal server renders it")

print("[linux-notification-gate] Bus traffic matches the spec contract.")
PY

echo "[linux-notification-gate] PASS"
