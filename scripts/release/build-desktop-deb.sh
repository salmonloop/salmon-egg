#!/usr/bin/env bash
# Builds the Debian package that installs the SalmonEgg desktop app and the salmon-egg command.
#
# Layout, and why:
#   /opt/salmon-egg/                     the self-contained publish output. /opt is where the FHS puts
#                                        add-on application packages that ship their own runtime, which is
#                                        what a self-contained .NET publish is.
#   /usr/bin/salmon-egg                  a symlink into that tree. /usr/bin is on every login PATH already,
#                                        so nothing has to edit a shell profile: an installer-managed file
#                                        is reversible, an edited .bashrc is not. dpkg owns the symlink and
#                                        removes it on purge.
#   /usr/share/applications/...          the desktop entry, so the GUI is launchable from the shell's menu
#                                        and registered for the s8p scheme the app answers.
#   /usr/share/icons/hicolor/...         the app icon at every size Resizetizer generated for this build.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"

PUBLISH_DIR=""
VERSION=""
ARCHITECTURE="amd64"
OUTPUT_DIR="$REPO_ROOT/artifacts/desktop"

PACKAGE_NAME="salmon-egg"
INSTALL_ROOT="/opt/salmon-egg"
APP_EXECUTABLE="SalmonEgg"
COMMAND_NAME="salmon-egg"

usage() {
  cat <<'USAGE'
Usage: build-desktop-deb.sh --publish-dir <dir> [options]

Options:
  --publish-dir <dir>    Self-contained Linux desktop publish output. Must contain the app executable
                         and cli/salmon-egg.
  --version <version>    Package version. Default: the repository display version.
  --architecture <arch>  Debian architecture. Default: amd64.
  --output <dir>         Output directory. Default: artifacts/desktop.
  -h, --help             Show this help.
USAGE
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --publish-dir) PUBLISH_DIR="${2:?--publish-dir requires a value}"; shift 2 ;;
    --publish-dir=*) PUBLISH_DIR="${1#*=}"; shift ;;
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

if [ -z "$PUBLISH_DIR" ]; then
  echo "--publish-dir is required." >&2
  usage >&2
  exit 2
fi

if [ ! -d "$PUBLISH_DIR" ]; then
  echo "Publish directory not found: $PUBLISH_DIR" >&2
  exit 1
fi
PUBLISH_DIR="$(cd "$PUBLISH_DIR" && pwd)"

if ! command -v dpkg-deb >/dev/null 2>&1; then
  echo "dpkg-deb is required to build the Debian package." >&2
  exit 1
fi

# Both halves of what this package promises. The command is what the PATH symlink points at, so a publish
# that lost it would produce a package whose /usr/bin/salmon-egg dangles.
if [ ! -f "$PUBLISH_DIR/$APP_EXECUTABLE" ]; then
  echo "The publish output has no app executable at $PUBLISH_DIR/$APP_EXECUTABLE." >&2
  exit 1
fi

if [ ! -f "$PUBLISH_DIR/cli/$COMMAND_NAME" ]; then
  echo "The publish output has no bundled CLI at $PUBLISH_DIR/cli/$COMMAND_NAME." >&2
  echo "Publish it with scripts/release/publish-cli-binary.sh and pass -p:SalmonEggBundledCliExecutable." >&2
  exit 1
fi

if [ -z "$VERSION" ]; then
  # -t:MinVer runs the MinVer target so the property holds the tag-derived version, not a default.
  VERSION="$("$DOTNET_BIN" msbuild "$REPO_ROOT/src/SalmonEgg.Cli/SalmonEgg.Cli.csproj" \
    -restore -t:MinVer -getProperty:SalmonEggDisplayVersion -nologo | tr -d '\r' | tail -n 1)"
fi

case "$VERSION" in
  [0-9]*.[0-9]*.[0-9]*) ;;
  *) echo "Package version must be a three-part numeric version, got: '$VERSION'" >&2; exit 1 ;;
esac

STAGING_DIR="$REPO_ROOT/artifacts/desktop-deb/$ARCHITECTURE"
rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR/DEBIAN" \
         "$STAGING_DIR$INSTALL_ROOT" \
         "$STAGING_DIR/usr/bin" \
         "$STAGING_DIR/usr/share/applications" \
         "$STAGING_DIR/usr/share/doc/$PACKAGE_NAME" \
         "$OUTPUT_DIR"

cp -a "$PUBLISH_DIR/." "$STAGING_DIR$INSTALL_ROOT/"
chmod 0755 "$STAGING_DIR$INSTALL_ROOT/$APP_EXECUTABLE" "$STAGING_DIR$INSTALL_ROOT/cli/$COMMAND_NAME"

# A relative symlink so the package stays correct under a chroot, a container image build, or any prefix a
# downstream rebuild uses. Two levels up, because the link lives in /usr/bin and the target is under /opt:
# one level would resolve to /usr/opt and dangle. dpkg records it as a package file either way, which is
# what makes purge remove the command instead of leaving a broken link behind.
ln -s "../..$INSTALL_ROOT/cli/$COMMAND_NAME" "$STAGING_DIR/usr/bin/$COMMAND_NAME"

# The app icon at every size the shell asks for. Uno's Resizetizer generates these from the single source
# artwork during publish and each one lands on a size the hicolor theme defines, so nothing here scales an
# image: the source artwork is 200x200, which is not a hicolor size, and a package shipping one odd-sized
# icon gets it rescaled twice by the theme engine or ignored outright.
ICON_SOURCE_DIR="$PUBLISH_DIR/Assets/Icons"
INSTALLED_ICON_SIZES=""
for size in 16 24 32 48 256; do
  icon="$ICON_SOURCE_DIR/iconLogo.targetsize-$size.png"
  [ -f "$icon" ] || continue
  icon_dir="$STAGING_DIR/usr/share/icons/hicolor/${size}x${size}/apps"
  mkdir -p "$icon_dir"
  install -m 0644 "$icon" "$icon_dir/$PACKAGE_NAME.png"
  INSTALLED_ICON_SIZES="$INSTALLED_ICON_SIZES $size"
done

# 256 is the size docks and the app grid actually render, so its absence is a package whose icon is a
# generic placeholder wherever it matters most.
case "$INSTALLED_ICON_SIZES" in
  *" 256"*) ;;
  *)
    echo "No generated 256x256 app icon under $ICON_SOURCE_DIR." >&2
    echo "Expected iconLogo.targetsize-256.png from Uno's Resizetizer in the publish output." >&2
    exit 1
    ;;
esac

# MimeType registers the same s8p scheme the Windows package declares as a protocol, so a link opens the
# app on either platform. StartupWMClass is deliberately absent: a wrong value silently breaks the
# taskbar's window-to-launcher association, and the Skia host's class is not something this script knows.
cat > "$STAGING_DIR/usr/share/applications/$PACKAGE_NAME.desktop" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=Salmon Egg
Comment=AI agent client
Exec=$INSTALL_ROOT/$APP_EXECUTABLE %u
Icon=$PACKAGE_NAME
Terminal=false
Categories=Development;Utility;
MimeType=x-scheme-handler/s8p;
EOF
chmod 0644 "$STAGING_DIR/usr/share/applications/$PACKAGE_NAME.desktop"

if [ -f "$REPO_ROOT/LICENSE" ]; then
  install -m 0644 "$REPO_ROOT/LICENSE" "$STAGING_DIR/usr/share/doc/$PACKAGE_NAME/copyright"
fi

INSTALLED_SIZE_KB="$(du -sk "$STAGING_DIR/opt" "$STAGING_DIR/usr" | awk '{ total += $1 } END { print total }')"

# Runtime libraries this package needs. They cannot be derived from the ELF headers: dpkg-shlibdeps and
# `readelf -d` see only link-time NEEDED entries, and everything below libstdc++ is loaded at runtime by
# P/Invoke or by the graphics stack, so the publish output declares none of it. This list was taken from
# what the published app actually maps at startup, read out of /proc/<pid>/maps while it ran headless, and
# the smoke gate re-checks that an installed package resolves all of it.
#
# Alternative dependencies rather than pinned ones: Microsoft's own .NET debs name a single libicu because
# they build one deb per distribution, while this package ships once for every glibc-compatible
# distribution. The t64 variants are Ubuntu 24.04's 64-bit-time transition, so both spellings appear.
#
# libc6 is bounded because .NET 10 supports Ubuntu 22.04 (glibc 2.35) and newer; installing on anything
# older would succeed and then fail to start.
DEPENDS="libc6 (>= 2.35), libgcc-s1, libstdc++6, zlib1g, libbrotli1"
DEPENDS="$DEPENDS, libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67 | libicu66"
DEPENDS="$DEPENDS, libssl3t64 | libssl3 | libssl1.1"
DEPENDS="$DEPENDS, libx11-6, libxext6, libxi6, libxrandr2, libxcursor1, libxrender1, libxfixes3"
DEPENDS="$DEPENDS, libgl1, libegl1, libfreetype6"
DEPENDS="$DEPENDS, libglib2.0-0t64 | libglib2.0-0"
DEPENDS="$DEPENDS, libgstreamer1.0-0, libgstreamer-plugins-base1.0-0"
DEPENDS="$DEPENDS, libwebkit2gtk-4.1-0 | libwebkit2gtk-4.0-37"

cat > "$STAGING_DIR/DEBIAN/control" <<EOF
Package: $PACKAGE_NAME
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCHITECTURE
Maintainer: SalmonLoop <salmonloop@users.noreply.github.com>
Installed-Size: $INSTALLED_SIZE_KB
Depends: $DEPENDS
Conflicts: salmon-egg-cli
Replaces: salmon-egg-cli
Homepage: https://github.com/salmonloop/salmon-egg
Description: Salmon Egg AI agent client
 Desktop client for AI coding agents speaking the Agent Client Protocol, with the
 salmon-egg command-line tool for managing server configurations and credentials.
 Ships as a self-contained build, so no .NET runtime is required.
 Credentials are stored through the Secret Service; when it is unavailable the
 write fails rather than downgrading to plaintext.
EOF

# The desktop and icon caches are indexes, not files this package owns: they have to be refreshed after
# install and after removal, and a machine without the tools is not an error. Both hooks are idempotent.
cat > "$STAGING_DIR/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e

if [ "$1" = "configure" ]; then
  if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
  fi
  if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
  fi
fi

exit 0
EOF

cat > "$STAGING_DIR/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e

if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
  if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
  fi
  if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
  fi
fi

exit 0
EOF

chmod 0755 "$STAGING_DIR/DEBIAN/postinst" "$STAGING_DIR/DEBIAN/postrm"

# dpkg refuses to install a package whose files are not owned by root, and the release runner is not root.
# fakeroot is what dpkg-deb's own documentation recommends for exactly this case.
DEB_PATH="$OUTPUT_DIR/${PACKAGE_NAME}_${VERSION}_${ARCHITECTURE}.deb"
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

echo "[desktop-deb] icons:   ${INSTALLED_ICON_SIZES# } (hicolor)"
echo "[desktop-deb] package:  $DEB_PATH"
echo "[desktop-deb] checksum: $DEB_PATH.sha256"

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  {
    echo "deb-path=$DEB_PATH"
    echo "display-version=$VERSION"
  } >> "$GITHUB_OUTPUT"
fi
