#!/usr/bin/env bash
# Exercises run-acp-sdk-tag-version-gate.sh with positive and negative cases so the rule is verified on
# every PR instead of only when a release tag is pushed. A guard that is never exercised against a
# failing case cannot be trusted to fail when it matters.
set -euo pipefail

gate="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/run-acp-sdk-tag-version-gate.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0

expect() {
  local expected="$1" tag="$2" dir="$3" label="$4"
  local actual
  if bash "$gate" "$tag" "$dir" >/dev/null 2>&1; then actual="pass"; else actual="fail"; fi
  if [ "$actual" = "$expected" ]; then
    echo "  ok    ${label} (${actual})"
  else
    echo "  FAIL  ${label}: expected ${expected}, got ${actual}" >&2
    failures=$((failures + 1))
  fi
}

mkdir -p "$work/match" && touch "$work/match/SalmonEgg.Acp.1.2.3.nupkg" "$work/match/SalmonEgg.Acp.1.2.3.snupkg"
mkdir -p "$work/version-mismatch" \
  && touch "$work/version-mismatch/SalmonEgg.Acp.1.2.4.nupkg" \
           "$work/version-mismatch/SalmonEgg.Acp.1.2.3.snupkg"
mkdir -p "$work/symbols-mismatch" \
  && touch "$work/symbols-mismatch/SalmonEgg.Acp.1.2.3.nupkg" \
           "$work/symbols-mismatch/SalmonEgg.Acp.1.2.4.snupkg"
mkdir -p "$work/no-symbols" && touch "$work/no-symbols/SalmonEgg.Acp.1.2.3.nupkg"
mkdir -p "$work/extra-nupkg" \
  && touch "$work/extra-nupkg/SalmonEgg.Acp.1.2.3.nupkg" \
           "$work/extra-nupkg/SalmonEgg.Acp.1.2.9.nupkg" \
           "$work/extra-nupkg/SalmonEgg.Acp.1.2.3.snupkg"
mkdir -p "$work/extra-snupkg" \
  && touch "$work/extra-snupkg/SalmonEgg.Acp.1.2.3.nupkg" \
           "$work/extra-snupkg/SalmonEgg.Acp.1.2.3.snupkg" \
           "$work/extra-snupkg/SalmonEgg.Acp.1.2.9.snupkg"
mkdir -p "$work/empty"
mkdir -p "$work/bare-version" \
  && touch "$work/bare-version/SalmonEgg.Acp.v1.2.3.nupkg" \
           "$work/bare-version/SalmonEgg.Acp.v1.2.3.snupkg"

echo "[selftest] ACP SDK tag/version gate"
expect pass "acp-sdk-v1.2.3" "$work/match"        "matching tag and package"
expect fail "acp-sdk-v1.2.3" "$work/version-mismatch" "packed version disagrees with tag"
expect fail "acp-sdk-v1.2.3" "$work/symbols-mismatch" "symbols version disagrees with tag"
expect fail "acp-sdk-v1.2.3" "$work/no-symbols"   "missing symbols package"
expect fail "acp-sdk-v1.2.3" "$work/extra-nupkg"      "second package leaked into output"
expect fail "acp-sdk-v1.2.3" "$work/extra-snupkg"     "second symbols package leaked into output"
expect fail "acp-sdk-v1.2.3" "$work/empty"        "no packages at all"
expect fail "v1.2.3"         "$work/match"        "app release tag rejected for SDK"
expect fail "v1.2.3"         "$work/bare-version" "app tag rejected by prefix, not by name match"
expect fail "acp-sdk-v"      "$work/match"        "tag without a version"
expect fail "acp-sdk-v1.2.3" "$work/missing-dir"  "nonexistent package directory"

if [ "$failures" -ne 0 ]; then
  echo "[selftest] ${failures} case(s) failed" >&2
  exit 1
fi

echo "[selftest] all cases passed"
