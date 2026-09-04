# Salmon Egg

[简体中文](README.md)

Salmon Egg is a desktop agent client built around the Agent Client Protocol (ACP).

It brings conversational AI, local tools, terminal workflows, and remote agent services into a single workspace, reducing the need to jump between multiple apps and windows.

With Salmon Egg, you can connect to local or remote ACP services, create and resume sessions, review conversation history and tool-call results, and handle terminal-driven workflows directly inside the app. For day-to-day use, the app also includes voice input, personalized settings, and diagnostics support.

## What It Is For

- Using ACP-powered agents reliably on the Windows desktop
- Keeping agent interactions and local tool workflows in one place
- Reviewing sessions, tool results, and terminal output in a unified UI

## Core Capabilities

- Connect to local or remote ACP services
- Create, resume, and manage sessions
- Display conversations, tool calls, and result feedback
- Support local terminal and subprocess workflows
- Support voice input
- Provide settings, logging, and diagnostics

## Tech Stack

- Uno Platform 6.7+ (repo pin: `Uno.Sdk` 6.7.22)
- .NET 10 (repo pin: SDK 10.0.302, with 10.0.3xx patch roll-forward)
- WinUI 3 on Windows
- Clean Architecture + MVVM

## Repository Layout

```text
SalmonEgg/
├── SalmonEgg/SalmonEgg/          # Uno Platform app project
├── src/
│   ├── SalmonEgg.Domain/         # Domain layer
│   ├── SalmonEgg.Application/    # Application layer
│   ├── SalmonEgg.Infrastructure/ # Infrastructure layer
│   ├── SalmonEgg.Infrastructure.Desktop/
│   ├── SalmonEgg.Presentation.Core/
│   ├── SalmonEgg.Acp/             # Standalone ACP protocol SDK
│   └── SalmonEgg.Cli/             # Configuration management CLI
├── tests/
└── docs/
```

## Quick Start

For environment requirements and detailed build steps, start with [BUILD_GUIDE.md](BUILD_GUIDE.md).

### Requirements

- .NET SDK **10.0.302** or a compatible **10.0.3xx** patch (see `global.json`)
- Windows 10 1809+ / Windows 11 for WinUI 3 and MSIX validation; on Windows prefer Visual Studio **18.8+**
- Or an equivalent command-line toolchain (Linux/macOS can build Desktop / WASM)

### Common Commands

```bash
# Restore dependencies
dotnet restore SalmonEgg.sln

# Build the solution
dotnet build SalmonEgg.sln --configuration Release

# Run tests
dotnet test --solution SalmonEgg.sln

# Validate the native Windows MSIX package
build.bat msix
```

### CLI configuration management

The repository includes a cross-platform desktop CLI for server configuration and credential management.

#### Supported platforms and installation

The CLI ships with the app: **installing SalmonEgg registers the `salmon-egg` command**. There is nothing
separate to install, and no .NET runtime is required (the command is a self-contained single-file build).

| Installer | How the command is registered |
|---|---|
| Windows MSIX | the package declares an app execution alias, and Windows materializes it under `%LOCALAPPDATA%\Microsoft\WindowsApps`, a directory already on your user PATH |
| Windows MSI (Skia Desktop) | the MSI's `Environment` table appends the install folder's `cli` directory to your user PATH, and removes it on uninstall |
| Linux `.deb` | dpkg installs a `/usr/bin/salmon-egg` symlink and removes it on purge |
| macOS `.pkg` | the installer links the command into `/usr/local/bin`, which is on the default macOS PATH; remove it with `rm /usr/local/bin/salmon-egg` |
| macOS `.dmg` | the command is inside `SalmonEgg.app`, but a dragged app has no install hook, so link it yourself or use the `.pkg` |

The app shows this too: **Settings → Command line** resolves PATH live, reports which copy it found and whether its version matches, and on macOS offers to link or unlink the `/usr/local/bin` entry.


Other runtime identifiers — `win-arm64`, `linux-arm64`, `osx-x64` — are not officially supported: they can be cross-compiled, but nothing verifies them on a real machine.

After installing, the command is available directly:

```bash
salmon-egg --help
salmon-egg config server list
```

#### Credential storage

Credential writes are fail-closed. If the platform secret store is unavailable, the write fails instead of silently downgrading to a plaintext file. Pass `--allow-insecure-storage` to accept the downgrade; the CLI still reports it on stderr. Non-credential configuration commands are unaffected either way.

This matters on Linux (Secret Service) and macOS (Keychain), where the store can be missing or locked. On Windows the flag is inert: DPAPI needs no keyring daemon and is always available.

```bash
# Refuses rather than writing the token unprotected
printf '%s\n' "$AGENT_TOKEN" | salmon-egg set-credential <server-id> --token-stdin

# Explicitly accepts plaintext storage
printf '%s\n' "$AGENT_TOKEN" | salmon-egg --allow-insecure-storage set-credential <server-id> --token-stdin
```

#### Running from source

The examples below use `dotnet run` so they work in a checkout without installing anything. Replace the `dotnet run --project ... --` prefix with `salmon-egg` when using an installed build.

```bash
# Show the command tree
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- --help

# Show CLI assembly version
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- --version

# Explore server configuration commands
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- config server --help

# Add a server with non-sensitive proxy settings and a credential read from stdin.
# Credential values never enter argv, shell history, or YAML.
printf '%s\n' "$AGENT_TOKEN" | dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- config server add \
  --name "Example Agent" --url "https://agent.example" --transport streamable_http \
  --token-stdin --proxy-mode custom --proxy-url "http://proxy.example:8080"

# Add a stdio server. --stdio-args takes one quoted command-line string;
# attached form preserves dash-prefixed agent arguments and inner quoting.
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- config server add \
  --name "Local Stdio Agent" --transport stdio --stdio-command "agent" \
  --stdio-args="--serve -T --mode plan"

# Update it with a new argument string; use --stdio-args="" to clear arguments.
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- config server update <server-id> \
  --stdio-args="--serve --mode strict"

# Existing server commands use show/remove (remove requires --yes).
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- config server show <server-id>
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- config server remove <server-id> --yes

# Register exactly one credential kind for an existing server. Each command reads one stdin line.
printf '%s\n' "$AGENT_TOKEN" | dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- set-credential <server-id> --token-stdin
printf '%s\n' "$AGENT_API_KEY" | dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- set-credential <server-id> --api-key-stdin

# Check presence without printing credential values, then clear both keys
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- has-credential <server-id>
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- clear-credential <server-id>

# Show credential command guidance
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- credentials --help
```

The CLI targets `net10.0` and intentionally does not reference the Uno/WinUI application project. It reuses the desktop configuration composition root, including the existing platform secure-storage selection, but chooses a fail-closed downgrade policy where the GUI chooses a permissive one — a scripted invocation cannot react to a warning stream in time, so it must not persist credentials unprotected without being asked. Credential values are read from stdin and stored through `ISecureStorage`; they never enter process arguments, server YAML, or `has-credential` output. Invalid command lines return exit code `2`; state and persistence failures return `1`; successful commands return `0`.

Release packaging, the supported runtime identifiers, and the install/PATH gates are documented in [docs/release-guide.md](docs/release-guide.md).

## Documentation

- [Documentation index](docs/README.md)
- [Build Guide](BUILD_GUIDE.md)
- [Coding Standards](docs/coding-standards.md)
- [Session / Navigation / Search Constraints](docs/hard-constraints-session-navigation-and-search.md)

## Notes

Windows Store and MSIX submission should follow the WinUI 3 MSIX packaging flow in this repository. A plain `dotnet build` is not the authoritative validation path for the native Windows package.
