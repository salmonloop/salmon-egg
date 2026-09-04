#!/bin/sh
# Installer postinstall: puts the bundled salmon-egg command on PATH.
#
# This script is the reason the .pkg exists. A .dmg is dragged, so it has no install hook at all, and Uno's
# PackageAppBundle task accepts no scripts parameter — there is no other place on macOS to register a
# command. /usr/local/bin is on the default PATH (macOS ships it in /etc/paths), so a symlink there is the
# smallest thing that makes `salmon-egg` resolve without editing anyone's shell profile.
#
# Installer argument contract: $1 is the package path, $2 the target location, $3 the target volume. Only
# $2 is read, which is also what lets scripts/gates/run-macos-pkg-contract-gate.sh run this exact file
# against a fake root on any platform instead of only ever running inside a real installation.
#
# Removal is the user's: macOS packages have no uninstall phase, so the link outlives a dragged-to-trash
# app the same way VS Code's `code` does. docs/release-guide.md says how to remove it.
set -eu

DESTINATION="${2:-/}"
DESTINATION="${DESTINATION%/}"

APP_BUNDLE="$DESTINATION/Applications/SalmonEgg.app"
BIN_DIR="$DESTINATION/usr/local/bin"
LINK_PATH="$BIN_DIR/salmon-egg"

# Two candidate locations, because which one the command lands in is Uno's decision, not ours. Dissecting
# the shipped v1.4.2 bundle shows how GenerateAppBundle splits a publish directory: the apphost, its
# deps.json/runtimeconfig.json and every .dylib go to Contents/MacOS (19 files), while managed assemblies,
# satellite resource directories and asset subdirectories go to Contents/Resources with their relative paths
# intact. A `cli/` subdirectory holding one extension-less Mach-O matches neither pattern exactly, and the
# split is not a documented contract, so both are probed rather than assumed. MacOS first: that is where
# Apple expects auxiliary executables, so if Uno ever classifies it that way it is the one to prefer.
COMMAND_SOURCE=""
for candidate in \
  "$APP_BUNDLE/Contents/MacOS/cli/salmon-egg" \
  "$APP_BUNDLE/Contents/Resources/cli/salmon-egg"
do
  if [ -x "$candidate" ]; then
    COMMAND_SOURCE="$candidate"
    break
  fi
done

# Fail rather than leave a link to nothing. No command in the bundle means the publish never embedded one,
# and a dangling /usr/local/bin entry is worse than an absent one: it shadows any other salmon-egg the user
# installs later.
if [ -z "$COMMAND_SOURCE" ]; then
  echo "salmon-egg is not present in the installed bundle at $APP_BUNDLE." >&2
  echo "Looked in Contents/MacOS/cli and Contents/Resources/cli." >&2
  exit 1
fi

# /usr/local/bin does not exist on a fresh macOS install.
mkdir -p "$BIN_DIR"

# Remove first rather than relying on `ln -sf`: when the existing path is a symlink to a directory, -f
# makes ln create the new link *inside* it. -L tests the link itself rather than its target, so a link left
# dangling by a previous version is replaced instead of being mistaken for absent.
if [ -L "$LINK_PATH" ] || [ -e "$LINK_PATH" ]; then
  rm -f "$LINK_PATH"
fi

ln -s "$COMMAND_SOURCE" "$LINK_PATH"

echo "salmon-egg linked: $LINK_PATH -> $COMMAND_SOURCE"
exit 0
