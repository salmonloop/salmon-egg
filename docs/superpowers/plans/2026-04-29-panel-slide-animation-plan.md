# Panel Slide-from-Edge Animation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add WinUI 3 Fluent Design "Slide from Edge + Fade" animation (167ms, CubicEase) to Bottom Panel and Right Panel open/close transitions in MainPage.

**Architecture:** Animation layer sits in `MainPage` code-behind as the last mile between `ShellLayoutViewModel.PropertyChanged` and `Visibility` mutation on the panel element. The SSOT pipeline (Action → Reducer → Policy → Snapshot → ViewModel) is untouched. Four Storyboards are defined in `Page.Resources`; dynamic `From`/`To` values are set in code-behind before each `Begin()` call.

**Tech Stack:** WinUI 3 Storyboard + DoubleAnimation + TranslateTransform + CubicEase; C# code-behind

---

## File Structure

| File | Responsibility |
|---|---|
| `MainPage.xaml` | Remove `Visibility` x:Bind from panels, add `BottomPanelTranslate` Transform, add 4 Storyboards |
| `MainPage.xaml.cs` | Animation state fields, panel open/close methods, Completed handlers, chat-exit suppression |

---

### Task 1: Add animation infrastructure to MainPage.xaml

**Files:**
- Modify: `SalmonEgg/SalmonEgg/MainPage.xaml`

- [ ] **Step 1: Add x:Name and RenderTransform to BottomPanelHost**

Remove the `Visibility` x:Bind from `BottomPanelHost`, add `x:Name`, and add `RenderTransform` with `TranslateTransform`.

Find (around line 520-526):
```xml
                        <chatViews:BottomPanelHost Grid.Row="1"
                                                   Visibility="{x:Bind LayoutVM.BottomPanelVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
                                                   TabsSource="{x:Bind ChatVM.BottomPanelTabs, Mode=OneWay}"
                                                   SelectedTab="{x:Bind ChatVM.SelectedBottomPanelTab, Mode=TwoWay}"
                                                   TerminalSessions="{x:Bind ChatVM.TerminalSessions, Mode=OneWay}"
                                                   SelectedTerminalSession="{x:Bind ChatVM.SelectedTerminalSession, Mode=OneWay}"
                                                   LocalTerminalSession="{x:Bind ChatVM.ActiveLocalTerminalSession, Mode=OneWay}" />
```

Replace with:
```xml
                        <chatViews:BottomPanelHost x:Name="BottomPanelHost"
                                                   Grid.Row="1"
                                                   TabsSource="{x:Bind ChatVM.BottomPanelTabs, Mode=OneWay}"
                                                   SelectedTab="{x:Bind ChatVM.SelectedBottomPanelTab, Mode=TwoWay}"
                                                   TerminalSessions="{x:Bind ChatVM.TerminalSessions, Mode=OneWay}"
                                                   SelectedTerminalSession="{x:Bind ChatVM.SelectedTerminalSession, Mode=OneWay}"
                                                   LocalTerminalSession="{x:Bind ChatVM.ActiveLocalTerminalSession, Mode=OneWay}">
                            <chatViews:BottomPanelHost.RenderTransform>
                                <TranslateTransform x:Name="BottomPanelTranslate" />
                            </chatViews:BottomPanelHost.RenderTransform>
                        </chatViews:BottomPanelHost>
```

- [ ] **Step 2: Remove Visibility x:Bind from RightPanelColumn**

Find (around line 569-572):
```xml
                <Grid x:Name="RightPanelColumn"
                  Grid.Column="1"
                  Visibility="{x:Bind LayoutVM.RightPanelVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
                  Background="{ThemeResource LayerFillColorDefaultBrush}"
```

Remove just the `Visibility` line, keeping everything else:
```xml
                <Grid x:Name="RightPanelColumn"
                  Grid.Column="1"
                  Background="{ThemeResource LayerFillColorDefaultBrush}"
```

- [ ] **Step 3: Add 4 Storyboards to Page.Resources**

Add the following inside the existing `<Page.Resources>` block, right before the closing `</Page.Resources>` tag (before line 276, after the `RightPanelResizerThumbStyle` closing `</Style>` at line 275):

```xml
        <!-- Panel slide-from-edge animations (Fluent "Fast" 167ms) -->
        <Storyboard x:Key="RightPanelSlideIn">
            <DoubleAnimation Storyboard.TargetName="RightPanelTranslate"
                             Storyboard.TargetProperty="X"
                             To="0" Duration="0:0:0.167">
                <DoubleAnimation.EasingFunction>
                    <CubicEase EasingMode="EaseOut" />
                </DoubleAnimation.EasingFunction>
            </DoubleAnimation>
            <DoubleAnimation Storyboard.TargetName="RightPanelColumn"
                             Storyboard.TargetProperty="Opacity"
                             To="1" Duration="0:0:0.167" />
        </Storyboard>

        <Storyboard x:Key="RightPanelSlideOut">
            <DoubleAnimation Storyboard.TargetName="RightPanelTranslate"
                             Storyboard.TargetProperty="X"
                             Duration="0:0:0.167">
                <DoubleAnimation.EasingFunction>
                    <CubicEase EasingMode="EaseIn" />
                </DoubleAnimation.EasingFunction>
            </DoubleAnimation>
            <DoubleAnimation Storyboard.TargetName="RightPanelColumn"
                             Storyboard.TargetProperty="Opacity"
                             To="0" Duration="0:0:0.167" />
        </Storyboard>

        <Storyboard x:Key="BottomPanelSlideUp">
            <DoubleAnimation Storyboard.TargetName="BottomPanelTranslate"
                             Storyboard.TargetProperty="Y"
                             To="0" Duration="0:0:0.167">
                <DoubleAnimation.EasingFunction>
                    <CubicEase EasingMode="EaseOut" />
                </DoubleAnimation.EasingFunction>
            </DoubleAnimation>
            <DoubleAnimation Storyboard.TargetName="BottomPanelHost"
                             Storyboard.TargetProperty="Opacity"
                             To="1" Duration="0:0:0.167" />
        </Storyboard>

        <Storyboard x:Key="BottomPanelSlideDown">
            <DoubleAnimation Storyboard.TargetName="BottomPanelTranslate"
                             Storyboard.TargetProperty="Y"
                             Duration="0:0:0.167">
                <DoubleAnimation.EasingFunction>
                    <CubicEase EasingMode="EaseIn" />
                </DoubleAnimation.EasingFunction>
            </DoubleAnimation>
            <DoubleAnimation Storyboard.TargetName="BottomPanelHost"
                             Storyboard.TargetProperty="Opacity"
                             To="0" Duration="0:0:0.167" />
        </Storyboard>
```

- [ ] **Step 4: Commit**

```bash
git add SalmonEgg/SalmonEgg/MainPage.xaml
git commit -m "feat: add panel slide animation Storyboards and Transforms to MainPage.xaml

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 2: Add animation state fields to MainPage.xaml.cs

**Files:**
- Modify: `SalmonEgg/SalmonEgg/MainPage.xaml.cs`

- [ ] **Step 1: Add animation state fields**

Add the following fields after the existing `_leftNavResizeStartWidth` field (around line 61):

```csharp
    private bool _isRightPanelAnimating;
    private bool _isBottomPanelAnimating;
    private double _lastRightPanelWidth;
    private double _lastBottomPanelHeight;
    private bool _suppressPanelAnimations;
    private bool _pendingRightPanelToggle;
    private bool _pendingBottomPanelToggle;
```

- [ ] **Step 2: Commit**

```bash
git add SalmonEgg/SalmonEgg/MainPage.xaml.cs
git commit -m "feat: add panel animation state fields to MainPage

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 3: Add panel animation methods to MainPage.xaml.cs

**Files:**
- Modify: `SalmonEgg/SalmonEgg/MainPage.xaml.cs`

- [ ] **Step 1: Add animation helper methods**

Add the following methods to the `MainPage` class. Place them after the existing `EndLeftNavResize` method (after its closing brace around line 1003), before the `OnLayoutViewModelPropertyChanged` method:

```csharp
    private void AnimateRightPanelOpen()
    {
        if (!UiMotion.Current.IsAnimationEnabled)
        {
            RightPanelColumn.Visibility = Visibility.Visible;
            return;
        }

        _lastRightPanelWidth = LayoutVM.RightPanelWidth;
        if (_lastRightPanelWidth <= 0)
        {
            RightPanelColumn.Visibility = Visibility.Visible;
            return;
        }

        RightPanelTranslate.X = _lastRightPanelWidth;
        RightPanelColumn.Opacity = 0;
        RightPanelColumn.Visibility = Visibility.Visible;

        var sb = (Microsoft.UI.Xaml.Media.Animation.Storyboard)Resources["RightPanelSlideIn"];
        sb.Begin();
        _isRightPanelAnimating = true;

        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            sb.Completed -= handler;
            _isRightPanelAnimating = false;

            if (_pendingRightPanelToggle)
            {
                _pendingRightPanelToggle = false;
                AnimateRightPanelClose();
            }
        };
        sb.Completed += handler;
    }

    private void AnimateRightPanelClose()
    {
        if (!UiMotion.Current.IsAnimationEnabled)
        {
            RightPanelColumn.Visibility = Visibility.Collapsed;
            return;
        }

        if (_lastRightPanelWidth <= 0)
        {
            RightPanelColumn.Visibility = Visibility.Collapsed;
            return;
        }

        var sb = (Microsoft.UI.Xaml.Media.Animation.Storyboard)Resources["RightPanelSlideOut"];
        var translateAnim = (Microsoft.UI.Xaml.Media.Animation.DoubleAnimation)sb.Children[0];
        translateAnim.To = _lastRightPanelWidth;

        sb.Begin();
        _isRightPanelAnimating = true;

        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            sb.Completed -= handler;
            RightPanelColumn.Visibility = Visibility.Collapsed;
            _isRightPanelAnimating = false;

            if (_pendingRightPanelToggle)
            {
                _pendingRightPanelToggle = false;
                AnimateRightPanelOpen();
            }
        };
        sb.Completed += handler;
    }

    private void AnimateBottomPanelOpen()
    {
        if (!UiMotion.Current.IsAnimationEnabled)
        {
            BottomPanelHost.Visibility = Visibility.Visible;
            return;
        }

        _lastBottomPanelHeight = LayoutVM.BottomPanelHeight;
        if (_lastBottomPanelHeight <= 0)
        {
            BottomPanelHost.Visibility = Visibility.Visible;
            return;
        }

        BottomPanelTranslate.Y = _lastBottomPanelHeight;
        BottomPanelHost.Opacity = 0;
        BottomPanelHost.Visibility = Visibility.Visible;

        var sb = (Microsoft.UI.Xaml.Media.Animation.Storyboard)Resources["BottomPanelSlideUp"];
        sb.Begin();
        _isBottomPanelAnimating = true;

        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            sb.Completed -= handler;
            _isBottomPanelAnimating = false;

            if (_pendingBottomPanelToggle)
            {
                _pendingBottomPanelToggle = false;
                AnimateBottomPanelClose();
            }
        };
        sb.Completed += handler;
    }

    private void AnimateBottomPanelClose()
    {
        if (!UiMotion.Current.IsAnimationEnabled)
        {
            BottomPanelHost.Visibility = Visibility.Collapsed;
            return;
        }

        if (_lastBottomPanelHeight <= 0)
        {
            BottomPanelHost.Visibility = Visibility.Collapsed;
            return;
        }

        var sb = (Microsoft.UI.Xaml.Media.Animation.Storyboard)Resources["BottomPanelSlideDown"];
        var translateAnim = (Microsoft.UI.Xaml.Media.Animation.DoubleAnimation)sb.Children[0];
        translateAnim.To = _lastBottomPanelHeight;

        sb.Begin();
        _isBottomPanelAnimating = true;

        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            sb.Completed -= handler;
            BottomPanelHost.Visibility = Visibility.Collapsed;
            _isBottomPanelAnimating = false;

            if (_pendingBottomPanelToggle)
            {
                _pendingBottomPanelToggle = false;
                AnimateBottomPanelOpen();
            }
        };
        sb.Completed += handler;
    }
```

- [ ] **Step 2: Commit**

```bash
git add SalmonEgg/SalmonEgg/MainPage.xaml.cs
git commit -m "feat: add panel slide animation methods to MainPage

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 4: Wire animation into OnLayoutViewModelPropertyChanged

**Files:**
- Modify: `SalmonEgg/SalmonEgg/MainPage.xaml.cs`

- [ ] **Step 1: Add panel visibility gating to OnLayoutViewModelPropertyChanged**

Add the following code at the **start** of the existing `OnLayoutViewModelPropertyChanged` method body, before the existing `if (e.PropertyName == nameof(ShellLayoutViewModel.IsNavPaneOpen))` check (around line 1013):

```csharp
        if (_suppressPanelAnimations)
        {
            if (e.PropertyName == nameof(ShellLayoutViewModel.RightPanelVisible))
            {
                RightPanelColumn.Visibility = LayoutVM.RightPanelVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (e.PropertyName == nameof(ShellLayoutViewModel.BottomPanelVisible))
            {
                BottomPanelHost.Visibility = LayoutVM.BottomPanelVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (!LayoutVM.RightPanelVisible && !LayoutVM.BottomPanelVisible)
            {
                _suppressPanelAnimations = false;
            }

            return;
        }

        if (e.PropertyName == nameof(ShellLayoutViewModel.RightPanelVisible))
        {
            if (LayoutVM.RightPanelVisible && !_isRightPanelAnimating)
            {
                AnimateRightPanelOpen();
            }
            else if (!LayoutVM.RightPanelVisible && !_isRightPanelAnimating)
            {
                AnimateRightPanelClose();
            }
            else if (_isRightPanelAnimating)
            {
                _pendingRightPanelToggle = true;
            }
        }

        if (e.PropertyName == nameof(ShellLayoutViewModel.BottomPanelVisible))
        {
            if (LayoutVM.BottomPanelVisible && !_isBottomPanelAnimating)
            {
                AnimateBottomPanelOpen();
            }
            else if (!LayoutVM.BottomPanelVisible && !_isBottomPanelAnimating)
            {
                AnimateBottomPanelClose();
            }
            else if (_isBottomPanelAnimating)
            {
                _pendingBottomPanelToggle = true;
            }
        }
```

- [ ] **Step 2: Commit**

```bash
git add SalmonEgg/SalmonEgg/MainPage.xaml.cs
git commit -m "feat: wire panel slide animations into ShellLayoutViewModel PropertyChanged

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 5: Add chat-exit panel suppression

**Files:**
- Modify: `SalmonEgg/SalmonEgg/MainPage.xaml.cs`

- [ ] **Step 1: Update ResetChatAuxiliaryPanelsOnChatExit to suppress animations**

Replace the existing method (around lines 417-420):
```csharp
    private void ResetChatAuxiliaryPanelsOnChatExit()
    {
        _metricsSink.ReportClearAuxiliaryPanels();
    }
```

With:
```csharp
    private void ResetChatAuxiliaryPanelsOnChatExit()
    {
        _suppressPanelAnimations = true;

        if (_isRightPanelAnimating)
        {
            ((Microsoft.UI.Xaml.Media.Animation.Storyboard)Resources["RightPanelSlideIn"]).Stop();
            ((Microsoft.UI.Xaml.Media.Animation.Storyboard)Resources["RightPanelSlideOut"]).Stop();
            RightPanelColumn.Visibility = Visibility.Collapsed;
            _isRightPanelAnimating = false;
        }

        if (_isBottomPanelAnimating)
        {
            ((Microsoft.UI.Xaml.Media.Animation.Storyboard)Resources["BottomPanelSlideUp"]).Stop();
            ((Microsoft.UI.Xaml.Media.Animation.Storyboard)Resources["BottomPanelSlideDown"]).Stop();
            BottomPanelHost.Visibility = Visibility.Collapsed;
            _isBottomPanelAnimating = false;
        }

        if (!LayoutVM.RightPanelVisible && !LayoutVM.BottomPanelVisible)
        {
            _suppressPanelAnimations = false;
        }

        _metricsSink.ReportClearAuxiliaryPanels();
    }
```

- [ ] **Step 2: Add using for Animation namespace if not present**

Verify that `using Microsoft.UI.Xaml.Media.Animation;` is already in the using block (line 19 of the current file). No change needed — it's already imported.

- [ ] **Step 3: Commit**

```bash
git add SalmonEgg/SalmonEgg/MainPage.xaml.cs
git commit -m "feat: suppress panel animations during chat exit navigation

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 6: Build and run existing tests to verify no regression

**Files:**
- (no file changes)

- [ ] **Step 1: Build the project**

Run: `dotnet build SalmonEgg/SalmonEgg/SalmonEgg.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 2: Run ShellLayout policy and ViewModel tests**

Run: `dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ShellLayout" --verbosity normal`
Expected: All ShellLayout tests pass (Policy, ViewModel, reducer).

- [ ] **Step 3: Run XAML compliance and conventions tests**

Run: `dotnet test tests/SalmonEgg.Application.Tests/SalmonEgg.Application.Tests.csproj --filter "FullyQualifiedName~ShellAuxiliaryPanels" --verbosity normal`
Expected: `ShellAuxiliaryPanels_ShouldRenderBottomPanelOutsideChatOverlayBlock` passes.

Run: `dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ChatViewXaml" --verbosity normal`
Expected: `ChatViewXaml_BindsTerminalPanelStateIntoBottomPanelHost` passes (bindings other than Visibility are unchanged).

- [ ] **Step 4: Run full test suite**

Run: `dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --verbosity normal`
Expected: All tests pass.
