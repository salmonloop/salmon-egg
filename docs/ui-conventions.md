# UI Conventions (Uno / WinUI3 / Skia)

This repo targets multiple UI backends:

- **WinUI 3 (Windows/MSIX)**: `net10.0-windows...`
- **Skia (cross-platform desktop)**: `net10.0-desktop`

To reduce “semantic drift” between targets (layout, state, behavior), follow these conventions.

## 1) `x:Bind` + ViewModel initialization order

`x:Bind` is **compile-time binding**. If the property used by `x:Bind` is assigned *after* `InitializeComponent()`, the binding may never see the value (and UI appears “not bound”).

**Rule**

- If a page/control resolves view models (DI) and uses `x:Bind`, the VM must be assigned **before** calling `InitializeComponent()`.

**Good**

```csharp
ViewModel = App.ServiceProvider.GetRequiredService<MyViewModel>();
InitializeComponent();
```

**Bad**

```csharp
InitializeComponent();
ViewModel = App.ServiceProvider.GetRequiredService<MyViewModel>();
```

## 2) ViewModel lifetimes: shared state must be singleton

If two pages need to share state (e.g. connection/session), the view model must be registered as **singleton**.

Example: `ChatViewModel` is a singleton so Settings and Chat pages share a single session/connection state across navigation.

## 3) Prefer app semantic resources over platform keys

Avoid legacy/unstable platform keys such as `SystemControlHighlightLowBrush`. They may:

- not exist on some targets,
- be resolved differently by Uno’s resource resolver,
- cause warnings and visual drift.

**Rule**

- Use WinUI3 token resources (e.g. `TextFillColorPrimaryBrush`, `ControlFillColor*`, `DividerStrokeColorDefaultBrush`)
- Or better: define **app semantic brushes** (`AccentBrush`, `SurfaceBrush`, etc.) and use those across pages.

## 4) Keep platform `#if` out of page semantics

Platform conditional compilation is acceptable for truly platform-specific integrations (system backdrops, windowing), but avoid it for core UI semantics (navigation/state/layout).

## 5) Automated guardrails

Unit tests under `tests/SalmonEgg.Application.Tests/UiConventionsTests.cs` enforce:

- DI-resolved VMs occur before `InitializeComponent()` in `*.xaml.cs`
- `ChatViewModel` is registered as singleton
- Legacy `SystemControl*Highlight*` resource keys are not used in XAML

