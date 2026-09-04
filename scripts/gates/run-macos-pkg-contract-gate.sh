#!/usr/bin/env bash
# Exercises the macOS installer's PATH registration, including every failure case, on any platform.
#
# The real thing runs inside `installer -pkg`, which needs macOS and an admin prompt. That made the one
# script standing between "the app is installed" and "salmon-egg is a command" unrehearsable. This gate runs
# that exact file — scripts/release/macos-pkg-postinstall.sh, the same copy pkgbuild embeds — against fake
# roots, and asserts both the link it creates and the situations in which it must refuse.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
POSTINSTALL="$REPO_ROOT/scripts/release/macos-pkg-postinstall.sh"

if [ ! -f "$POSTINSTALL" ]; then
  echo "[macos-pkg-gate] FAIL postinstall script not found: $POSTINSTALL" >&2
  exit 1
fi

failures=0
checks=0

fail() { echo "  [FAIL] $1" >&2; failures=$((failures + 1)); }
pass() { echo "  [ok] $1"; }
check() { checks=$((checks + 1)); }

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

# Builds the tree the installer would have laid down before postinstall runs: the app bundle in place, with
# or without the bundled command.
new_fake_root() {
  local name="$1" with_command="$2" area="${3:-MacOS}"
  local root="$WORK_DIR/$name"
  mkdir -p "$root/Applications/SalmonEgg.app/Contents/MacOS" \
           "$root/Applications/SalmonEgg.app/Contents/Resources"
  if [ "$with_command" = "with-command" ]; then
    mkdir -p "$root/Applications/SalmonEgg.app/Contents/$area/cli"
    printf '#!/bin/sh\necho 1.0.0\n' > "$root/Applications/SalmonEgg.app/Contents/$area/cli/salmon-egg"
    chmod +x "$root/Applications/SalmonEgg.app/Contents/$area/cli/salmon-egg"
  fi
  printf '%s' "$root"
}

# The installer invokes postinstall as `postinstall <package> <target-location> <target-volume>`.
run_postinstall() {
  local root="$1"
  sh "$POSTINSTALL" "/tmp/SalmonEgg.pkg" "$root" "$root"
}

# Both bundle areas Uno might place the command in. Which one it picks is its decision, not this
# repository's, so the installer has to work either way -- see the comment in macos-pkg-postinstall.sh.
for area in MacOS Resources; do
  echo "[macos-pkg-gate] 1. a bundle carrying the command under Contents/$area gets a link on PATH"
  root="$(new_fake_root "install-$area" with-command "$area")"
  check
  if run_postinstall "$root" >/dev/null 2>&1; then
    pass "postinstall succeeded"
  else
    fail "postinstall failed on a bundle carrying the command under Contents/$area"
  fi

  link="$root/usr/local/bin/salmon-egg"
  expected_target="$root/Applications/SalmonEgg.app/Contents/$area/cli/salmon-egg"

  check
  if [ -L "$link" ]; then
    pass "$link is a symlink"
  else
    fail "$link is not a symlink"
  fi

  check
  actual_target="$(readlink "$link" 2>/dev/null || true)"
  if [ "$actual_target" = "$expected_target" ]; then
    pass "the link points at the bundled command under Contents/$area"
  else
    fail "the link points at '${actual_target:-nothing}', expected $expected_target"
  fi

  check
  # /usr/local/bin is on the default macOS PATH, so resolving the link is what makes the command work.
  # Running it proves the link is usable rather than merely present.
  if [ "$("$link" 2>/dev/null || true)" = "1.0.0" ]; then
    pass "the linked command executes"
  else
    fail "the linked command did not execute"
  fi
done

echo "[macos-pkg-gate] 2. an install with no command in the bundle is refused"
root="$(new_fake_root missing-command without-command)"
check
if run_postinstall "$root" >/dev/null 2>&1; then
  fail "postinstall succeeded for a bundle with no bundled command"
else
  pass "postinstall failed, as it must"
fi

check
# The consequence this guards: a link to nothing shadows any salmon-egg the user installs later.
if [ -e "$root/usr/local/bin/salmon-egg" ] || [ -L "$root/usr/local/bin/salmon-egg" ]; then
  fail "a link was created even though the bundle carries no command"
else
  pass "no link was left behind"
fi

echo "[macos-pkg-gate] 3. a dangling link from an earlier version is replaced"
root="$(new_fake_root dangling with-command)"
mkdir -p "$root/usr/local/bin"
ln -s "$root/Applications/SalmonEgg.app/Contents/MacOS/cli/gone" "$root/usr/local/bin/salmon-egg"
check
if run_postinstall "$root" >/dev/null 2>&1; then
  pass "postinstall succeeded over a dangling link"
else
  fail "postinstall failed when a dangling link was present"
fi

check
# -e follows the link, so a dangling one reads as absent: without the -L test in the script, `ln -s` would
# fail here and the upgrade would silently leave the broken link in place.
actual_target="$(readlink "$root/usr/local/bin/salmon-egg" 2>/dev/null || true)"
if [ "$actual_target" = "$root/Applications/SalmonEgg.app/Contents/MacOS/cli/salmon-egg" ]; then
  pass "the dangling link was replaced with a working one"
else
  fail "the link still points at '${actual_target:-nothing}'"
fi

echo "[macos-pkg-gate] 4. a real file at the link path is replaced"
root="$(new_fake_root occupied with-command)"
mkdir -p "$root/usr/local/bin"
printf 'not a link\n' > "$root/usr/local/bin/salmon-egg"
check
if run_postinstall "$root" >/dev/null 2>&1; then
  pass "postinstall succeeded over an existing regular file"
else
  fail "postinstall failed when a regular file occupied the link path"
fi

check
if [ -L "$root/usr/local/bin/salmon-egg" ]; then
  pass "the regular file was replaced by the link"
else
  fail "the link path is still a regular file"
fi

echo "[macos-pkg-gate] 5. the script is idempotent"
root="$(new_fake_root idempotent with-command)"
check
if run_postinstall "$root" >/dev/null 2>&1 && run_postinstall "$root" >/dev/null 2>&1; then
  pass "running postinstall twice succeeds, as a reinstall over an existing install does"
else
  fail "the second run failed"
fi

echo
if [ "$failures" -ne 0 ]; then
  echo "[macos-pkg-gate] FAILED: $failures of $checks checks failed." >&2
  exit 1
fi

if [ "$checks" -lt 15 ]; then
  echo "[macos-pkg-gate] FAILED: only $checks checks ran; expected at least 15." >&2
  exit 1
fi

echo "[macos-pkg-gate] PASSED: $checks checks."
