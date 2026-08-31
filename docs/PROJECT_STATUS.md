# SalmonEgg Project Status

> Status snapshot: 2026-08-23. This file is a navigation/status index, not an architecture specification.

## Current Shape

- Product: Uno Platform / WinUI 3 ACP client for local and remote agent workflows.
- Main app project: `SalmonEgg/SalmonEgg/SalmonEgg.csproj`.
- Core layers: `src/SalmonEgg.Domain`, `src/SalmonEgg.Application`, `src/SalmonEgg.Infrastructure`, `src/SalmonEgg.Infrastructure.Desktop`, `src/SalmonEgg.Presentation.Core`.
- Standalone delivery units: `src/SalmonEgg.Acp` (packable protocol SDK) and `src/SalmonEgg.Cli` (configuration management CLI).
- Test layers: cross-platform unit/behavior tests under `tests/*Tests` plus shared helpers in `tests/SalmonEgg.TestSupport`; Windows-only GUI and hardware/bridge validation under `tests/SalmonEgg.GuiTests.Windows` and `tests/SalmonEgg.GamepadBridge.Windows`; Skia Desktop and BrowserWasm GUI gates under `scripts/gates/`.

## Authoritative References

- Agent rules and delivery gates: `AGENTS.md`.
- Architecture: `docs/architecture.md`.
- Build and run commands: `BUILD_GUIDE.md`.
- Documentation index: `docs/README.md`.
- Coding standards: `docs/coding-standards.md`.
- Session/navigation/search constraints: `docs/hard-constraints-session-navigation-and-search.md`.
- ACP standard and extension boundary: the official ACP specification plus the protocol rules in `AGENTS.md` and `docs/hard-constraints-session-navigation-and-search.md`.

## Status Policy

历史实现计划不属于当前项目状态；临时计划、脚手架记录和已完成计划已清理。需要核对历史结论时，应以当前代码、测试和本文件列出的权威文档为准。
