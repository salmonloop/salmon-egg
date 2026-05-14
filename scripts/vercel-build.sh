#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_dir="${repo_root}/.vercel-dotnet"
publish_dir="${repo_root}/publish/vercel-wasm"
dotnet_version="$(grep -m 1 '"version"' "${repo_root}/global.json" | sed -E 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/')"

export DOTNET_ROOT="${dotnet_dir}"
export PATH="${DOTNET_ROOT}:${PATH}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks | grep -q "^${dotnet_version//./\\.}"; then
  mkdir -p "${dotnet_dir}"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${dotnet_dir}/dotnet-install.sh"
  bash "${dotnet_dir}/dotnet-install.sh" --version "${dotnet_version}" --install-dir "${dotnet_dir}" --no-path
fi

if ! dotnet workload list | grep -q "^wasm-tools[[:space:]]"; then
  dotnet workload install wasm-tools --skip-manifest-update
fi

rm -rf "${publish_dir}"

dotnet publish "${repo_root}/SalmonEgg/SalmonEgg/SalmonEgg.csproj" \
  --configuration Release \
  --framework net10.0-browserwasm \
  --output "${publish_dir}" \
  -maxcpucount:1 \
  -p:BuildInParallel=false

find "${publish_dir}" -type d -name .vercel -prune -exec rm -rf {} +
