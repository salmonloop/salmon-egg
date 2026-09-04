#!/usr/bin/env bash
# Builds the macOS installer package that installs SalmonEgg.app and registers the salmon-egg command.
#
# Why a .pkg exists alongside the .dmg: a dragged .dmg has no install hook, so nothing can put the bundled
# command on PATH. Uno can produce a .pkg (PackageFormat=pkg), but its PackageAppBundle task takes no
# scripts parameter, so the package it builds cannot carry a postinstall either. This script therefore runs
# pkgbuild directly, with scripts/release/macos-pkg-postinstall.sh as the postinstall — the same file
# scripts/gates/run-macos-pkg-contract-gate.sh exercises on every push.
#
# macOS only: pkgbuild and productsign ship with Xcode's command line tools.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"

APP_BUNDLE=""
VERSION=""
OUTPUT_DIR="$REPO_ROOT/artifacts/macos"
SIGNING_KEY=""

usage() {
  cat <<'USAGE'
Usage: build-macos-pkg.sh --app-bundle <path> [options]

Options:
  --app-bundle <path>    The .app bundle to package. Must contain Contents/MacOS/cli/salmon-egg.
  --version <version>    Package version. Default: the repository display version.
  --output <dir>         Output directory. Default: artifacts/macos.
  --signing-key <name>   Developer ID Installer identity. Unsigned when omitted.
  -h, --help             Show this help.
USAGE
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --app-bundle) APP_BUNDLE="${2:?--app-bundle requires a value}"; shift 2 ;;
    --app-bundle=*) APP_BUNDLE="${1#*=}"; shift ;;
    --version) VERSION="${2:?--version requires a value}"; shift 2 ;;
    --version=*) VERSION="${1#*=}"; shift ;;
    --output) OUTPUT_DIR="${2:?--output requires a value}"; shift 2 ;;
    --output=*) OUTPUT_DIR="${1#*=}"; shift ;;
    --signing-key) SIGNING_KEY="${2:?--signing-key requires a value}"; shift 2 ;;
    --signing-key=*) SIGNING_KEY="${1#*=}"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [ -z "$APP_BUNDLE" ]; then
  echo "--app-bundle is required." >&2
  usage >&2
  exit 2
fi

if [ ! -d "$APP_BUNDLE" ]; then
  echo "App bundle not found: $APP_BUNDLE" >&2
  exit 1
fi
APP_BUNDLE="$(cd "$APP_BUNDLE" && pwd)"

case "$APP_BUNDLE" in
  *.app) ;;
  *) echo "Expected a path ending in .app, got: $APP_BUNDLE" >&2; exit 1 ;;
esac

if ! command -v pkgbuild >/dev/null 2>&1; then
  echo "pkgbuild is required and ships with the Xcode command line tools; this script runs on macOS." >&2
  exit 1
fi

# The command the postinstall links. Without it the installer would run, succeed at copying the app, and
# then fail in postinstall — a worse failure than not building the package.
COMMAND_PATH="$APP_BUNDLE/Contents/MacOS/cli/salmon-egg"
if [ ! -x "$COMMAND_PATH" ]; then
  echo "The app bundle has no executable bundled CLI at Contents/MacOS/cli/salmon-egg." >&2
  echo "Publish it with scripts/release/publish-cli-binary.sh and pass -p:SalmonEggBundledCliExecutable." >&2
  exit 1
fi

# The bundle identifier is the package identifier: two identifiers for one product would let the installer
# treat an upgrade as a second, independent install.
PLIST="$APP_BUNDLE/Contents/Info.plist"
if [ ! -f "$PLIST" ]; then
  echo "The app bundle has no Contents/Info.plist." >&2
  exit 1
fi
IDENTIFIER="$(python3 -c "
import plistlib, sys
with open('$PLIST', 'rb') as handle:
    data = plistlib.load(handle)
identifier = data.get('CFBundleIdentifier')
if not identifier:
    sys.exit('Info.plist declares no CFBundleIdentifier')
print(identifier)
")"

if [ -z "$VERSION" ]; then
  # -t:MinVer runs the MinVer target so the property holds the tag-derived version, not a default.
  VERSION="$("$DOTNET_BIN" msbuild "$REPO_ROOT/src/SalmonEgg.Cli/SalmonEgg.Cli.csproj" \
    -restore -t:MinVer -getProperty:SalmonEggDisplayVersion -nologo | tr -d '\r' | tail -n 1)"
fi

case "$VERSION" in
  [0-9]*.[0-9]*.[0-9]*) ;;
  *) echo "Package version must be a three-part numeric version, got: '$VERSION'" >&2; exit 1 ;;
esac

STAGING_DIR="$REPO_ROOT/artifacts/macos-pkg"
rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR/root/Applications" "$STAGING_DIR/scripts" "$OUTPUT_DIR"

# -R rather than a move: the .app is also uploaded on its own, and pkgbuild reads its payload from here.
cp -R "$APP_BUNDLE" "$STAGING_DIR/root/Applications/"

# pkgbuild requires the postinstall to be named exactly that, and executable.
install -m 0755 "$REPO_ROOT/scripts/release/macos-pkg-postinstall.sh" "$STAGING_DIR/scripts/postinstall"

UNSIGNED_PKG="$STAGING_DIR/SalmonEgg-unsigned.pkg"
pkgbuild \
  --root "$STAGING_DIR/root" \
  --scripts "$STAGING_DIR/scripts" \
  --identifier "$IDENTIFIER" \
  --version "$VERSION" \
  --install-location / \
  "$UNSIGNED_PKG"

PKG_PATH="$OUTPUT_DIR/SalmonEgg-$VERSION.pkg"
rm -f "$PKG_PATH" "$PKG_PATH.sha256"

if [ -n "$SIGNING_KEY" ]; then
  # A Developer ID Installer identity, which is a different certificate from the app and disk-image ones.
  # Unsigned packages still install after the user overrides Gatekeeper, so signing stays optional here and
  # the release workflow supplies the key only when the secret is configured.
  productsign --sign "$SIGNING_KEY" "$UNSIGNED_PKG" "$PKG_PATH"
  echo "[macos-pkg] signed with: $SIGNING_KEY"
else
  cp "$UNSIGNED_PKG" "$PKG_PATH"
  echo "[macos-pkg] unsigned: no installer signing identity was supplied"
fi

(
  cd "$OUTPUT_DIR"
  pkg_file="$(basename "$PKG_PATH")"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$pkg_file" > "$pkg_file.sha256"
  else
    shasum -a 256 "$pkg_file" > "$pkg_file.sha256"
  fi
)

echo "[macos-pkg] identifier: $IDENTIFIER"
echo "[macos-pkg] command:    ${COMMAND_PATH#$APP_BUNDLE/}"
echo "[macos-pkg] package:    $PKG_PATH"
echo "[macos-pkg] checksum:   $PKG_PATH.sha256"

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  {
    echo "pkg-path=$PKG_PATH"
    echo "display-version=$VERSION"
  } >> "$GITHUB_OUTPUT"
fi
