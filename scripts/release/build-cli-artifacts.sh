#!/usr/bin/env bash
# Packages the SalmonEgg CLI as a standalone release archive with a SHA-256 sidecar.
#
# The publish itself lives in publish-cli-binary.sh, because the same self-contained single-file
# executable is embedded by every SalmonEgg installer. Duplicating the publish here would let the
# standalone archive and the bundled command drift apart while both still built successfully.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

RID=""
CONFIGURATION="Release"
OUTPUT_DIR="$REPO_ROOT/artifacts/cli"
ALLOW_UNSUPPORTED_RID="false"

usage() {
  cat <<'USAGE'
Usage: build-cli-artifacts.sh --rid <runtime-identifier> [options]

Options:
  --rid <rid>              Runtime identifier to publish (win-x64, linux-x64, osx-arm64).
  --configuration <name>   Build configuration. Default: Release.
  --output <dir>           Artifact output directory. Default: artifacts/cli.
  --allow-unsupported-rid  Publish a RID outside the support matrix for local verification only.
  -h, --help               Show this help.
USAGE
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --rid) RID="${2:?--rid requires a value}"; shift 2 ;;
    --rid=*) RID="${1#*=}"; shift ;;
    --configuration) CONFIGURATION="${2:?--configuration requires a value}"; shift 2 ;;
    --configuration=*) CONFIGURATION="${1#*=}"; shift ;;
    --output) OUTPUT_DIR="${2:?--output requires a value}"; shift 2 ;;
    --output=*) OUTPUT_DIR="${1#*=}"; shift ;;
    --allow-unsupported-rid) ALLOW_UNSUPPORTED_RID="true"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [ -z "$RID" ]; then
  echo "--rid is required." >&2
  usage >&2
  exit 2
fi

# macOS ships `shasum`, not GNU `sha256sum`. Both print "<hash>  <name>", so the sidecar format is
# identical either way and `shasum -c` / `sha256sum -c` can both verify it.
write_sha256() {
  local file_name="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$file_name" > "$file_name.sha256"
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$file_name" > "$file_name.sha256"
  else
    echo "Neither sha256sum nor shasum is available to checksum $file_name" >&2
    return 1
  fi
}

# The publish step reports the executable path and release version through the same key=value protocol
# GitHub Actions uses for step outputs, so pointing GITHUB_OUTPUT at a temporary file reads them back
# without parsing human-readable log lines. The assignment is scoped to the child process, so this
# script's own GITHUB_OUTPUT (when running in CI) is untouched.
PUBLISH_METADATA="$(mktemp)"
trap 'rm -f "$PUBLISH_METADATA"' EXIT

publish_args=(
  --rid "$RID"
  --configuration "$CONFIGURATION"
  --output "$REPO_ROOT/artifacts/cli-publish/$RID"
)
if [ "$ALLOW_UNSUPPORTED_RID" = "true" ]; then
  publish_args+=(--allow-unsupported-rid)
fi
GITHUB_OUTPUT="$PUBLISH_METADATA" "$REPO_ROOT/scripts/release/publish-cli-binary.sh" "${publish_args[@]}"

read_publish_metadata() {
  local key="$1" value
  value="$(sed -n "s/^$key=//p" "$PUBLISH_METADATA")"
  if [ -z "$value" ]; then
    echo "publish-cli-binary.sh did not report '$key'." >&2
    return 1
  fi
  printf '%s\n' "$value"
}

EXECUTABLE_PATH="$(read_publish_metadata executable-path)"
DISPLAY_VERSION="$(read_publish_metadata display-version)"
EXECUTABLE_NAME="$(basename "$EXECUTABLE_PATH")"

case "$RID" in
  win-*) ARCHIVE_FORMAT="zip" ;;
  *) ARCHIVE_FORMAT="tar.gz" ;;
esac

PACKAGE_NAME="salmon-egg-cli-$DISPLAY_VERSION-$RID"
STAGING_ROOT="$REPO_ROOT/artifacts/cli-staging/$RID"
STAGING_DIR="$STAGING_ROOT/$PACKAGE_NAME"

rm -rf "$STAGING_ROOT"
mkdir -p "$STAGING_DIR" "$OUTPUT_DIR"

cp "$EXECUTABLE_PATH" "$STAGING_DIR/$EXECUTABLE_NAME"
chmod +x "$STAGING_DIR/$EXECUTABLE_NAME"
for doc in LICENSE README.md README.en.md; do
  if [ -f "$REPO_ROOT/$doc" ]; then
    cp "$REPO_ROOT/$doc" "$STAGING_DIR/$doc"
  fi
done

ARCHIVE_PATH="$OUTPUT_DIR/$PACKAGE_NAME.$ARCHIVE_FORMAT"
rm -f "$ARCHIVE_PATH" "$ARCHIVE_PATH.sha256"

if [ "$ARCHIVE_FORMAT" = "zip" ]; then
  if command -v zip >/dev/null 2>&1; then
    (cd "$STAGING_ROOT" && zip -q -r "$ARCHIVE_PATH" "$PACKAGE_NAME")
  elif command -v python3 >/dev/null 2>&1; then
    # Windows runners and this repository's Linux toolchain both ship python3; zip(1) is not universal.
    (cd "$STAGING_ROOT" && python3 -m zipfile -c "$ARCHIVE_PATH" "$PACKAGE_NAME")
  else
    echo "Neither zip nor python3 is available to create $ARCHIVE_PATH" >&2
    exit 1
  fi
else
  tar -czf "$ARCHIVE_PATH" -C "$STAGING_ROOT" "$PACKAGE_NAME"
fi

(cd "$OUTPUT_DIR" && write_sha256 "$PACKAGE_NAME.$ARCHIVE_FORMAT")

echo "[cli-release] executable: $EXECUTABLE_PATH"
echo "[cli-release] archive:    $ARCHIVE_PATH"
echo "[cli-release] checksum:   $ARCHIVE_PATH.sha256"

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  {
    echo "display-version=$DISPLAY_VERSION"
    echo "executable-path=$EXECUTABLE_PATH"
    echo "archive-path=$ARCHIVE_PATH"
  } >> "$GITHUB_OUTPUT"
fi
