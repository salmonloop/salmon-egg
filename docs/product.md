# Product

## Register

product

## Users

SalmonEgg is for developers and operators who use ACP-compatible coding agents across local and remote environments. They need a dependable cross-platform client where navigation state, session identity, protocol state, and local capabilities stay understandable under repeated use.

## Product Purpose

The product provides a native-feeling Uno/WinUI shell for ACP conversations, session discovery, configuration, diagnostics, and local workflow affordances. Success means the same user intent produces the same observable result on MSIX, desktop, and browser WASM unless a platform capability is explicitly unavailable.

## Brand Personality

Precise, quiet, capable. The interface should feel like an engineering tool that respects attention and native platform expectations.

## Anti-references

Avoid marketing-page composition, decorative motion, custom controls that replace native behavior, platform-specific visual tricks in shared UI, and any second state owner that makes the UI look correct while protocol or platform facts disagree.

## Design Principles

- Native first: use Uno/WinUI controls and platform services as the behavior owner wherever possible.
- Single source of truth: settings, navigation selection, session identity, capability flags, and resource tags each have one authoritative owner.
- Explicit capability boundaries: unsupported platform features are disabled or rejected before side effects, not silently approximated.
- Dense but calm: prioritize repeatable task flow, scannable panels, and restrained visual hierarchy.
- Verification follows artifacts: GUI and deployment checks must identify the exact build output or remote URL they exercised.

## Accessibility & Inclusion

Use native accessibility semantics, focus behavior, keyboard navigation, reduced-motion preferences, and localized resources. Do not replace native selection, focus, or announcement behavior with pointer-level compensation.
