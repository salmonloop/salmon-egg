#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <base-url>" >&2
  exit 2
fi

base_url="${1%/}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
artifact_dir="${repo_root}/artifacts/verification"
mkdir -p "${artifact_dir}"

commit="$(git -C "${repo_root}" rev-parse HEAD)"
timestamp="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
report="${artifact_dir}/wasm-static-assets-${commit:0:12}.md"

check_url() {
  local path="$1"
  local status
  status="$(curl -sS -o /dev/null -w "%{http_code}" "${base_url}${path}")"
  printf '| `%s` | `%s` |\n' "${path}" "${status}" >> "${report}"
  if [ "${status}" = "401" ] || [ "${status}" = "403" ] || [ "${status}" = "404" ]; then
    echo "asset check failed: ${path} returned ${status}" >&2
    return 1
  fi
}

{
  echo "# WASM Static Asset Verification"
  echo
  echo "- Commit: \`${commit}\`"
  echo "- Base URL: \`${base_url}\`"
  echo "- Verified at: \`${timestamp}\`"
  echo "- Runtime source: remote HTTP deployment"
  echo
  echo "| Path | HTTP status |"
  echo "|---|---|"
} > "${report}"

check_url "/index.html"
check_url "/manifest.webmanifest"
check_url "/service-worker.js"

echo "wrote ${report}"
