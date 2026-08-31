#!/usr/bin/env bash
set -euo pipefail

PACKAGE_DIR="${1:?Package directory is required}"
CONFIGURATION="${2:-Release}"

DOTNET_BIN="${DOTNET_BIN:-dotnet}"

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

"$DOTNET_BIN" restore "$console_dir/ACPConsumerSmoke.csproj" --configfile "$smoke_dir/NuGet.config"
"$DOTNET_BIN" build "$console_dir/ACPConsumerSmoke.csproj" --configuration "$CONFIGURATION" --no-restore -v minimal
smoke_output="$("$DOTNET_BIN" run --project "$console_dir/ACPConsumerSmoke.csproj" --configuration "$CONFIGURATION" --no-build --no-restore -v minimal)"
if [ "$smoke_output" != "$expected_type_name" ]; then
  echo "Unexpected ACP consumer smoke output." >&2
  echo "Expected: $expected_type_name" >&2
  echo "Actual:   $smoke_output" >&2
  exit 1
fi

echo "[smoke] Runtime output: $smoke_output"

echo "[smoke] ACP SDK package consumer smoke passed"
