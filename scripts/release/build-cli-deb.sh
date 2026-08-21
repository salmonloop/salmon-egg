#!/usr/bin/env bash
# Builds a Debian package that installs the SalmonEgg CLI to /usr/bin/salmon-egg.
#
# PATH ownership: /usr/bin is already on every login PATH, so the package registers the command by
# placing the binary there and dpkg removes it on purge. Nothing edits .bashrc, .zshrc or any user PATH
# variable — an installer-managed file is reversible, an edited shell profile is not.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"

EXECUTABLE=""
VERSION=""
ARCHITECTURE="amd64"
OUTPUT_DIR="$REPO_ROOT/artifacts/cli"

usage() {
  cat <<'USAGE'
Usage: build-cli-deb.sh --executable <path> [options]

Options:
  --executable <path>    Published single-file salmon-egg binary.
  --version <version>    Package version. Default: the repository display version.
  --architecture <arch>  Debian architecture. Default: amd64.
  --output <dir>         Output directory. Default: artifacts/cli.
  -h, --help             Show this help.
USAGE
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --executable) EXECUTABLE="${2:?--executable requires a value}"; shift 2 ;;
    --executable=*) EXECUTABLE="${1#*=}"; shift ;;
    --version) VERSION="${2:?--version requires a value}"; shift 2 ;;
    --version=*) VERSION="${1#*=}"; shift ;;
    --architecture) ARCHITECTURE="${2:?--architecture requires a value}"; shift 2 ;;
    --architecture=*) ARCHITECTURE="${1#*=}"; shift ;;
    --output) OUTPUT_DIR="${2:?--output requires a value}"; shift 2 ;;
    --output=*) OUTPUT_DIR="${1#*=}"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [ -z "$EXECUTABLE" ]; then
  echo "--executable is required." >&2
  usage >&2
  exit 2
fi

if [ ! -f "$EXECUTABLE" ]; then
  echo "Executable not found: $EXECUTABLE" >&2
  exit 1
fi

if ! command -v dpkg-deb >/dev/null 2>&1; then
  echo "dpkg-deb is required to build the Debian package." >&2
  exit 1
fi

if [ -z "$VERSION" ]; then
  VERSION="$("$DOTNET_BIN" msbuild "$REPO_ROOT/src/SalmonEgg.Cli/SalmonEgg.Cli.csproj" \
    -getProperty:SalmonEggDisplayVersion -nologo | tr -d '\r' | tail -n 1)"
fi

case "$VERSION" in
  [0-9]*.[0-9]*.[0-9]*) ;;
  *) echo "Package version must be a three-part numeric version, got: '$VERSION'" >&2; exit 1 ;;
esac

STAGING_DIR="$REPO_ROOT/artifacts/cli-deb/$ARCHITECTURE"
rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR/DEBIAN" \
         "$STAGING_DIR/usr/bin" \
         "$STAGING_DIR/usr/share/doc/salmon-egg-cli" \
         "$OUTPUT_DIR"

install -m 0755 "$EXECUTABLE" "$STAGING_DIR/usr/bin/salmon-egg"

INSTALLED_SIZE_KB="$(du -sk "$STAGING_DIR/usr" | cut -f1)"

cat > "$STAGING_DIR/DEBIAN/control" <<EOF
Package: salmon-egg-cli
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCHITECTURE
Maintainer: SalmonLoop <salmonloop@users.noreply.github.com>
Installed-Size: $INSTALLED_SIZE_KB
Homepage: https://github.com/salmonloop/salmon-egg
Description: Salmon Egg configuration management CLI
 Command-line tool for managing Salmon Egg ACP server configurations and
 credentials. Ships as a self-contained build, so no .NET runtime is required.
 Credentials are stored through the Secret Service; when it is unavailable the
 write fails rather than downgrading to plaintext, unless the operator passes
 --allow-insecure-storage.
EOF

if [ -f "$REPO_ROOT/LICENSE" ]; then
  install -m 0644 "$REPO_ROOT/LICENSE" "$STAGING_DIR/usr/share/doc/salmon-egg-cli/copyright"
fi

# dpkg refuses to install a package whose files are not owned by root, and the release runner is not
# root. fakeroot is what dpkg-deb's own documentation recommends for exactly this case.
DEB_PATH="$OUTPUT_DIR/salmon-egg-cli_${VERSION}_${ARCHITECTURE}.deb"
rm -f "$DEB_PATH" "$DEB_PATH.sha256"
if command -v fakeroot >/dev/null 2>&1; then
  fakeroot dpkg-deb --build --root-owner-group "$STAGING_DIR" "$DEB_PATH" >/dev/null
else
  dpkg-deb --build --root-owner-group "$STAGING_DIR" "$DEB_PATH" >/dev/null
fi

# macOS ships `shasum` rather than GNU `sha256sum`; both emit the same "<hash>  <name>" sidecar format.
(
  cd "$OUTPUT_DIR"
  deb_file="$(basename "$DEB_PATH")"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$deb_file" > "$deb_file.sha256"
  else
    shasum -a 256 "$deb_file" > "$deb_file.sha256"
  fi
)

echo "[cli-deb] package:  $DEB_PATH"
echo "[cli-deb] checksum: $DEB_PATH.sha256"

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  echo "deb-path=$DEB_PATH" >> "$GITHUB_OUTPUT"
fi
