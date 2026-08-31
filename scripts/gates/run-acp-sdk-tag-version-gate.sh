#!/usr/bin/env bash
# Guards the one irreversible step in ACP SDK publishing: a push to nuget.org cannot be undone or
# overwritten. If the release tag and the packed version disagree, the wrong version ships forever
# under the right tag. This rule is kept out of the workflow so it can be exercised with positive and
# negative cases on any machine instead of only being discovered by pushing a tag.
set -euo pipefail

RELEASE_TAG="${1:?Release tag is required (acp-sdk-v<version>)}"
PACKAGE_DIR="${2:?Package directory is required}"

tag_prefix="acp-sdk-v"
case "$RELEASE_TAG" in
  "$tag_prefix"*) ;;
  *)
    echo "ACP SDK release tags must start with '${tag_prefix}', got: ${RELEASE_TAG}" >&2
    exit 1
    ;;
esac

tag_version="${RELEASE_TAG#"$tag_prefix"}"
# The next two checks (empty version, missing directory) are diagnostics, not independent safety
# properties: removing either still leaves the run rejected by the package name/count rules below.
# They exist to name the actual problem instead of reporting a confusing downstream mismatch, so
# the selftest deliberately does not claim mutation coverage for them.
if [ -z "$tag_version" ]; then
  echo "ACP SDK release tag carries no version: ${RELEASE_TAG}" >&2
  exit 1
fi

if [ ! -d "$PACKAGE_DIR" ]; then
  echo "ACP SDK package directory not found: ${PACKAGE_DIR}" >&2
  exit 1
fi

# Exactly one .nupkg and one .snupkg. Two packages in the same directory means an earlier build leaked
# in, and `nuget push *.nupkg` would publish both.
mapfile -t nupkgs < <(find "$PACKAGE_DIR" -maxdepth 1 -type f -name 'SalmonEgg.Acp.*.nupkg' ! -name '*.snupkg' -print | sort)
mapfile -t snupkgs < <(find "$PACKAGE_DIR" -maxdepth 1 -type f -name 'SalmonEgg.Acp.*.snupkg' -print | sort)

if [ "${#nupkgs[@]}" -ne 1 ]; then
  echo "Expected exactly one SalmonEgg.Acp .nupkg in ${PACKAGE_DIR}, found ${#nupkgs[@]}:" >&2
  printf '  %s\n' "${nupkgs[@]:-<none>}" >&2
  exit 1
fi

# Symbols are part of the published contract: PublishRepositoryUrl/EmbedUntrackedSources only pay off
# if the snupkg reaches nuget.org alongside the package.
if [ "${#snupkgs[@]}" -ne 1 ]; then
  echo "Expected exactly one SalmonEgg.Acp .snupkg in ${PACKAGE_DIR}, found ${#snupkgs[@]}:" >&2
  printf '  %s\n' "${snupkgs[@]:-<none>}" >&2
  exit 1
fi

package_file="$(basename "${nupkgs[0]}")"
package_version="${package_file#SalmonEgg.Acp.}"
package_version="${package_version%.nupkg}"

if [ "$package_version" != "$tag_version" ]; then
  echo "ACP SDK release tag and package version disagree." >&2
  echo "  tag:     ${RELEASE_TAG} (version ${tag_version})" >&2
  echo "  package: ${package_file} (version ${package_version})" >&2
  echo "Bump <Version> in src/SalmonEgg.Acp/SalmonEgg.Acp.csproj to match the tag, or retag." >&2
  exit 1
fi

symbols_file="$(basename "${snupkgs[0]}")"
if [ "$symbols_file" != "SalmonEgg.Acp.${tag_version}.snupkg" ]; then
  echo "ACP SDK symbols package does not match the release tag." >&2
  echo "  tag:     ${RELEASE_TAG} (version ${tag_version})" >&2
  echo "  symbols: ${symbols_file}" >&2
  exit 1
fi

echo "[gate] ACP SDK tag/version contract satisfied: ${RELEASE_TAG} -> ${package_file} + ${symbols_file}"
