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

## 5) Use Lightweight Styling over ControlTemplate hacks

When adjusting specific control states (such as changing a button's hover color or hiding a TextBox's focus border), **do not** extract and override the entire `ControlTemplate`, and **do not** use hacky visual tree manipulations from code-behind. This causes semantic drift from future OS updates and inflates the codebase.

**Official Rationale (Non-Hack)**
* **Microsoft Recommended Practice:** [Lightweight Styling](https://learn.microsoft.com/en-us/windows/apps/design/style/xaml-styles#lightweight-styling) is the official mechanism provided by WinUI/UWP to customize internal states of controls.
* **Why it is elegant:** Instead of replacing a massive 500-line default `ControlTemplate` (which freezes the visual appearance to the current OS version), Lightweight Styling leverages standard XAML resource resolution. By safely injecting transparent brushes or zero-thickness values into the local `<Control.Resources>` scope, the framework natively picks up our local overrides while continuing to receive underlying layout/animation updates from the OS.

**Rule**

- Always use **Lightweight Styling** (轻量级样式) by overriding the specific `ThemeResource` keys locally in `<Control.Resources>`.

**Good**

```xml
<TextBox>
    <TextBox.Resources>
        <!-- Elegantly override the system's focused background and bottom border thickness purely via resource scope -->
        <SolidColorBrush x:Key="TextControlBackgroundFocused" Color="Transparent" />
        <Thickness x:Key="TextControlBorderThemeThicknessFocused">0</Thickness>
    </TextBox.Resources>
</TextBox>
```

## 6) Automated guardrails

Unit tests under `tests/SalmonEgg.Application.Tests/UiConventionsTests.cs` enforce:

- DI-resolved VMs occur before `InitializeComponent()` in `*.xaml.cs`
- `ChatViewModel` is registered as singleton
- Legacy `SystemControl*Highlight*` resource keys are not used in XAML

