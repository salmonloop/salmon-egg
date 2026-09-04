#!/usr/bin/env bash
# Installs the SalmonEgg desktop Debian package, proves the app and the salmon-egg command both become
# usable, then removes the package and proves both disappear.
#
# This is the gate that makes the claim "installing SalmonEgg installs the CLI" true on Linux. A package
# that merely contains two executables is not the same as an installed app plus a PATH command, and the
# two ways this can be silently wrong are exactly what is asserted: a symlink whose relative depth
# resolves outside the filesystem root, and a desktop entry whose Exec names a file that is not there.
#
# Requires root (dpkg writes to /usr and /opt). Uses sudo when not already root.
set -euo pipefail

DEB_PATH="${1:?Path to the salmon-egg .deb is required}"

if [ ! -f "$DEB_PATH" ]; then
  echo "Debian package not found: $DEB_PATH" >&2
  exit 1
fi
DEB_PATH="$(cd "$(dirname "$DEB_PATH")" && pwd)/$(basename "$DEB_PATH")"

PACKAGE_NAME="salmon-egg"
COMMAND_PATH="/usr/bin/salmon-egg"
INSTALL_ROOT="/opt/salmon-egg"
APP_PATH="$INSTALL_ROOT/SalmonEgg"
CLI_PATH="$INSTALL_ROOT/cli/salmon-egg"
DESKTOP_ENTRY="/usr/share/applications/salmon-egg.desktop"
ICON_PATH="/usr/share/icons/hicolor/256x256/apps/salmon-egg.png"

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

# The package must be gone whether the gate passes, fails, or is interrupted: leaving a test build
# installed would poison every later job on the machine and every later run of this gate.
cleanup() {
  if dpkg-query --status "$PACKAGE_NAME" >/dev/null 2>&1; then
    as_root dpkg --purge "$PACKAGE_NAME" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

echo "[desktop-package-smoke] package: $DEB_PATH"

check
if dpkg-query --status "$PACKAGE_NAME" >/dev/null 2>&1; then
  fail "$PACKAGE_NAME is already installed; the gate cannot attribute anything to this package"
else
  pass "$PACKAGE_NAME is not installed before the gate runs"
fi

check
if [ -e "$COMMAND_PATH" ] || [ -e "$INSTALL_ROOT" ]; then
  fail "$COMMAND_PATH or $INSTALL_ROOT already exists before installation"
else
  pass "neither $COMMAND_PATH nor $INSTALL_ROOT exists before installation"
fi

echo "[desktop-package-smoke] 1. install"
check
# apt rather than dpkg, when it is available: apt resolves the package's own Depends, which is both what a
# user's `apt install ./salmon-egg.deb` does and a stronger assertion than dpkg gives. A misspelled or
# non-existent package name in Depends installs fine under `dpkg --install` on a machine that happens to
# have the libraries, and fails here.
if command -v apt-get >/dev/null 2>&1; then
  install_command="apt-get install --yes --quiet"
  # The runner image's package lists name versions the archive has since replaced, and the mirror
  # serves only current ones -- apt then 404s mid-install ("maybe run apt-get update"). Refreshing
  # the lists is part of installing cleanly, so it belongs inside the gate rather than in a workflow
  # step the script cannot see.
  if as_root env DEBIAN_FRONTEND=noninteractive apt-get update --quiet >/dev/null; then
    pass "apt-get update refreshed the package lists"
  else
    fail "apt-get update failed"
  fi
  if as_root env DEBIAN_FRONTEND=noninteractive apt-get install --yes --quiet "$DEB_PATH" >/dev/null; then
    pass "apt-get install resolved every declared dependency and installed the package"
  else
    fail "apt-get install failed"
  fi
else
  install_command="dpkg --install"
  if as_root dpkg --install "$DEB_PATH" >/dev/null; then
    pass "dpkg --install succeeded"
  else
    fail "dpkg --install failed"
  fi
fi
echo "[desktop-package-smoke]    (installed with: $install_command)"

echo "[desktop-package-smoke] 2. the app is installed and executable"
check
if [ -x "$APP_PATH" ]; then
  pass "$APP_PATH is present and executable"
else
  fail "$APP_PATH is missing or not executable"
fi

echo "[desktop-package-smoke] 3. the command is registered on PATH"
check
# `hash -r` clears this shell's command cache so resolution reflects the filesystem, not its memory of it.
hash -r 2>/dev/null || true
resolved="$(command -v salmon-egg || true)"
if [ "$resolved" = "$COMMAND_PATH" ]; then
  pass "command -v salmon-egg resolves to $resolved"
else
  fail "command -v salmon-egg resolved to '${resolved:-nothing}', expected $COMMAND_PATH"
fi

check
# The symlink's own target, not just "the command runs": a relative link one level too shallow resolves to
# /usr/opt/... and dangles. That is invisible in the built package's file list, which shows only the text.
link_target="$(readlink "$COMMAND_PATH" 2>/dev/null || true)"
resolved_target="$(readlink -f "$COMMAND_PATH" 2>/dev/null || true)"
if [ "$resolved_target" = "$CLI_PATH" ]; then
  pass "$COMMAND_PATH -> '$link_target' resolves to $CLI_PATH"
else
  fail "$COMMAND_PATH -> '$link_target' resolves to '${resolved_target:-nothing}', expected $CLI_PATH"
fi

check
if as_root dpkg-query --listfiles "$PACKAGE_NAME" | grep -qx "$COMMAND_PATH"; then
  pass "dpkg owns $COMMAND_PATH, so removal is reversible"
else
  fail "dpkg does not own $COMMAND_PATH"
fi

echo "[desktop-package-smoke] 4. the installed command runs"
check
# A login shell with a default PATH and an isolated app-data root: this is what a real user's shell
# resolves, not the PATH this script inherited, and it must not touch real user configuration.
smoke_appdata="$(mktemp -d)"
if version="$(env -i HOME="$HOME" SALMONEGG_APPDATA_ROOT="$smoke_appdata" \
    bash -lc 'salmon-egg --version' 2>/dev/null)"; then
  if printf '%s' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+'; then
    pass "salmon-egg --version works in a clean login shell ($version)"
  else
    fail "salmon-egg --version produced unexpected output: [$version]"
  fi
else
  fail "salmon-egg --version failed in a clean login shell"
fi
rm -rf "$smoke_appdata"

check
package_version="$(dpkg-query --show --showformat='${Version}' "$PACKAGE_NAME")"
reported_version="$(printf '%s' "${version:-}" | cut -d'+' -f1)"
case "$reported_version" in
  "$package_version"*) pass "reported version $reported_version matches package version $package_version" ;;
  *) fail "reported version '$reported_version' does not match package version '$package_version'" ;;
esac

echo "[desktop-package-smoke] 5. the app is launchable from the desktop shell"
check
if [ -f "$DESKTOP_ENTRY" ]; then
  pass "$DESKTOP_ENTRY is installed"
else
  fail "$DESKTOP_ENTRY is missing"
fi

check
# An Exec naming a file that is not there is a launcher that does nothing when clicked, and neither dpkg
# nor desktop-file-validate would notice.
exec_line="$(sed -n 's/^Exec=//p' "$DESKTOP_ENTRY" 2>/dev/null | head -1)"
exec_target="${exec_line%% *}"
if [ -n "$exec_target" ] && [ -x "$exec_target" ]; then
  pass "the desktop entry's Exec ($exec_target) is present and executable"
else
  fail "the desktop entry's Exec resolves to '${exec_target:-nothing}', which is not an executable"
fi

check
icon_name="$(sed -n 's/^Icon=//p' "$DESKTOP_ENTRY" 2>/dev/null | head -1)"
if [ -n "$icon_name" ] && [ -f "/usr/share/icons/hicolor/256x256/apps/$icon_name.png" ]; then
  pass "the desktop entry's Icon ($icon_name) has a 256x256 hicolor icon"
else
  fail "the desktop entry's Icon '${icon_name:-}' has no matching hicolor icon"
fi

check
if [ -f "$ICON_PATH" ]; then
  pass "$ICON_PATH is installed"
else
  fail "$ICON_PATH is missing"
fi

echo "[desktop-package-smoke] 6. removal takes all of it with it"
check
if as_root dpkg --purge "$PACKAGE_NAME" >/dev/null; then
  pass "dpkg --purge succeeded"
else
  fail "dpkg --purge failed"
fi

check
hash -r 2>/dev/null || true
leftovers=""
for path in "$COMMAND_PATH" "$APP_PATH" "$CLI_PATH" "$DESKTOP_ENTRY" "$ICON_PATH" "$INSTALL_ROOT"; do
  if [ -e "$path" ] || [ -L "$path" ]; then
    leftovers="$leftovers $path"
  fi
done
if [ -z "$leftovers" ]; then
  pass "install root, command, desktop entry and icon were all removed"
else
  fail "purge left behind:$leftovers"
fi

check
if env -i HOME="$HOME" bash -lc 'command -v salmon-egg' >/dev/null 2>&1; then
  fail "salmon-egg is still resolvable after purge"
else
  pass "salmon-egg is no longer resolvable"
fi

echo
if [ "$failures" -ne 0 ]; then
  echo "[desktop-package-smoke] FAILED: $failures of $checks checks failed." >&2
  exit 1
fi

if [ "$checks" -lt 14 ]; then
  # Guards against a silently short run: a gate that exited early would otherwise report success.
  echo "[desktop-package-smoke] FAILED: only $checks checks ran; expected at least 14." >&2
  exit 1
fi

echo "[desktop-package-smoke] PASSED: $checks checks."
