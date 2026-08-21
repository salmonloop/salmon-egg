#!/usr/bin/env bash
# Exercises a released SalmonEgg CLI executable — the actual artifact, never `dotnet run`.
#
# Two things are being proven that a unit test cannot:
#   1. The self-contained single-file binary runs with no .NET SDK or runtime on PATH.
#   2. The fail-closed credential default, exit-code contract and secret-handling boundary hold in the
#      shipped binary, not just in a test host that shares the build's own process.
#
# All state goes to an isolated SALMONEGG_APPDATA_ROOT so the gate never reads or writes real user data.
set -euo pipefail

CLI="${1:?Path to the published salmon-egg executable is required}"

# Windows runners execute this under Git Bash. The executable bit is not meaningful on that filesystem, so
# existence is the portable precondition and the run itself proves executability.
if [ ! -f "$CLI" ]; then
  echo "CLI executable not found: $CLI" >&2
  exit 1
fi
CLI="$(cd "$(dirname "$CLI")" && pwd)/$(basename "$CLI")"

case "${OS:-}${OSTYPE:-}" in
  *Windows*|*msys*|*cygwin*) IS_WINDOWS=1 ;;
  *) IS_WINDOWS=0 ;;
esac

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

APP_DATA_ROOT="$WORK_DIR/appdata"
mkdir -p "$APP_DATA_ROOT"
export SALMONEGG_APPDATA_ROOT="$APP_DATA_ROOT"

# The published binary must not need the SDK that produced it. Asserting that `dotnet` is simply absent
# from PATH is not portable — package-managed installs land in /usr/bin, which the CLI still needs for
# other utilities. Instead a shim that records any invocation is placed ahead of the real one and
# DOTNET_ROOT is pointed at a directory that does not exist. If the executable ever consulted a shared
# runtime it would either trip the shim or fail to start, so a clean run is positive evidence rather than
# an assumption about the host layout.
DOTNET_SHIM_DIR="$WORK_DIR/no-dotnet"
DOTNET_SHIM_MARKER="$WORK_DIR/dotnet-was-invoked"
mkdir -p "$DOTNET_SHIM_DIR"
cat > "$DOTNET_SHIM_DIR/dotnet" <<SHIM
#!/bin/sh
echo "invoked with: \$*" >> "$DOTNET_SHIM_MARKER"
exit 127
SHIM
chmod +x "$DOTNET_SHIM_DIR/dotnet" 2>/dev/null || true

unset DOTNET_HOST_PATH MSBuildSDKsPath 2>/dev/null || true
export DOTNET_ROOT="$WORK_DIR/nonexistent-dotnet-root"
export PATH="$DOTNET_SHIM_DIR:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

failures=0
checks=0

fail() {
  echo "  [FAIL] $1" >&2
  failures=$((failures + 1))
}

pass() {
  echo "  [ok] $1"
}

check() {
  checks=$((checks + 1))
}

# Runs the CLI, capturing stdout, stderr and the exit code without tripping `set -e`.
run_cli() {
  set +e
  LAST_STDOUT="$(mktemp "$WORK_DIR/stdout.XXXXXX")"
  LAST_STDERR="$(mktemp "$WORK_DIR/stderr.XXXXXX")"
  if [ -n "${CLI_STDIN:-}" ]; then
    printf '%s\n' "$CLI_STDIN" | "$CLI" "$@" > "$LAST_STDOUT" 2> "$LAST_STDERR"
  else
    "$CLI" "$@" > "$LAST_STDOUT" 2> "$LAST_STDERR" < /dev/null
  fi
  LAST_EXIT=$?
  set -e
  LAST_OUT="$(cat "$LAST_STDOUT")"
  LAST_ERR="$(cat "$LAST_STDERR")"
}

expect_exit() {
  local expected="$1" label="$2"
  check
  if [ "$LAST_EXIT" -eq "$expected" ]; then
    pass "$label (exit $LAST_EXIT)"
  else
    fail "$label expected exit $expected, got $LAST_EXIT. stdout=[$LAST_OUT] stderr=[$LAST_ERR]"
  fi
}

expect_contains() {
  local haystack="$1" needle="$2" label="$3"
  check
  case "$haystack" in
    *"$needle"*) pass "$label" ;;
    *) fail "$label: expected to contain '$needle', got [$haystack]" ;;
  esac
}

expect_not_contains() {
  local haystack="$1" needle="$2" label="$3"
  check
  case "$haystack" in
    *"$needle"*) fail "$label: unexpectedly contains '$needle'" ;;
    *) pass "$label" ;;
  esac
}

echo "[artifact-smoke] executable: $CLI"
echo "[artifact-smoke] app data:   $APP_DATA_ROOT"

echo "[artifact-smoke] 1. runs without a usable .NET installation"
check
if [ "$IS_WINDOWS" -eq 1 ]; then
  # On Windows the shell resolves dotnet.exe, not the extensionless shim, so shim precedence cannot be
  # asserted. DOTNET_ROOT still points at a directory that does not exist, and the marker check below
  # still detects an invocation, so the substantive assertion is unchanged.
  pass "shim precedence not asserted on Windows; DOTNET_ROOT points at a nonexistent directory"
elif [ "$(command -v dotnet)" = "$DOTNET_SHIM_DIR/dotnet" ]; then
  pass "the recording shim precedes any real dotnet on PATH"
else
  fail "the dotnet shim is not first on PATH; resolved '$(command -v dotnet || echo none)'"
fi

run_cli --version
expect_exit 0 "--version succeeds"

check
if [ -f "$DOTNET_SHIM_MARKER" ]; then
  fail "the executable invoked dotnet: $(cat "$DOTNET_SHIM_MARKER")"
else
  pass "the executable ran without invoking dotnet"
fi
check
if printf '%s' "$LAST_OUT" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+'; then
  pass "--version reports a numeric version ($LAST_OUT)"
else
  fail "--version output is not a version: [$LAST_OUT]"
fi

echo "[artifact-smoke] 2. help and exit-code contract"
run_cli --help
expect_exit 0 "--help succeeds"
expect_contains "$LAST_OUT" "config" "--help lists the config group"
expect_contains "$LAST_OUT" "--allow-insecure-storage" "--help documents the secure-storage opt-in"

run_cli definitely-not-a-command
expect_exit 2 "unknown command returns the usage exit code"

run_cli config server remove missing-id
expect_exit 2 "remove without --yes returns the usage exit code"

run_cli config server show missing-id
expect_exit 1 "show of a missing server returns the failure exit code"

echo "[artifact-smoke] 3. add / show / update / list / remove round-trip"
run_cli config server add --name "Smoke Agent" --url "https://smoke.example" --transport streamable_http
expect_exit 0 "add succeeds"
expect_contains "$LAST_OUT" "Server added:" "add reports the new id"
SERVER_ID="${LAST_OUT##*Server added: }"
SERVER_ID="$(printf '%s' "$SERVER_ID" | tr -d '\r\n ')"
check
if [ -n "$SERVER_ID" ]; then
  pass "captured server id $SERVER_ID"
else
  fail "could not parse the server id from [$LAST_OUT]"
fi

run_cli config server show "$SERVER_ID"
expect_exit 0 "show succeeds"
expect_contains "$LAST_OUT" "Smoke Agent" "show reports the name"
expect_contains "$LAST_OUT" "streamable_http" "show reports the transport"

run_cli config server update "$SERVER_ID" --name "Renamed Agent" --timeout 45
expect_exit 0 "update succeeds"
run_cli config server show "$SERVER_ID"
expect_contains "$LAST_OUT" "Renamed Agent" "update persisted the new name"
expect_contains "$LAST_OUT" "45s" "update persisted the new timeout"

run_cli config server list
expect_exit 0 "list succeeds"
expect_contains "$LAST_OUT" "$SERVER_ID" "list includes the server"

echo "[artifact-smoke] 4. credential writes never expose the value"
SECRET="artifact-smoke-secret-$$"
CLI_STDIN="$SECRET" run_cli set-credential "$SERVER_ID" --token-stdin
FAIL_CLOSED_EXIT="$LAST_EXIT"
FAIL_CLOSED_ERR="$LAST_ERR"
expect_not_contains "$LAST_OUT$LAST_ERR" "$SECRET" "a credential write never echoes the value"

if [ "$FAIL_CLOSED_EXIT" -eq 0 ]; then
  # This host has a working platform secret store (Windows DPAPI always does, and a desktop runner may
  # have a keyring), so the fail-closed refusal cannot be observed here. What must still hold is that the
  # secret never lands in the configuration tree.
  echo "  [info] platform secure storage is available; the fail-closed refusal is not exercised on this host"
  check
  if grep -rqF "$SECRET" "$APP_DATA_ROOT/config" 2>/dev/null; then
    fail "credential value leaked into the configuration tree"
  else
    pass "credential value stayed out of the configuration tree"
  fi

  echo "[artifact-smoke] 5. the opt-in is accepted where the platform store works"
  CLI_STDIN="$SECRET" run_cli --allow-insecure-storage set-credential "$SERVER_ID" --token-stdin
  expect_exit 0 "credential write succeeds with --allow-insecure-storage"
  expect_not_contains "$LAST_OUT$LAST_ERR" "$SECRET" "the accepted credential write never echoes the value"
  check
  if grep -rqF "$SECRET" "$APP_DATA_ROOT/config" 2>/dev/null; then
    fail "credential value leaked into the configuration tree"
  else
    pass "credential value stayed out of the configuration tree"
  fi
else
  expect_exit 1 "credential write refuses rather than downgrading to plaintext"
  expect_contains "$FAIL_CLOSED_ERR" "--allow-insecure-storage" "the refusal names the opt-in flag"
  check
  if grep -rqF "$SECRET" "$APP_DATA_ROOT" 2>/dev/null; then
    fail "a refused credential write still left the secret on disk"
  else
    pass "a refused credential write left nothing on disk"
  fi

  echo "[artifact-smoke] 5. the opt-in allows the downgrade and reports it"
  CLI_STDIN="$SECRET" run_cli --allow-insecure-storage set-credential "$SERVER_ID" --token-stdin
  expect_exit 0 "credential write succeeds with --allow-insecure-storage"
  expect_contains "$LAST_ERR" "plaintext fallback storage is in use" "the downgrade is reported on stderr"
  expect_not_contains "$LAST_OUT$LAST_ERR" "$SECRET" "the accepted credential write never echoes the value"
  check
  if grep -rqF "$SECRET" "$APP_DATA_ROOT/config" 2>/dev/null; then
    fail "credential value leaked into the configuration tree"
  else
    pass "credential value stayed out of the configuration tree"
  fi
fi

echo "[artifact-smoke] 6. credential presence is reported without the value"
run_cli has-credential "$SERVER_ID"
expect_exit 0 "has-credential succeeds"
expect_contains "$LAST_OUT" "token:" "has-credential reports token presence"
expect_not_contains "$LAST_OUT" "$SECRET" "has-credential never prints the value"

run_cli clear-credential "$SERVER_ID"
expect_exit 0 "clear-credential succeeds"
check
if grep -rqF "$SECRET" "$APP_DATA_ROOT" 2>/dev/null; then
  fail "clear-credential left the secret on disk"
else
  pass "clear-credential removed the secret from disk"
fi

echo "[artifact-smoke] 7. legacy inline credential options stay rejected"
run_cli set-credential "$SERVER_ID" --token "$SECRET"
expect_exit 2 "inline --token is rejected"
expect_not_contains "$LAST_OUT$LAST_ERR" "$SECRET" "the rejection never echoes the value"

echo "[artifact-smoke] 8. removal and transaction hygiene"
run_cli config server remove "$SERVER_ID" --yes
expect_exit 0 "remove succeeds"
run_cli config server list
expect_not_contains "$LAST_OUT" "$SERVER_ID" "list no longer includes the removed server"

check
leftovers="$(find "$APP_DATA_ROOT" -name '*.pending*' -o -name '*.rollback*' | head -5)"
if [ -n "$leftovers" ]; then
  fail "interrupted-transaction artifacts remain: $leftovers"
else
  pass "no pending or rollback artifacts remain"
fi

echo "[artifact-smoke] 9. isolated app data was honored"
check
if [ -d "$APP_DATA_ROOT/config" ]; then
  pass "state was written under SALMONEGG_APPDATA_ROOT"
else
  fail "no state under SALMONEGG_APPDATA_ROOT; the gate may have used real user data"
fi

check
if [ -f "$DOTNET_SHIM_MARKER" ]; then
  fail "the executable invoked dotnet during the run: $(cat "$DOTNET_SHIM_MARKER")"
else
  pass "no command in this run needed a shared .NET runtime"
fi

echo
if [ "$failures" -ne 0 ]; then
  echo "[artifact-smoke] FAILED: $failures of $checks checks failed." >&2
  exit 1
fi

if [ "$checks" -lt 25 ]; then
  # Guards against a silently short run: a truncated gate would otherwise report success.
  echo "[artifact-smoke] FAILED: only $checks checks ran; expected at least 25." >&2
  exit 1
fi

echo "[artifact-smoke] PASSED: $checks checks."
