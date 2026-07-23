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
      <package pattern="NETStandard.Library*" />
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
using SalmonEgg.Acp.Protocol;

Console.WriteLine(typeof(SessionListParams).FullName);
EOF2

netstandard_dir="$smoke_dir/NetStandardSmoke"
mkdir -p "$netstandard_dir"
cat > "$netstandard_dir/ACPConsumerSmoke.NetStandard.csproj" <<EOF2
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SalmonEgg.Acp" Version="$package_version" />
  </ItemGroup>
</Project>
EOF2

cat > "$netstandard_dir/AcpSmoke.cs" <<'EOF2'
using SalmonEgg.Acp.Protocol;

namespace ACPConsumerSmoke
{
    public static class AcpSmoke
    {
        public static string TypeName => typeof(SessionListParams).FullName ?? string.Empty;
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

"$DOTNET_BIN" restore "$netstandard_dir/ACPConsumerSmoke.NetStandard.csproj" --configfile "$smoke_dir/NuGet.config"
"$DOTNET_BIN" build "$netstandard_dir/ACPConsumerSmoke.NetStandard.csproj" --configuration "$CONFIGURATION" --no-restore -v minimal

echo "[smoke] ACP SDK package consumer smoke passed"
