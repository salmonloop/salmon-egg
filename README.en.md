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

- Uno Platform 6.6+ (repo pin: `Uno.Sdk` 6.6.29)
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
│   └── SalmonEgg.Presentation.Core/
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

```bash
# Show the command tree
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- --help

# Show CLI assembly version
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- --version

# Explore server configuration commands
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- config server --help

# Add a server with non-sensitive proxy settings and a credential.
# Credential values are persisted through the shared configuration service and never enter YAML.
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- config server add \
  --name "Example Agent" --url "https://agent.example" --transport streamable_http \
  --token "$AGENT_TOKEN" --proxy-mode custom --proxy-url "http://proxy.example:8080"

# Existing server commands use show/remove (remove requires --yes).
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- config server show <server-id>
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- config server remove <server-id> --yes

# Register exactly one credential kind for an existing server
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- set-credential <server-id> --token <value>
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- set-credential <server-id> --api-key <value>

# Check presence without printing credential values, then clear both keys
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- has-credential <server-id>
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- clear-credential <server-id>

# Show credential command guidance
dotnet run --project src/SalmonEgg.Cli/SalmonEgg.Cli.csproj -- credentials --help
```

The CLI targets `net10.0` and intentionally does not reference the Uno/WinUI application project. It reuses the desktop configuration composition root, including the existing platform secure-storage selection and plaintext fallback behavior. Credential values are stored through `ISecureStorage`, never written to server YAML, and never returned by `has-credential`. Invalid command lines return exit code `2`; unexpected host failures return `1`; successful commands return `0`.

## Documentation

- [Build Guide](BUILD_GUIDE.md)
- [Coding Standards](docs/coding-standards.md)
- [Session / Navigation / Search Constraints](docs/hard-constraints-session-navigation-and-search.md)

## Notes

Windows Store and MSIX submission should follow the WinUI 3 MSIX packaging flow in this repository. A plain `dotnet build` is not the authoritative validation path for the native Windows package.
