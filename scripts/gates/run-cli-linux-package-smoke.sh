#!/usr/bin/env bash
# Installs the SalmonEgg CLI Debian package, proves `salmon-egg` becomes a PATH command, then removes the
# package and proves the command disappears.
#
# This is the gate that makes the PATH claim in the documentation true. A package that merely contains a
# binary is not the same as an installed command, and an install that cannot be reversed is a worse
# outcome than no packaging at all — so both directions are asserted.
#
# Requires root (dpkg writes to /usr/bin). Uses sudo when not already root.
set -euo pipefail

DEB_PATH="${1:?Path to the salmon-egg-cli .deb is required}"

if [ ! -f "$DEB_PATH" ]; then
  echo "Debian package not found: $DEB_PATH" >&2
  exit 1
fi
DEB_PATH="$(cd "$(dirname "$DEB_PATH")" && pwd)/$(basename "$DEB_PATH")"

PACKAGE_NAME="salmon-egg-cli"
INSTALLED_PATH="/usr/bin/salmon-egg"

if [ "$(id -u)" -eq 0 ]; then
  as_root() { "$@"; }
elif command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
  as_root() { sudo -n "$@"; }
else
  echo "This gate installs a system package and needs root or passwordless sudo." >&2
  exit 1
fi

failures=0
checks=0

fail() { echo "  [FAIL] $1" >&2; failures=$((failures + 1)); }
pass() { echo "  [ok] $1"; }
check() { checks=$((checks + 1)); }

# The package must be gone whether the gate passes, fails, or is interrupted: leaving a test build of the
# CLI installed on the runner would poison every later job on that machine.
cleanup() {
  if dpkg-query --status "$PACKAGE_NAME" >/dev/null 2>&1; then
    as_root dpkg --purge "$PACKAGE_NAME" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

echo "[package-smoke] package: $DEB_PATH"

check
if dpkg-query --status "$PACKAGE_NAME" >/dev/null 2>&1; then
  fail "$PACKAGE_NAME is already installed; the gate cannot attribute the command to this package"
else
  pass "$PACKAGE_NAME is not installed before the gate runs"
fi

check
if [ -e "$INSTALLED_PATH" ]; then
  fail "$INSTALLED_PATH already exists before installation"
else
  pass "$INSTALLED_PATH does not exist before installation"
fi

echo "[package-smoke] 1. install"
check
if as_root dpkg --install "$DEB_PATH" >/dev/null; then
  pass "dpkg --install succeeded"
else
  fail "dpkg --install failed"
fi

echo "[package-smoke] 2. the command is registered on PATH"
check
# `hash -r` clears the shell's own command cache so resolution reflects the filesystem, not this
# process's memory of it.
hash -r 2>/dev/null || true
resolved="$(command -v salmon-egg || true)"
if [ "$resolved" = "$INSTALLED_PATH" ]; then
  pass "command -v salmon-egg resolves to $resolved"
else
  fail "command -v salmon-egg resolved to '${resolved:-nothing}', expected $INSTALLED_PATH"
fi

check
if as_root dpkg-query --listfiles "$PACKAGE_NAME" | grep -qx "$INSTALLED_PATH"; then
  pass "dpkg owns $INSTALLED_PATH, so removal is reversible"
else
  fail "dpkg does not own $INSTALLED_PATH"
fi

echo "[package-smoke] 3. the installed command runs"
check
# A login shell with a default PATH: this is what a real user gets, not the PATH this script inherited.
if version="$(env -i HOME="$HOME" bash -lc 'salmon-egg --version' 2>/dev/null)"; then
  if printf '%s' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+'; then
    pass "salmon-egg --version works in a clean login shell ($version)"
  else
    fail "salmon-egg --version produced unexpected output: [$version]"
  fi
else
  fail "salmon-egg --version failed in a clean login shell"
fi

check
package_version="$(dpkg-query --show --showformat='${Version}' "$PACKAGE_NAME")"
reported_version="$(printf '%s' "$version" | cut -d'+' -f1)"
case "$reported_version" in
  "$package_version"*) pass "reported version $reported_version matches package version $package_version" ;;
  *) fail "reported version '$reported_version' does not match package version '$package_version'" ;;
esac

echo "[package-smoke] 4. removal takes the command with it"
check
if as_root dpkg --purge "$PACKAGE_NAME" >/dev/null; then
  pass "dpkg --purge succeeded"
else
  fail "dpkg --purge failed"
fi

check
hash -r 2>/dev/null || true
if [ -e "$INSTALLED_PATH" ]; then
  fail "$INSTALLED_PATH still exists after purge"
else
  pass "$INSTALLED_PATH was removed"
fi

check
if env -i HOME="$HOME" bash -lc 'command -v salmon-egg' >/dev/null 2>&1; then
  fail "salmon-egg is still resolvable after purge"
else
  pass "salmon-egg is no longer resolvable"
fi

echo
if [ "$failures" -ne 0 ]; then
  echo "[package-smoke] FAILED: $failures of $checks checks failed." >&2
  exit 1
fi

if [ "$checks" -lt 10 ]; then
  # Guards against a silently short run: a gate that exited early would otherwise report success.
  echo "[package-smoke] FAILED: only $checks checks ran; expected at least 10." >&2
  exit 1
fi

echo "[package-smoke] PASSED: $checks checks."
