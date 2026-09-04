#!/usr/bin/env bash
# Publishes the SalmonEgg CLI as the single self-contained executable that every SalmonEgg installer
# embeds. There is deliberately no archive step: the command is no longer distributed on its own, so the
# only consumers are packaging chains that copy this file into their own payload (Windows MSIX, Windows
# desktop MSI, macOS app bundle, Linux deb). Keeping the publish in one script is what makes the command
# inside all four installers the same binary rather than four independently produced ones.
#
# The runtime identifier allow-list lives in src/SalmonEgg.Cli/SalmonEgg.Cli.csproj
# (SalmonEggCliSupportedRuntimeIdentifiers). This script reads it back instead of repeating it so the
# project stays the single source of truth for which platforms ship a bundled command.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CLI_PROJECT="$REPO_ROOT/src/SalmonEgg.Cli/SalmonEgg.Cli.csproj"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"

RID=""
CONFIGURATION="Release"
OUTPUT_DIR=""
ALLOW_UNSUPPORTED_RID="false"

usage() {
  cat <<'USAGE'
Usage: publish-cli-binary.sh --rid <runtime-identifier> [options]

Options:
  --rid <rid>              Runtime identifier to publish (win-x64, linux-x64, osx-arm64).
  --configuration <name>   Build configuration. Default: Release.
  --output <dir>           Publish directory. Default: artifacts/cli-bin/<rid>.
  --allow-unsupported-rid  Publish a RID outside the support matrix for local verification only.
  -h, --help               Show this help.

Outputs (stdout and, when set, $GITHUB_OUTPUT):
  executable-path          Absolute path of the published executable.
  executable-path-native   Same path in the host's native form (a Windows drive path under Git Bash).
  display-version          Three-part release version derived from the git tag by MinVer.
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
  win-*) EXECUTABLE_NAME="salmon-egg.exe" ;;
  *) EXECUTABLE_NAME="salmon-egg" ;;
esac

if [ -z "$OUTPUT_DIR" ]; then
  OUTPUT_DIR="$REPO_ROOT/artifacts/cli-bin/$RID"
fi

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

echo "[cli-binary] Publish $RID ($CONFIGURATION) version $DISPLAY_VERSION"
publish_args=(
  publish "$CLI_PROJECT"
  -c "$CONFIGURATION"
  -r "$RID"
  -p:IsCliReleaseBuild=true
  -o "$OUTPUT_DIR"
  -v minimal
)
if [ "$ALLOW_UNSUPPORTED_RID" = "true" ]; then
  publish_args+=(-p:SalmonEggCliAllowUnsupportedRuntimeIdentifier=true)
fi
"$DOTNET_BIN" "${publish_args[@]}"

EXECUTABLE_PATH="$OUTPUT_DIR/$EXECUTABLE_NAME"
if [ ! -f "$EXECUTABLE_PATH" ]; then
  echo "Published executable not found: $EXECUTABLE_PATH" >&2
  exit 1
fi

# A self-contained single-file publish must contain exactly the one executable. Loose managed
# assemblies, symbols or native libraries beside it would be a silent regression back to a
# framework-style layout, and every installer below embeds that single file — an MSIX app execution
# alias and a /usr/bin symlink both name one path and would launch a broken command.
unexpected="$(find "$OUTPUT_DIR" -mindepth 1 ! -name "$EXECUTABLE_NAME" -print)"
if [ -n "$unexpected" ]; then
  echo "Unexpected files in single-file publish output:" >&2
  echo "$unexpected" >&2
  exit 1
fi

chmod +x "$EXECUTABLE_PATH"

# On a Windows runner this script runs under Git Bash, where the path above is an MSYS one
# (/d/a/repo/...). MSBuild and WiX are native Windows processes and cannot open it, so the native form
# is reported alongside it. Doing the conversion here rather than in each consuming workflow step keeps
# one implementation: the same translation used to be inlined as a PowerShell regex per call site.
NATIVE_EXECUTABLE_PATH="$EXECUTABLE_PATH"
if command -v cygpath >/dev/null 2>&1; then
  NATIVE_EXECUTABLE_PATH="$(cygpath -w "$EXECUTABLE_PATH")"
fi

echo "[cli-binary] executable: $EXECUTABLE_PATH"
echo "[cli-binary] native:     $NATIVE_EXECUTABLE_PATH"
echo "[cli-binary] version:    $DISPLAY_VERSION"

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  {
    echo "executable-path=$EXECUTABLE_PATH"
    echo "executable-path-native=$NATIVE_EXECUTABLE_PATH"
    echo "display-version=$DISPLAY_VERSION"
  } >> "$GITHUB_OUTPUT"
fi
