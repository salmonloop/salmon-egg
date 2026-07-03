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
