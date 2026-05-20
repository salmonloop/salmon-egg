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

- Uno Platform 6.5+
- .NET 10
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

- .NET SDK 10.0
- Windows 10 1809+ / Windows 11 for WinUI 3 and MSIX validation
- Visual Studio 2022 17.12+ or an equivalent command-line toolchain

### Common Commands

```bash
# Restore dependencies
dotnet restore SalmonEgg.sln

# Build the solution
dotnet build SalmonEgg.sln --configuration Release

# Run tests
dotnet test SalmonEgg.sln

# Validate the native Windows MSIX package
build.bat msix
```

## Documentation

- [Build Guide](BUILD_GUIDE.md)
- [Coding Standards](docs/coding-standards.md)
- [Session / Navigation / Search Constraints](docs/hard-constraints-session-navigation-and-search.md)

## Notes

Windows Store and MSIX submission should follow the WinUI 3 MSIX packaging flow in this repository. A plain `dotnet build` is not the authoritative validation path for the native Windows package.
