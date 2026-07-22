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
nupkg="$(find "$package_dir" -maxdepth 1 -name 'SalmonEgg.Acp.*.nupkg' -print -quit)"
if [ -z "$nupkg" ]; then
  echo "ACP SDK package artifact not found in $package_dir" >&2
  exit 1
fi

package_file="$(basename "$nupkg")"
package_version="${package_file#SalmonEgg.Acp.}"
package_version="${package_version%.nupkg}"

smoke_dir="$(mktemp -d)"
trap 'rm -rf "$smoke_dir"' EXIT

cat > "$smoke_dir/ACPConsumerSmoke.csproj" <<EOF2
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

cat > "$smoke_dir/Program.cs" <<'EOF2'
using SalmonEgg.Acp.Protocol;

Console.WriteLine(typeof(SessionListParams).FullName);
EOF2

cat > "$smoke_dir/NuGet.config" <<EOF2
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="acp-local" value="$package_dir" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF2

"$DOTNET_BIN" restore "$smoke_dir/ACPConsumerSmoke.csproj" --configfile "$smoke_dir/NuGet.config"
"$DOTNET_BIN" build "$smoke_dir/ACPConsumerSmoke.csproj" --configuration "$CONFIGURATION" --no-restore -v minimal
