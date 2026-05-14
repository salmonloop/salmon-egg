# Design

## System

SalmonEgg is a restrained product UI. The visual system follows Uno/WinUI defaults first, then adds only app-level spacing, shell structure, and semantic states needed for the ACP workflow.

## Typography

Use the platform/system UI font stack through Uno and WinUI. Keep labels, command text, and dense panels compact; reserve larger type for page or panel section headings.

## Color

Use system theme resources and semantic brushes for text, surfaces, borders, selection, warning, success, and critical states. Accent color is for current selection, primary action, and state emphasis only.

## Layout

The shell is a task surface: navigation pane, content frame, optional side/bottom panels, and settings pages. Prefer native adaptive controls and stable layout constraints over pixel nudges. Cards are only for individual grouped settings or repeated items, never nested as page structure.

## Components

Prefer Uno/WinUI-native controls: NavigationView, Frame, ComboBox, ToggleSwitch, InfoBar, Expander, ListView, AutoSuggestBox, NumberBox, and Buttons. Shared UI binds to ViewModels and platform services; platform-native types stay in platform implementations, partial classes, or UI adapters.

## Motion

Motion communicates state change only. Respect the app motion preference and use native page transitions or suppressed transitions through the shared motion policy.

## Cross-Platform Rules

MSIX is the baseline for native behavior. Non-Windows targets rely on Uno mappings unless a platform capability source declares the feature unavailable. Shared UI must not invent fallback behavior that conflicts with platform services, resource catalogs, or protocol state.
