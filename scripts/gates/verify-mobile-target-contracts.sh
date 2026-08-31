#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

app_project="SalmonEgg/SalmonEgg/SalmonEgg.csproj"

read_property() {
  dotnet msbuild "$app_project" -getProperty:TargetFrameworks "$@" | tr -d '\r'
}

assert_equal() {
  local actual="$1"
  local expected="$2"
  local label="$3"

  if [[ "$actual" != "$expected" ]]; then
    printf '[mobile-gate] %s mismatch\nexpected: %s\nactual:   %s\n' "$label" "$expected" "$actual" >&2
    exit 1
  fi
}

find_first() {
  local pattern="$1"
  shift

  local roots=()
  for root in "$@"; do
    if [[ -d "$root" ]]; then
      roots+=("$root")
    fi
  done

  if [[ ${#roots[@]} -eq 0 ]]; then
    return 0
  fi

  find "${roots[@]}" -path "$pattern" -print 2>/dev/null | sort -V | tail -n 1
}

echo "[mobile-gate] Verify default target graph excludes mobile TFMs"
assert_equal "$(read_property)" "net10.0-desktop;net10.0-browserwasm" "default TargetFrameworks"

echo "[mobile-gate] Verify iOS opt-in target graph"
assert_equal \
  "$(read_property -p:EnableMobileTargets=true -p:EnableIosTarget=true)" \
  "net10.0-desktop;net10.0-browserwasm;net10.0-ios" \
  "iOS TargetFrameworks"

echo "[mobile-gate] Verify CI iOS restore graph is app-scoped and single-target"
assert_equal \
  "$(read_property -p:SalmonEggTargetFrameworks=net10.0-ios -p:SalmonEggSupportsDesktopProcessHost=false)" \
  "net10.0-ios" \
  "CI iOS TargetFrameworks"

echo "[mobile-gate] Verify CI Android restore graph is app-scoped and single-target"
assert_equal \
  "$(read_property -p:SalmonEggTargetFrameworks=net10.0-android36.0 -p:SalmonEggSupportsDesktopProcessHost=false)" \
  "net10.0-android36.0" \
  "CI Android TargetFrameworks"

echo "[mobile-gate] Verify app-scoped single-target restore graph preserves child TFMs"
restore_graph_dir="$(mktemp -d)"
restore_graph_path="${restore_graph_dir}/restore-graph.json"
android_restore_graph_path="${restore_graph_dir}/android-restore-graph.json"
trap 'rm -rf "${restore_graph_dir}"' EXIT
dotnet msbuild "$app_project" \
  -t:GenerateRestoreGraphFile \
  -p:RestoreGraphOutputPath="$restore_graph_path" \
  -p:SalmonEggTargetFrameworks=net10.0-desktop \
  -p:SalmonEggSupportsDesktopProcessHost=false \
  -v:minimal
python3 - "$restore_graph_path" "$repo_root" <<'PY'
import json
import pathlib
import sys

graph_path = pathlib.Path(sys.argv[1])
repo_root = pathlib.Path(sys.argv[2]).resolve()
with graph_path.open(encoding="utf-8") as stream:
    projects = json.load(stream)["projects"]

expected_frameworks = {
    "SalmonEgg/SalmonEgg/SalmonEgg.csproj": ["net10.0-desktop"],
    "src/SalmonEgg.Acp/SalmonEgg.Acp.csproj": ["net10.0"],
    "src/SalmonEgg.Application/SalmonEgg.Application.csproj": ["net10.0"],
    "src/SalmonEgg.Domain/SalmonEgg.Domain.csproj": ["net10.0"],
    "src/SalmonEgg.Infrastructure/SalmonEgg.Infrastructure.csproj": ["net10.0"],
    "src/SalmonEgg.Presentation.Core/SalmonEgg.Presentation.Core.csproj": ["net10.0"],
}

for relative_path, expected in expected_frameworks.items():
    project_path = str((repo_root / relative_path).resolve())
    if project_path not in projects:
        raise SystemExit(f"[mobile-gate] restore graph omitted {relative_path}")

    actual = list(projects[project_path]["frameworks"])
    if actual != expected:
        raise SystemExit(
            f"[mobile-gate] {relative_path} restore TFM mismatch: "
            f"expected {expected}, actual {actual}"
        )

desktop_infrastructure = str(
    (repo_root / "src/SalmonEgg.Infrastructure.Desktop/SalmonEgg.Infrastructure.Desktop.csproj").resolve()
)
if desktop_infrastructure in projects:
    raise SystemExit(
        "[mobile-gate] restricted-platform restore graph must exclude SalmonEgg.Infrastructure.Desktop"
    )
PY

android_sdk_dir="${AndroidSdkDirectory:-${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}}"
if [[ -z "$android_sdk_dir" && -d "$HOME/android-sdk" ]]; then
  android_sdk_dir="$HOME/android-sdk"
fi

if [[ -n "$android_sdk_dir" ]]; then
  echo "[mobile-gate] Verify Android opt-in target graph"
  assert_equal \
    "$(read_property -p:EnableMobileTargets=true -p:AndroidSdkDirectory="$android_sdk_dir")" \
    "net10.0-desktop;net10.0-browserwasm;net10.0-android36.0" \
    "Android TargetFrameworks"

  echo "[mobile-gate] Verify combined mobile target graph"
  assert_equal \
    "$(read_property -p:EnableMobileTargets=true -p:EnableIosTarget=true -p:AndroidSdkDirectory="$android_sdk_dir")" \
    "net10.0-desktop;net10.0-browserwasm;net10.0-android36.0;net10.0-ios" \
    "combined mobile TargetFrameworks"

  echo "[mobile-gate] Verify Release Android restore graph excludes the x64 build host RID"
  dotnet msbuild "$app_project" \
    -t:GenerateRestoreGraphFile \
    -p:RestoreGraphOutputPath="$android_restore_graph_path" \
    -p:Configuration=Release \
    -p:NETCoreSdkPortableRuntimeIdentifier=linux-x64 \
    -p:SalmonEggTargetFrameworks=net10.0-android36.0 \
    -p:SalmonEggSupportsDesktopProcessHost=false \
    -p:AndroidSdkDirectory="$android_sdk_dir" \
    -v:minimal
  python3 - "$android_restore_graph_path" "$repo_root" <<'PY'
import json
import pathlib
import sys

graph_path = pathlib.Path(sys.argv[1])
repo_root = pathlib.Path(sys.argv[2]).resolve()
with graph_path.open(encoding="utf-8") as stream:
    projects = json.load(stream)["projects"]

expected_frameworks = {
    "SalmonEgg/SalmonEgg/SalmonEgg.csproj": ["net10.0-android36.0"],
    "src/SalmonEgg.Acp/SalmonEgg.Acp.csproj": ["net10.0"],
    "src/SalmonEgg.Application/SalmonEgg.Application.csproj": ["net10.0"],
    "src/SalmonEgg.Domain/SalmonEgg.Domain.csproj": ["net10.0"],
    "src/SalmonEgg.Infrastructure/SalmonEgg.Infrastructure.csproj": ["net10.0"],
    "src/SalmonEgg.Presentation.Core/SalmonEgg.Presentation.Core.csproj": ["net10.0"],
}

for relative_path, expected in expected_frameworks.items():
    project_path = str((repo_root / relative_path).resolve())
    if project_path not in projects:
        raise SystemExit(f"[mobile-gate] Android restore graph omitted {relative_path}")

    actual = list(projects[project_path]["frameworks"])
    if actual != expected:
        raise SystemExit(
            f"[mobile-gate] {relative_path} Android restore TFM mismatch: "
            f"expected {expected}, actual {actual}"
        )

app_path = str((repo_root / "SalmonEgg/SalmonEgg/SalmonEgg.csproj").resolve())
app = projects[app_path]
android_framework = app["frameworks"]["net10.0-android36.0"]
actual_runtimes = set(app.get("runtimes", {}))
expected_runtimes = {"android-arm64", "android-x64"}
if actual_runtimes != expected_runtimes:
    raise SystemExit(
        "[mobile-gate] app Android restore RIDs mismatch: "
        f"expected {sorted(expected_runtimes)}, actual {sorted(actual_runtimes)}"
    )

download_dependencies = {
    dependency.get("name", "")
    for dependency in android_framework.get("downloadDependencies", [])
}
for forbidden_dependency in (
    "Microsoft.AspNetCore.App.Runtime.linux-x64",
    "Microsoft.NETCore.App.Host.linux-x64",
    "Microsoft.NETCore.App.Runtime.Mono.linux-x64",
):
    if forbidden_dependency in download_dependencies:
        raise SystemExit(
            f"[mobile-gate] Android restore graph contains build-host dependency {forbidden_dependency}"
        )

desktop_infrastructure = str(
    (repo_root / "src/SalmonEgg.Infrastructure.Desktop/SalmonEgg.Infrastructure.Desktop.csproj").resolve()
)
if desktop_infrastructure in projects:
    raise SystemExit(
        "[mobile-gate] Android restore graph must exclude SalmonEgg.Infrastructure.Desktop"
    )
PY
else
  echo "[mobile-gate] Android SDK not configured; skipped Android target graph checks"
fi

android_ref_dll="$(find_first '*/Microsoft.Android.Ref.36/*/ref/net10.0/Mono.Android.dll' "$HOME/.dotnet/packs" /usr/lib/dotnet/packs)"
netcore_ref_dll="$(find_first '*/Microsoft.NETCore.App.Ref/*/ref/net10.0/System.Runtime.dll' /usr/lib/dotnet/packs "$HOME/.dotnet/packs")"
csc_dll="$(find_first '*/Roslyn/bincore/csc.dll' /usr/lib/dotnet/sdk "$HOME/.dotnet/sdk")"
android_ref_dir=""
netcore_ref_dir=""
if [[ -n "$android_ref_dll" ]]; then
  android_ref_dir="$(dirname "$android_ref_dll")"
fi
if [[ -n "$netcore_ref_dll" ]]; then
  netcore_ref_dir="$(dirname "$netcore_ref_dll")"
fi

if [[ -n "$android_ref_dir" && -n "$netcore_ref_dir" && -n "$csc_dll" ]]; then
  echo "[mobile-gate] Compile Android secure storage source against Android refs"
  refs=()
  while IFS= read -r dll; do
    refs+=("-r:$dll")
  done < <(find "$netcore_ref_dir" "$android_ref_dir" -maxdepth 1 -name '*.dll' -print | sort)

  dotnet "$csc_dll" \
    -noconfig \
    -nostdlib \
    -target:library \
    -langversion:preview \
    -nullable:enable \
    -nowarn:CS1701 \
    -define:__ANDROID__ \
    "${refs[@]}" \
    -out:/tmp/SalmonEgg.AndroidKeyStoreSecureStorage.check.dll \
    src/SalmonEgg.Infrastructure/Storage/ISecureStorage.cs \
    src/SalmonEgg.Infrastructure/Storage/SecureStorageUnavailableException.cs \
    SalmonEgg/SalmonEgg/Platforms/Android/AndroidKeyStoreSecureStorage.cs
else
  echo "[mobile-gate] Android ref pack or Roslyn compiler not available; skipped Android source compile"
fi

echo "[mobile-gate] Mobile target contracts passed"
