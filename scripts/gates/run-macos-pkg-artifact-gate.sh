#!/usr/bin/env bash
# Checks a real macOS package and runs its packaged postinstall against the extracted payload only.
# This verifies packaging and PATH registration without installing into the runner's system directories.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PKG_PATH="${1:?Path to the SalmonEgg .pkg is required}"

fail() { echo "[macos-pkg-artifact] FAIL $*" >&2; exit 1; }

[ -f "$PKG_PATH" ] || fail "Package not found: $PKG_PATH"
PKG_PATH="$(cd "$(dirname "$PKG_PATH")" && pwd)/$(basename "$PKG_PATH")"
command -v pkgutil >/dev/null 2>&1 || fail "pkgutil is required; run this gate on macOS."

if [ -f "$PKG_PATH.sha256" ]; then
  (
    cd "$(dirname "$PKG_PATH")"
    if command -v sha256sum >/dev/null 2>&1; then
      sha256sum --check "$(basename "$PKG_PATH").sha256"
    else
      shasum -a 256 --check "$(basename "$PKG_PATH").sha256"
    fi
  )
fi

WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/salmonegg-macos-pkg.XXXXXX")"
trap 'rm -rf "$WORK_DIR"' EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

pkgutil --expand-full "$PKG_PATH" "$WORK_DIR/package"
INSTALL_ROOT="$WORK_DIR/package/Payload"
APP_BUNDLE="$INSTALL_ROOT/Applications/SalmonEgg.app"
POSTINSTALL="$WORK_DIR/package/Scripts/postinstall"

"$REPO_ROOT/scripts/gates/run-release-artifact-contract-gate.sh" macos-bundle "$APP_BUNDLE"
[ -f "$POSTINSTALL" ] && [ -x "$POSTINSTALL" ] || fail "The package has no executable Scripts/postinstall."
cmp -s "$POSTINSTALL" "$REPO_ROOT/scripts/release/macos-pkg-postinstall.sh" || fail "The packaged postinstall differs from the source."

EXPECTED_CLI=""
for candidate in \
  "$APP_BUNDLE/Contents/MacOS/cli/salmon-egg" \
  "$APP_BUNDLE/Contents/Resources/cli/salmon-egg"
do
  if [ -f "$candidate" ] && [ -x "$candidate" ]; then
    EXPECTED_CLI="$candidate"
    break
  fi
done
[ -n "$EXPECTED_CLI" ] || fail "The extracted bundle has no executable CLI."

# The builder ships only Applications; postinstall creates usr/local/bin. Refuse an unexpected usr
# entry, including a symlink, so the temporary registration cannot follow it into system directories.
[ ! -e "$INSTALL_ROOT/usr" ] && [ ! -L "$INSTALL_ROOT/usr" ] || fail "The payload unexpectedly contains usr."
"$POSTINSTALL" "$PKG_PATH" "$INSTALL_ROOT" "$INSTALL_ROOT"

LINK_PATH="$INSTALL_ROOT/usr/local/bin/salmon-egg"
[ -L "$LINK_PATH" ] || fail "Postinstall did not create the CLI symlink."
[ "$(readlink "$LINK_PATH")" = "$EXPECTED_CLI" ] || fail "The CLI symlink does not target the packaged command."
"$LINK_PATH" --version

echo "[macos-pkg-artifact] PASS: package payload, postinstall and linked CLI verified."
