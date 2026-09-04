#!/usr/bin/env bash
#
# Asserts that a locally produced release artifact actually contains what it must, before it is uploaded.
#
# The CLI already holds this standard: run-cli-release-artifact-smoke.sh executes the binary it just
# built, and run-desktop-linux-package-smoke.sh installs the .deb it just built. The other artifacts had no
# equivalent — a successful `dotnet publish` was the entire evidence that the WASM bundle or the macOS
# app bundle was usable. Those two shapes can fail in ways a green build never reveals:
#
#   * WASM: the boot manifest and the framework payload are emitted by separate steps. A publish that
#     drops the runtime assemblies still produces an index.html and still exits zero, so the release
#     ships a page that fails at first load with a 404 nobody saw.
#   * macOS .app: the bundle is a directory contract. Info.plist naming the executable is what makes it
#     launchable; a bundle whose CFBundleExecutable does not exist on disk installs fine and then does
#     nothing when double-clicked.
#
# Deliberately structural rather than behavioural: this runs on the packaging runner, where launching a
# GUI app is not possible, and the point is to catch a malformed artifact rather than to re-test the app.
# Behavioural GUI coverage lives in the smoke gates.
#
# Usage:
#   run-release-artifact-contract-gate.sh wasm <publish-dir>
#   run-release-artifact-contract-gate.sh macos-bundle <path-to-.app>
#   run-release-artifact-contract-gate.sh --self-test
set -euo pipefail

fail() {
  echo "[artifact-gate] FAIL $*" >&2
  return 1
}

# The macOS checks need a plist reader. `python3` is present on the macOS and Linux runners this gate runs
# on; on a Windows runner under Git Bash it may not be, and the macOS artifact does not exist there
# anyway. Resolve it once so a missing interpreter produces a clear message instead of an obscure failure
# deep inside a check.
PYTHON_BIN=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1; then
    PYTHON_BIN="$candidate"
    break
  fi
done

require_python() {
  if [ -z "$PYTHON_BIN" ]; then
    echo "[artifact-gate] FAIL no python3/python on PATH; the macOS bundle checks need one to read Info.plist" >&2
    return 1
  fi
}

# --- wasm ---------------------------------------------------------------------------------------------

verify_wasm() {
  local dir="$1"

  [ -d "$dir" ] || { fail "publish directory does not exist: $dir"; return 1; }

  # The publish output puts the served files under wwwroot (there is also a _framework directory at the
  # publish root holding the app's own JS interop modules, which is not the runtime payload). Accept
  # either the publish root or the wwwroot itself, so this works against `-o publish/wasm` and against an
  # unzipped release bundle.
  local root
  if [ -f "$dir/wwwroot/index.html" ]; then
    root="$dir/wwwroot"
  elif [ -f "$dir/index.html" ]; then
    root="$dir"
  else
    fail "no index.html under $dir or $dir/wwwroot"
    return 1
  fi

  [ -d "$root/_framework" ] || { fail "no _framework directory under $root"; return 1; }

  # The runtime loader. Its name carries a content hash (dotnet.<hash>.js) because fingerprinting is on,
  # so match the shape rather than a literal name. Dissecting the shipped v1.2.0 bundle showed the
  # _framework directory holds exactly dotnet.<hash>.js, dotnet.native.<hash>.js,
  # dotnet.runtime.<hash>.js, the icu .dat files and the .wasm payload -- and no blazor.boot.json at all,
  # which an earlier version of this check wrongly required.
  local loader_count
  loader_count="$(find "$root/_framework" -maxdepth 1 -name 'dotnet.*js' -not -name '*.br' -not -name '*.gz' | wc -l | tr -d ' ')"
  [ "$loader_count" -gt 0 ] || { fail "no dotnet loader script under $root/_framework"; return 1; }

  # A _framework directory holding the loader but no managed payload is the signature of a publish that
  # resolved the host and emitted nothing to run.
  local wasm_count
  wasm_count="$(find "$root/_framework" -maxdepth 1 -name '*.wasm' -not -name '*.br' -not -name '*.gz' | wc -l | tr -d ' ')"
  [ "$wasm_count" -gt 0 ] || { fail "no .wasm payload under $root/_framework"; return 1; }

  # The app's own interop modules. Their absence does not fail the build but does break storage,
  # notifications, and the shell at runtime; SalmonEgg.csproj declares them as WasmShellNativeFileReference.
  local interop_missing=""
  for module in salmon-egg-wasm-storage.js salmon-egg-wasm-shell.js salmon-egg-wasm-notifications.js; do
    if ! find "$dir" "$root" -maxdepth 3 -name "$module" 2>/dev/null | grep -q .; then
      interop_missing="$interop_missing $module"
    fi
  done
  [ -z "$interop_missing" ] || { fail "missing app interop module(s):$interop_missing"; return 1; }

  # Requested automatically by the browser on every load; shipping without them turns every deployment
  # into a console full of 404s. scripts/gates/verify-wasm-static-assets.sh checks the same two names
  # against a deployed URL -- this is the same contract, asserted before upload.
  for required in "manifest.webmanifest" "service-worker.js"; do
    [ -f "$root/$required" ] || { fail "missing $required under $root"; return 1; }
  done

  echo "[artifact-gate] wasm: index.html, ${loader_count} loader script(s), ${wasm_count} .wasm payload file(s), 3 interop module(s), manifest.webmanifest, service-worker.js all present"
}

# --- macos app bundle ---------------------------------------------------------------------------------

# Reads a top-level string value from an Info.plist.
#
# The real bundle's Info.plist is a BINARY plist -- the shipped v1.2.0 SalmonEgg.app begins with the
# `bplist00` magic. An awk/XML text scan (which the first version of this gate used) reads nothing out of
# it and would report every correct bundle as declaring no CFBundleExecutable. Python's plistlib reads
# both the binary and XML forms and is present on every runner used here, including the macOS packaging
# runner, so the rule stays rehearsable off macOS without plutil or PlistBuddy.
read_plist_string() {
  local plist="$1" key="$2"
  "$PYTHON_BIN" -c '
import plistlib
import sys

path, key = sys.argv[1], sys.argv[2]
try:
    with open(path, "rb") as handle:
        data = plistlib.load(handle)
except Exception:
    sys.exit(1)

value = data.get(key)
if isinstance(value, str) and value:
    # Raw bytes, not print(): a Windows interpreter translates text-mode newlines to \r\n,
    # and the command substitution that captures this output strips only the \n -- leaving a
    # trailing \r on the executable name, which then never matches the file on disk.
    sys.stdout.buffer.write(value.encode("utf-8"))
' "$plist" "$key"
}

verify_macos_bundle() {
  local app="$1"

  require_python || return 1

  [ -d "$app" ] || { fail "app bundle is not a directory: $app"; return 1; }
  case "$app" in
    *.app) ;;
    *) fail "expected a path ending in .app, got: $app"; return 1 ;;
  esac

  local plist="$app/Contents/Info.plist"
  [ -f "$plist" ] || { fail "missing Contents/Info.plist in $app"; return 1; }

  local executable
  executable="$(read_plist_string "$plist" CFBundleExecutable)"
  [ -n "$executable" ] || { fail "Info.plist declares no CFBundleExecutable"; return 1; }

  # The single most consequential inconsistency: a bundle that names an executable it does not contain
  # installs cleanly and then does nothing at all when launched.
  local binary="$app/Contents/MacOS/$executable"
  [ -f "$binary" ] || { fail "Info.plist declares CFBundleExecutable '$executable' but $binary does not exist"; return 1; }

  local identifier
  identifier="$(read_plist_string "$plist" CFBundleIdentifier)"
  [ -n "$identifier" ] || { fail "Info.plist declares no CFBundleIdentifier"; return 1; }

  # The bundled command. Installing SalmonEgg installs salmon-egg, and on macOS the only thing that puts it
  # on PATH is the pkg's postinstall symlinking it -- so a bundle without it produces an installer that
  # copies the app and then fails, or a .dmg whose command silently does not exist.
  #
  # Both bundle areas are accepted, in the same order the postinstall probes them: Uno's GenerateAppBundle
  # sends the apphost and .dylib files to Contents/MacOS and everything else to Contents/Resources with
  # relative paths intact, and a cli/ subdirectory holding one extension-less Mach-O matches neither pattern
  # exactly. The area is reported so the first real bundle settles the question.
  local command_path="" area=""
  for area in MacOS Resources; do
    if [ -f "$app/Contents/$area/cli/salmon-egg" ]; then
      command_path="$app/Contents/$area/cli/salmon-egg"
      break
    fi
  done
  [ -n "$command_path" ] || { fail "the bundle carries no salmon-egg under Contents/MacOS/cli or Contents/Resources/cli"; return 1; }
  # Executability, not just presence: the postinstall tests -x and refuses otherwise, so a mode bit dropped
  # by a copy would fail the install rather than this gate.
  [ -x "$command_path" ] || { fail "$command_path is present but not executable"; return 1; }

  echo "[artifact-gate] macos-bundle: $(basename "$app") declares $identifier, executable '$executable' present, bundled salmon-egg under Contents/$area/cli"
}

# --- self-test ----------------------------------------------------------------------------------------

# Every assertion above claims to reject a specific defect. Without this, a weakened check would pass
# silently for as long as the artifacts happen to be well formed.
run_self_test() {
  local work
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN

  local failures=0

  expect() {
    local description="$1" expected="$2"
    shift 2
    # The check's own output is hidden on the happy path, but shown when a case turns out
    # unexpectedly: a verdict alone ("expected pass but got fail") cannot be diagnosed from a
    # CI log, and this self-test exists so the gate never needs a debugger to fix.
    local output
    output="$("$@" 2>&1)" && actual="pass" || actual="fail"

    if [ "$actual" != "$expected" ]; then
      echo "[artifact-gate] FAIL self-test: $description expected $expected but got $actual"
      [ -n "$output" ] && printf '%s\n' "$output" | sed 's/^/[artifact-gate]   /'
      failures=$((failures + 1))
    else
      echo "[artifact-gate] self-test: $description -> $actual (as intended)"
    fi
  }

  # Mirrors the layout of the shipped bundle rather than an idealized one: hashed loader names, no
  # blazor.boot.json, interop modules under _framework, served files under wwwroot.
  make_good_wasm() {
    local root="$1"
    mkdir -p "$root/wwwroot/_framework"
    printf '<html></html>' > "$root/wwwroot/index.html"
    printf '// loader' > "$root/wwwroot/_framework/dotnet.115cw7iqpe.js"
    printf '// native' > "$root/wwwroot/_framework/dotnet.native.e4tubmy6fd.js"
    printf '// runtime' > "$root/wwwroot/_framework/dotnet.runtime.zbexyp8zrs.js"
    printf 'wasm' > "$root/wwwroot/_framework/dotnet.native.wasm"
    printf 'wasm' > "$root/wwwroot/_framework/SalmonEgg.abc123.wasm"
    for module in salmon-egg-wasm-storage.js salmon-egg-wasm-shell.js salmon-egg-wasm-notifications.js; do
      printf '// interop' > "$root/wwwroot/_framework/$module"
    done
    printf '{}' > "$root/wwwroot/manifest.webmanifest"
    printf '// sw' > "$root/wwwroot/service-worker.js"
  }

  # Written as a real binary plist, which is the form the packaging tool emits.
  make_good_bundle() {
    local app="$1" exe="${2:-SalmonEgg}" area="${3:-MacOS}"
    mkdir -p "$app/Contents/MacOS"
    "$PYTHON_BIN" -c '
import plistlib
import sys

path, exe = sys.argv[1], sys.argv[2]
with open(path, "wb") as handle:
    plistlib.dump(
        {"CFBundleIdentifier": "com.companyname.salmonegg", "CFBundleExecutable": exe},
        handle,
        fmt=plistlib.FMT_BINARY,
    )
' "$app/Contents/Info.plist" "$exe"
    printf 'bin' > "$app/Contents/MacOS/$exe"
    # The bundled command, which a conforming release bundle carries and the pkg's postinstall links.
    mkdir -p "$app/Contents/$area/cli"
    printf 'bin' > "$app/Contents/$area/cli/salmon-egg"
    chmod +x "$app/Contents/$area/cli/salmon-egg"
  }

  write_partial_plist() {
    "$PYTHON_BIN" -c '
import plistlib
import sys

path, key, value = sys.argv[1], sys.argv[2], sys.argv[3]
with open(path, "wb") as handle:
    plistlib.dump({key: value}, handle, fmt=plistlib.FMT_BINARY)
' "$1" "$2" "$3"
  }

  # wasm: conforming
  make_good_wasm "$work/wasm-ok"
  expect "a complete wasm publish" pass verify_wasm "$work/wasm-ok"

  # wasm: accepted when pointed straight at wwwroot
  expect "a wasm publish addressed via its wwwroot" pass verify_wasm "$work/wasm-ok/wwwroot"

  # wasm: no loader script. The names are content-hashed, so remove by glob rather than literal name.
  make_good_wasm "$work/wasm-noloader"
  rm "$work/wasm-noloader"/wwwroot/_framework/dotnet.*.js
  expect "a wasm publish with no dotnet loader script" fail verify_wasm "$work/wasm-noloader"

  # wasm: loader present but no managed payload at all
  make_good_wasm "$work/wasm-nopayload"
  find "$work/wasm-nopayload/wwwroot/_framework" -name '*.wasm' -delete
  expect "a wasm publish whose framework directory has no .wasm payload" fail verify_wasm "$work/wasm-nopayload"

  # wasm: one payload file removed while others remain -- still a valid publish
  make_good_wasm "$work/wasm-onepayload"
  rm "$work/wasm-onepayload/wwwroot/_framework/SalmonEgg.abc123.wasm"
  expect "a wasm publish with fewer payload files but not zero" pass verify_wasm "$work/wasm-onepayload"

  # wasm: the app's own interop module dropped
  make_good_wasm "$work/wasm-nointerop"
  rm "$work/wasm-nointerop/wwwroot/_framework/salmon-egg-wasm-storage.js"
  expect "a wasm publish missing an app interop module" fail verify_wasm "$work/wasm-nointerop"

  # wasm: the whole framework directory absent
  make_good_wasm "$work/wasm-noframework"
  rm -rf "$work/wasm-noframework/wwwroot/_framework"
  expect "a wasm publish with no _framework directory" fail verify_wasm "$work/wasm-noframework"

  # wasm: missing service worker
  make_good_wasm "$work/wasm-nosw"
  rm "$work/wasm-nosw/wwwroot/service-worker.js"
  expect "a wasm publish missing service-worker.js" fail verify_wasm "$work/wasm-nosw"

  # wasm: missing manifest
  make_good_wasm "$work/wasm-nomanifest"
  rm "$work/wasm-nomanifest/wwwroot/manifest.webmanifest"
  expect "a wasm publish missing manifest.webmanifest" fail verify_wasm "$work/wasm-nomanifest"

  # wasm: no index at all
  mkdir -p "$work/wasm-empty"
  expect "an empty publish directory" fail verify_wasm "$work/wasm-empty"
  expect "a publish directory that does not exist" fail verify_wasm "$work/wasm-absent"

  # The macOS half needs a plist reader. Skipping it silently would make a Windows run of this self-test
  # look like full coverage, so say so out loud; the wasm half above needs nothing beyond coreutils.
  if [ -z "$PYTHON_BIN" ]; then
    echo "[artifact-gate] self-test: macOS bundle cases SKIPPED (no python3/python on PATH)"
    if [ "$failures" -gt 0 ]; then
      echo "[artifact-gate] self-test failed with $failures wrong outcome(s)" >&2
      return 1
    fi

    echo "[artifact-gate] self-test passed (wasm cases only)"
    return 0
  fi

  # macos: conforming
  make_good_bundle "$work/Good.app"
  expect "a complete .app bundle" pass verify_macos_bundle "$work/Good.app"

  # macos: Info.plist names a binary that is absent — the defect that ships a bundle doing nothing
  make_good_bundle "$work/Dangling.app"
  rm "$work/Dangling.app/Contents/MacOS/SalmonEgg"
  expect "a bundle whose declared executable is missing" fail verify_macos_bundle "$work/Dangling.app"

  # macos: no Info.plist
  mkdir -p "$work/NoPlist.app/Contents/MacOS"
  expect "a bundle with no Info.plist" fail verify_macos_bundle "$work/NoPlist.app"

  # macos: plist without CFBundleExecutable
  mkdir -p "$work/NoExecKey.app/Contents/MacOS"
  write_partial_plist "$work/NoExecKey.app/Contents/Info.plist" CFBundleIdentifier com.companyname.salmonegg
  expect "a bundle declaring no CFBundleExecutable" fail verify_macos_bundle "$work/NoExecKey.app"

  # macos: plist without CFBundleIdentifier
  make_good_bundle "$work/NoId.app"
  write_partial_plist "$work/NoId.app/Contents/Info.plist" CFBundleExecutable SalmonEgg
  expect "a bundle declaring no CFBundleIdentifier" fail verify_macos_bundle "$work/NoId.app"

  # macos: an XML plist must also be readable -- the format is an implementation detail of the toolchain
  # and could change back.
  mkdir -p "$work/XmlPlist.app/Contents/MacOS/cli"
  printf 'bin' > "$work/XmlPlist.app/Contents/MacOS/SalmonEgg"
  printf 'bin' > "$work/XmlPlist.app/Contents/MacOS/cli/salmon-egg"
  chmod +x "$work/XmlPlist.app/Contents/MacOS/cli/salmon-egg"
  cat > "$work/XmlPlist.app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key>
  <string>com.companyname.salmonegg</string>
  <key>CFBundleExecutable</key>
  <string>SalmonEgg</string>
</dict>
</plist>
PLIST
  expect "a bundle whose Info.plist is XML rather than binary" pass verify_macos_bundle "$work/XmlPlist.app"

  # macos: a plist that is not a plist at all
  mkdir -p "$work/Garbage.app/Contents/MacOS"
  printf 'bin' > "$work/Garbage.app/Contents/MacOS/SalmonEgg"
  printf 'not a plist' > "$work/Garbage.app/Contents/Info.plist"
  expect "a bundle whose Info.plist is unreadable" fail verify_macos_bundle "$work/Garbage.app"

  # macos: the same bundle with the command in the other area Uno might choose. Rejecting this would make
  # the gate fail on a bundle that installs correctly.
  make_good_bundle "$work/ResourcesCli.app" SalmonEgg Resources
  expect "a bundle carrying salmon-egg under Contents/Resources" pass verify_macos_bundle "$work/ResourcesCli.app"

  # macos: the app is complete but the bundled command was never embedded. The .dmg would install an app
  # whose `salmon-egg` does not exist, and the .pkg's postinstall would fail after copying it.
  make_good_bundle "$work/NoCli.app"
  rm -rf "$work/NoCli.app/Contents/MacOS/cli" "$work/NoCli.app/Contents/Resources/cli"
  expect "a bundle carrying no bundled salmon-egg" fail verify_macos_bundle "$work/NoCli.app"

  # macos: present but not executable, which is what a copy through a filesystem that drops the mode bit
  # produces. The postinstall tests -x, so the installer would refuse it.
  make_good_bundle "$work/UnexecutableCli.app"
  chmod -x "$work/UnexecutableCli.app/Contents/MacOS/cli/salmon-egg"
  expect "a bundle whose salmon-egg is not executable" fail verify_macos_bundle "$work/UnexecutableCli.app"

  # macos: a plain directory that is not a bundle
  mkdir -p "$work/not-a-bundle"
  expect "a directory that is not a .app" fail verify_macos_bundle "$work/not-a-bundle"

  if [ "$failures" -gt 0 ]; then
    echo "[artifact-gate] self-test failed with $failures wrong outcome(s)" >&2
    return 1
  fi

  echo "[artifact-gate] self-test passed"
}

# --- entry --------------------------------------------------------------------------------------------

case "${1:-}" in
  --self-test)
    run_self_test
    ;;
  wasm)
    verify_wasm "${2:?publish directory is required}"
    ;;
  macos-bundle)
    verify_macos_bundle "${2:?path to the .app bundle is required}"
    ;;
  *)
    echo "usage: $0 {wasm <publish-dir>|macos-bundle <path-to-.app>|--self-test}" >&2
    exit 2
    ;;
esac
