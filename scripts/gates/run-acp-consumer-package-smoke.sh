#!/usr/bin/env bash
set -euo pipefail

PACKAGE_DIR="${1:?Package directory is required}"
CONFIGURATION="${2:-Release}"

DOTNET_BIN="${DOTNET_BIN:-dotnet}"

# Resolved from the script's own location rather than the caller's working directory: the workflows
# invoke this from the repository root today, but the draft-type list it reads is repository state,
# not an argument, and it must not silently read nothing if that ever changes.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

if [ ! -d "$PACKAGE_DIR" ]; then
  echo "ACP SDK package directory not found: $PACKAGE_DIR" >&2
  exit 1
fi

package_dir="$(cd "$PACKAGE_DIR" && pwd)"
smoke_dir="$(mktemp -d)"
trap 'rm -rf "$smoke_dir"' EXIT

package_list="$smoke_dir/packages.txt"
find "$package_dir" -maxdepth 1 -type f -name 'SalmonEgg.Acp.*.nupkg' -print | sort > "$package_list"
package_count="$(wc -l < "$package_list" | tr -d ' ')"
if [ "$package_count" -eq 0 ]; then
  echo "ACP SDK package artifact not found in $package_dir" >&2
  exit 1
fi
if [ "$package_count" -ne 1 ]; then
  echo "Expected exactly one ACP SDK package artifact in $package_dir, found $package_count:" >&2
  cat "$package_list" >&2
  exit 1
fi

nupkg="$(sed -n '1p' "$package_list")"
package_file="$(basename "$nupkg")"
package_version="${package_file#SalmonEgg.Acp.}"
package_version="${package_version%.nupkg}"
expected_type_name="SalmonEgg.Acp.Protocol.SessionListParams"

export NUGET_PACKAGES="$smoke_dir/packages"

# The v2 draft surface is classified in the SDK's public-surface manifest, and that is the list this
# gate consumes: reading it here means a newly modeled draft type is covered the moment it is
# classified, instead of when somebody remembers to extend a second list in this script.
manifest="$repo_root/src/SalmonEgg.Acp/PublicSurface.Types.txt"
if [ ! -f "$manifest" ]; then
  echo "ACP public surface manifest not found: $manifest" >&2
  exit 1
fi
draft_types="$smoke_dir/draft-types.txt"
awk '$1 !~ /^#/ && $2 == "draft" { print $1 }' "$manifest" | sort > "$draft_types"
draft_count="$(wc -l < "$draft_types" | tr -d ' ')"
if [ "$draft_count" -eq 0 ]; then
  echo "No draft types are classified in $manifest, so this gate would assert nothing." >&2
  exit 1
fi
echo "[smoke] Draft contracts to verify: $draft_count"

# Capturing a command that is *expected* to fail needs the failure to survive `set -e`, and the exit
# status has to come from the compiler rather than from a pipeline tail: `dotnet build | sed` reports
# sed's status, which is always 0.
#
# The output goes to a file rather than into a variable, and every later assertion greps that file.
# That is not a style preference. `printf '%s' "$out" | grep -q needle` under `set -o pipefail` reports
# "not found" whenever grep matches early enough to exit before printf finished writing: printf dies of
# SIGPIPE (141), pipefail adopts that status, and the search silently inverts. Measured on this gate's
# own ~55 KB build log: diagnostics near the top read as absent while later ones read as present, so
# the check failed on exactly the evidence it was most certain about.
build_rc=0
build_log="$smoke_dir/build.log"
capture_build() {
  set +e
  "$@" > "$build_log" 2>&1
  build_rc=$?
  set -e
}

# grep -c exits non-zero on zero matches, which `set -e` would turn into a script failure.
count_in_build_log() {
  local matches
  set +e
  matches="$(grep -cE "$1" "$build_log")"
  set -e
  printf '%s' "${matches:-0}"
}

require_no_infrastructure_failure() {
  local label="$1"
  # A package that cannot be restored also produces "Build FAILED" - with zero diagnostics from the
  # SDK. Asserting only "the build failed" would pass for a broken package reference and certify a
  # protection that never ran, so the infrastructure failures are excluded explicitly.
  if grep -qE "(NU1101|NU1102|NU1103|NU1605|error MSB|error NETSDK)" "$build_log"; then
    echo "$label: build failed for infrastructure reasons, not because of the draft marking." >&2
    grep -E "(NU1101|NU1102|NU1103|NU1605|error MSB|error NETSDK)" "$build_log" >&2
    exit 1
  fi
}

cat > "$smoke_dir/NuGet.config" <<EOF2
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="acp-local" value="$package_dir" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="acp-local">
      <package pattern="SalmonEgg.Acp" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="Microsoft.*" />
      <package pattern="System.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF2

console_dir="$smoke_dir/ConsoleSmoke"
mkdir -p "$console_dir"
cat > "$console_dir/ACPConsumerSmoke.csproj" <<EOF2
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SalmonEgg.Acp" Version="$package_version" />
  </ItemGroup>
</Project>
EOF2

cat > "$console_dir/Program.cs" <<'EOF2'
using System.Text.Json;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

var initialize = new InitializeParams(
    new ClientInfo("PackageConsumer", "1.0.0", "Package Consumer"),
    ClientCapabilityDefaults.Create());
var initializeJson = JsonSerializer.Serialize(initialize, AcpJsonContext.Default.InitializeParams);

using var initializeDocument = JsonDocument.Parse(initializeJson);
var initializeRoot = initializeDocument.RootElement;

Require(initialize.ProtocolVersion == AcpProtocolVersion.V1, "Default InitializeParams must use stable ACP v1.");
Require(
    initializeRoot.GetProperty("protocolVersion").GetInt32() == AcpProtocolVersion.V1,
    $"Default initialize wire payload must use protocolVersion 1: {initializeJson}");
Require(
    initializeRoot.TryGetProperty("clientInfo", out var clientInfo)
        && clientInfo.ValueKind == JsonValueKind.Object
        && clientInfo.GetProperty("name").GetString() == "PackageConsumer",
    $"Default initialize wire payload must include v1 clientInfo: {initializeJson}");
Require(
    initializeRoot.TryGetProperty("clientCapabilities", out var clientCapabilities)
        && clientCapabilities.ValueKind == JsonValueKind.Object,
    $"Default initialize wire payload must include v1 clientCapabilities: {initializeJson}");
Require(
    clientCapabilities.TryGetProperty("session", out var session)
        && session.ValueKind == JsonValueKind.Object,
    $"Default initialize wire payload must preserve v1 client session capabilities: {initializeJson}");
Require(
    !initializeRoot.TryGetProperty("info", out _)
        && !initializeRoot.TryGetProperty("capabilities", out _),
    $"Default initialize wire payload must not include ACP v2 fields: {initializeJson}");

Console.WriteLine(typeof(SessionListParams).FullName);

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
EOF2

echo "[smoke] Using ACP SDK package: $package_file"

# Restore is asserted on its own, before any build: a build that fails because the package could not
# be resolved looks identical to a build that fails for the reason a gate is testing for.
"$DOTNET_BIN" restore "$console_dir/ACPConsumerSmoke.csproj" --configfile "$smoke_dir/NuGet.config"

capture_build "$DOTNET_BIN" build "$console_dir/ACPConsumerSmoke.csproj" --configuration "$CONFIGURATION" --no-restore -v minimal
if [ "$build_rc" -ne 0 ]; then
  echo "[smoke] Stable-surface consumer failed to build." >&2
  cat "$build_log" >&2
  exit 1
fi
# The one direction that proves the draft markings did not spill onto the supported surface: a
# consumer touching only stable v1 API must see no draft diagnostic at all. Matched as a diagnostic
# rather than as a bare token, because a successful build echoes csc's own "nowarn:" argument at
# higher verbosities and a token match would invert this assertion the day someone raises -v.
if grep -qE "(warning|error) SEACP002" "$build_log"; then
  echo "[smoke] A stable-surface-only consumer must not see SEACP002; the marking has spilled onto stable API." >&2
  grep -E "(warning|error) SEACP002" "$build_log" >&2
  exit 1
fi

smoke_output="$("$DOTNET_BIN" run --project "$console_dir/ACPConsumerSmoke.csproj" --configuration "$CONFIGURATION" --no-build --no-restore -v minimal)"
if [ "$smoke_output" != "$expected_type_name" ]; then
  echo "Unexpected ACP consumer smoke output." >&2
  echo "Expected: $expected_type_name" >&2
  echo "Actual:   $smoke_output" >&2
  exit 1
fi

echo "[smoke] Runtime output: $smoke_output"
echo "[smoke] Stable-surface consumer builds clean and runs"

# --- Draft surface: every classified contract must refuse to compile unsuppressed -------------------
# One type would be a sample, not a gate. The whole classified set is generated from the manifest so
# a newly modeled draft contract is covered the moment it is classified.
draft_dir="$smoke_dir/DraftSmoke"
mkdir -p "$draft_dir"
cat > "$draft_dir/ACPDraftConsumerSmoke.csproj" <<EOF2
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SalmonEgg.Acp" Version="$package_version" />
  </ItemGroup>
</Project>
EOF2

write_draft_program() {
  local suppression="$1"
  {
    echo "// Generated by run-acp-consumer-package-smoke.sh from PublicSurface.Types.txt."
    if [ "$suppression" = "pragma" ]; then
      echo "#pragma warning disable SEACP002"
    fi
    while read -r type_name; do
      echo "_ = typeof($type_name);"
    done < "$draft_types"
    if [ "$suppression" = "pragma" ]; then
      echo "#pragma warning restore SEACP002"
    fi
    echo 'System.Console.WriteLine("draft-consumer-ok");'
  } > "$draft_dir/Program.cs"
}

write_draft_program none
"$DOTNET_BIN" restore "$draft_dir/ACPDraftConsumerSmoke.csproj" --configfile "$smoke_dir/NuGet.config"

capture_build "$DOTNET_BIN" build "$draft_dir/ACPDraftConsumerSmoke.csproj" --configuration "$CONFIGURATION" --no-restore -v minimal
require_no_infrastructure_failure "[smoke] Draft consumer"
if [ "$build_rc" -eq 0 ]; then
  echo "[smoke] Naming the v2 draft surface without suppression must not compile, but the build succeeded." >&2
  exit 1
fi

missing_diagnostics=0
while read -r type_name; do
  if ! grep -qF "error SEACP002: '$type_name'" "$build_log"; then
    echo "[smoke] No SEACP002 error for draft type $type_name" >&2
    missing_diagnostics=$((missing_diagnostics + 1))
  fi
done < "$draft_types"
if [ "$missing_diagnostics" -ne 0 ]; then
  echo "[smoke] $missing_diagnostics of $draft_count classified draft contracts compiled without a diagnostic." >&2
  echo "[smoke] SEACP002 diagnostics the compiler actually reported:" >&2
  grep "error SEACP002" "$build_log" | sed 's/ is for evaluation.*//' | sort -u >&2
  exit 1
fi

# Every diagnostic must be anchored to consumer source. Without this the count could be satisfied by
# errors raised inside the package's own generated code, which says nothing about what a consumer sees.
seacp_lines="$(count_in_build_log "error SEACP002")"
located_lines="$(count_in_build_log "Program\.cs\([0-9]+,[0-9]+\): error SEACP002")"
if [ "$seacp_lines" != "$located_lines" ] || [ "$located_lines" -eq 0 ]; then
  echo "[smoke] Expected every SEACP002 error to be reported at a Program.cs location: $located_lines of $seacp_lines were." >&2
  grep "error SEACP002" "$build_log" | grep -vE "Program\.cs\([0-9]+,[0-9]+\): error SEACP002" >&2 || true
  exit 1
fi

echo "[smoke] All $draft_count draft contracts refuse to compile unsuppressed"

# --- The documented opt-in must actually work ------------------------------------------------------
# README.md tells consumers to opt in with NoWarn or #pragma. Both are asserted, because a marking
# nobody can suppress is not an evaluation gate, it is a wall - and the package's documentation would
# be wrong.
capture_build "$DOTNET_BIN" build "$draft_dir/ACPDraftConsumerSmoke.csproj" --configuration "$CONFIGURATION" --no-restore -v minimal -p:NoWarn=SEACP002
require_no_infrastructure_failure "[smoke] Draft consumer with project NoWarn"
if [ "$build_rc" -ne 0 ]; then
  echo "[smoke] The documented project-level opt-in (NoWarn=SEACP002) did not build." >&2
  cat "$build_log" >&2
  exit 1
fi

write_draft_program pragma
capture_build "$DOTNET_BIN" build "$draft_dir/ACPDraftConsumerSmoke.csproj" --configuration "$CONFIGURATION" --no-restore -v minimal
require_no_infrastructure_failure "[smoke] Draft consumer with #pragma"
if [ "$build_rc" -ne 0 ]; then
  echo "[smoke] The documented per-region opt-in (#pragma warning disable SEACP002) did not build." >&2
  cat "$build_log" >&2
  exit 1
fi

draft_output="$("$DOTNET_BIN" run --project "$draft_dir/ACPDraftConsumerSmoke.csproj" --configuration "$CONFIGURATION" --no-build --no-restore -v minimal)"
if [ "$draft_output" != "draft-consumer-ok" ]; then
  echo "[smoke] Unexpected draft consumer runtime output: $draft_output" >&2
  exit 1
fi

echo "[smoke] Both documented suppressions compile and run"

echo "[smoke] ACP SDK package consumer smoke passed"
