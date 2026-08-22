#!/usr/bin/env bash
#
# Asserts that a locally produced release artifact actually contains what it must, before it is uploaded.
#
# The CLI already holds this standard: run-cli-release-artifact-smoke.sh executes the binary it just
# built, and run-cli-linux-package-smoke.sh installs the .deb it just built. The other artifacts had no
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

# --- wasm ---------------------------------------------------------------------------------------------

verify_wasm() {
  local dir="$1"

  [ -d "$dir" ] || { fail "publish directory does not exist: $dir"; return 1; }

  local root
  # Uno/Blazor publish output puts the served files under wwwroot; accept either the publish root or the
  # wwwroot itself so the gate works against both `-o publish/wasm` and an unzipped release bundle.
  if [ -f "$dir/wwwroot/index.html" ]; then
    root="$dir/wwwroot"
  elif [ -f "$dir/index.html" ]; then
    root="$dir"
  else
    fail "no index.html under $dir or $dir/wwwroot"
    return 1
  fi

  # The boot manifest is what the browser reads to discover the assemblies. Without it nothing loads,
  # and its absence is invisible to the publish exit code.
  local boot=""
  for candidate in "$root/_framework/blazor.boot.json" "$root/_framework/dotnet.boot.js" "$root/_framework/dotnet.js"; do
    if [ -f "$candidate" ]; then
      boot="$candidate"
      break
    fi
  done
  [ -n "$boot" ] || { fail "no runtime boot entry under $root/_framework (looked for blazor.boot.json, dotnet.boot.js, dotnet.js)"; return 1; }

  # A framework directory containing only the boot entry is the signature of a publish that resolved the
  # host but emitted no managed payload.
  local wasm_count
  wasm_count="$(find "$root/_framework" -maxdepth 1 -name '*.wasm' | wc -l | tr -d ' ')"
  [ "$wasm_count" -gt 0 ] || { fail "no .wasm payload under $root/_framework"; return 1; }

  # These two are requested automatically by the browser on every load. Shipping without them turns
  # every deployment into a console full of 404s.
  for required in "manifest.webmanifest" "service-worker.js"; do
    [ -f "$root/$required" ] || { fail "missing $required under $root"; return 1; }
  done

  echo "[artifact-gate] wasm: index.html, $(basename "$boot"), ${wasm_count} .wasm payload file(s), manifest.webmanifest, service-worker.js all present"
}

# --- macos app bundle ---------------------------------------------------------------------------------

# Reads a top-level string value from an Info.plist without PlistBuddy or plutil, so the rule is
# rehearsable on Linux. Matches <key>NAME</key> followed by the next <string> value.
read_plist_string() {
  local plist="$1" key="$2"
  awk -v key="$key" '
    $0 ~ "<key>" key "</key>" { found = 1; next }
    found && match($0, /<string>.*<\/string>/) {
      line = substr($0, RSTART, RLENGTH)
      sub(/^<string>/, "", line)
      sub(/<\/string>$/, "", line)
      print line
      exit
    }
  ' "$plist"
}

verify_macos_bundle() {
  local app="$1"

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

  echo "[artifact-gate] macos-bundle: $(basename "$app") declares $identifier, executable '$executable' present"
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
    if "$@" >/dev/null 2>&1; then
      local actual="pass"
    else
      local actual="fail"
    fi

    if [ "$actual" != "$expected" ]; then
      echo "[artifact-gate] FAIL self-test: $description expected $expected but got $actual"
      failures=$((failures + 1))
    else
      echo "[artifact-gate] self-test: $description -> $actual (as intended)"
    fi
  }

  make_good_wasm() {
    local root="$1"
    mkdir -p "$root/wwwroot/_framework"
    printf '<html></html>' > "$root/wwwroot/index.html"
    printf '{}' > "$root/wwwroot/_framework/blazor.boot.json"
    printf 'wasm' > "$root/wwwroot/_framework/dotnet.native.wasm"
    printf '{}' > "$root/wwwroot/manifest.webmanifest"
    printf '// sw' > "$root/wwwroot/service-worker.js"
  }

  make_good_bundle() {
    local app="$1" exe="${2:-SalmonEgg}"
    mkdir -p "$app/Contents/MacOS"
    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key>
  <string>com.salmonloop.salmonegg</string>
  <key>CFBundleExecutable</key>
  <string>${exe}</string>
</dict>
</plist>
PLIST
    printf 'bin' > "$app/Contents/MacOS/$exe"
  }

  # wasm: conforming
  make_good_wasm "$work/wasm-ok"
  expect "a complete wasm publish" pass verify_wasm "$work/wasm-ok"

  # wasm: accepted when pointed straight at wwwroot
  expect "a wasm publish addressed via its wwwroot" pass verify_wasm "$work/wasm-ok/wwwroot"

  # wasm: no boot manifest
  make_good_wasm "$work/wasm-noboot"
  rm "$work/wasm-noboot/wwwroot/_framework/blazor.boot.json"
  expect "a wasm publish with no runtime boot entry" fail verify_wasm "$work/wasm-noboot"

  # wasm: boot entry present but no managed payload
  make_good_wasm "$work/wasm-nopayload"
  rm "$work/wasm-nopayload/wwwroot/_framework/dotnet.native.wasm"
  expect "a wasm publish whose framework directory has no .wasm payload" fail verify_wasm "$work/wasm-nopayload"

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
  cat > "$work/NoExecKey.app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key>
  <string>com.salmonloop.salmonegg</string>
</dict>
</plist>
PLIST
  expect "a bundle declaring no CFBundleExecutable" fail verify_macos_bundle "$work/NoExecKey.app"

  # macos: plist without CFBundleIdentifier
  make_good_bundle "$work/NoId.app"
  cat > "$work/NoId.app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key>
  <string>SalmonEgg</string>
</dict>
</plist>
PLIST
  expect "a bundle declaring no CFBundleIdentifier" fail verify_macos_bundle "$work/NoId.app"

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
