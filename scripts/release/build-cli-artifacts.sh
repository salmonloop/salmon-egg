#!/usr/bin/env bash
# Publishes the SalmonEgg CLI as a self-contained single-file executable for one supported runtime
# identifier and packages it as a release archive with a SHA-256 sidecar.
#
# The runtime identifier allow-list lives in src/SalmonEgg.Cli/SalmonEgg.Cli.csproj
# (SalmonEggCliSupportedRuntimeIdentifiers). This script reads it back instead of repeating it so the
# project stays the single source of truth for which platforms are officially supported.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CLI_PROJECT="$REPO_ROOT/src/SalmonEgg.Cli/SalmonEgg.Cli.csproj"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"

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

read_project_property() {
  "$DOTNET_BIN" msbuild "$CLI_PROJECT" "-getProperty:$1" -nologo | tr -d '\r' | tail -n 1
}

# Release identity is derived from the git tag by MinVer, so the property only holds a version once
# the MinVer target has executed; a plain -getProperty evaluation would return the pre-MinVer default.
read_release_version() {
  "$DOTNET_BIN" msbuild "$CLI_PROJECT" -restore -t:MinVer "-getProperty:$1" -nologo | tr -d '\r' | tail -n 1
}

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

DISPLAY_VERSION="$(read_release_version SalmonEggDisplayVersion)"
SUPPORTED_RIDS="$(read_project_property SalmonEggCliSupportedRuntimeIdentifiers)"

case "$DISPLAY_VERSION" in
  [0-9]*.[0-9]*.[0-9]*) ;;
  *) echo "SalmonEggDisplayVersion must be a three-part numeric version, got: '$DISPLAY_VERSION'" >&2; exit 1 ;;
esac

case ";$SUPPORTED_RIDS;" in
  *";$RID;"*) ;;
  *)
    if [ "$ALLOW_UNSUPPORTED_RID" != "true" ]; then
      echo "Unsupported runtime identifier '$RID'. Supported values: $SUPPORTED_RIDS" >&2
      exit 1
    fi
    echo "[warn] '$RID' is outside the support matrix ($SUPPORTED_RIDS); output is for local verification only." >&2
    ;;
esac

case "$RID" in
  win-*) EXECUTABLE_NAME="salmon-egg.exe"; ARCHIVE_FORMAT="zip" ;;
  *) EXECUTABLE_NAME="salmon-egg"; ARCHIVE_FORMAT="tar.gz" ;;
esac

PACKAGE_NAME="salmon-egg-cli-$DISPLAY_VERSION-$RID"
PUBLISH_DIR="$REPO_ROOT/artifacts/cli-publish/$RID"
STAGING_ROOT="$REPO_ROOT/artifacts/cli-staging/$RID"
STAGING_DIR="$STAGING_ROOT/$PACKAGE_NAME"

rm -rf "$PUBLISH_DIR" "$STAGING_ROOT"
mkdir -p "$PUBLISH_DIR" "$STAGING_DIR" "$OUTPUT_DIR"

echo "[cli-release] Publish $RID ($CONFIGURATION) version $DISPLAY_VERSION"
publish_args=(
  publish "$CLI_PROJECT"
  -c "$CONFIGURATION"
  -r "$RID"
  -p:IsCliReleaseBuild=true
  -o "$PUBLISH_DIR"
  -v minimal
)
if [ "$ALLOW_UNSUPPORTED_RID" = "true" ]; then
  publish_args+=(-p:SalmonEggCliAllowUnsupportedRuntimeIdentifier=true)
fi
"$DOTNET_BIN" "${publish_args[@]}"

EXECUTABLE_PATH="$PUBLISH_DIR/$EXECUTABLE_NAME"
if [ ! -f "$EXECUTABLE_PATH" ]; then
  echo "Published executable not found: $EXECUTABLE_PATH" >&2
  exit 1
fi

# A self-contained single-file publish must contain exactly the one executable: loose managed
# assemblies, symbols or native libraries beside it would be a silent regression back to a
# framework-style layout, and every install package below only ever carries that single file.
unexpected="$(find "$PUBLISH_DIR" -mindepth 1 ! -name "$EXECUTABLE_NAME" -print)"
if [ -n "$unexpected" ]; then
  echo "Unexpected files in single-file publish output:" >&2
  echo "$unexpected" >&2
  exit 1
fi

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
