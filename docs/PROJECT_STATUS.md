# SalmonEgg Project Status

> Status snapshot: 2026-07-01. This file is a navigation/status index, not an architecture specification.

## Current Shape

- Product: Uno Platform / WinUI 3 ACP client for local and remote agent workflows.
- Main app project: `SalmonEgg/SalmonEgg/SalmonEgg.csproj`.
- Core layers: `src/SalmonEgg.Domain`, `src/SalmonEgg.Application`, `src/SalmonEgg.Infrastructure`, `src/SalmonEgg.Infrastructure.Desktop`, `src/SalmonEgg.Presentation.Core`.
- Test layers: cross-platform unit/behavior tests under `tests/*Tests` plus shared helpers in `tests/SalmonEgg.TestSupport`; Windows-only GUI and hardware/bridge validation under `tests/SalmonEgg.GuiTests.Windows` and `tests/SalmonEgg.GamepadBridge.Windows`; Skia Desktop and BrowserWasm GUI gates under `scripts/gates/`.

## Authoritative References

- Agent rules and delivery gates: `AGENTS.md`.
- Architecture: `docs/architecture.md`.
- Build and run commands: `BUILD_GUIDE.md`.
- Coding standards: `docs/coding-standards.md`.
- Session/navigation/search constraints: `docs/hard-constraints-session-navigation-and-search.md`.
- ACP standard and extension boundary: the official ACP specification plus the protocol rules in `AGENTS.md` and `docs/hard-constraints-session-navigation-and-search.md`.

## Status Policy

Do not use historical implementation plans as current project status unless they carry a current `Status:` header. Documents under `docs/superpowers/`, `docs/plans/`, and old audit remediation trackers may describe planned or completed work; verify against the current repo before treating them as live requirements.
