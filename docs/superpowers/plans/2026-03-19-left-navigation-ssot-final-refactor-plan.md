# Left Navigation SSOT/MVVM Final Refactor Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the left shell navigation so session selection, compact/expanded visual projection, and chat-session activation all flow from a single semantic source of truth without `MainPage` carrying ad-hoc state-machine logic.

**Architecture:** Keep `MainNavigationViewModel` as the only semantic owner of navigation intent and selection state. Add an explicit navigation projection/coordinator layer that converts semantic selection into view-facing projection for `NavigationView`, including compact ancestor emphasis and session activation, while keeping `MainPage` as a thin control adapter only. `Project` nodes remain grouping concepts rather than true navigation destinations.

**Tech Stack:** Uno Platform, WinUI 3 `NavigationView`, CommunityToolkit.Mvvm, Uno/Reactive MVUX shell layout state, xUnit.

---

## File Map

**Semantic state / projection**
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs`
  - Own semantic nav selection only.
  - Stop leaking control-projection decisions into `SelectedItem` usage.
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavItemViewModel.cs`
  - Keep only item-level projection properties that are UI-agnostic.
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavItemViewModels.cs`
  - Expose explicit compact/ancestor indicator state from projection.
- Create: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavigationSelectionState.cs`
  - Strongly typed semantic selection state (`Start`, `Settings`, `Session(sessionId)`).
- Create: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavigationViewProjection.cs`
  - Explicit view projection model for `NavigationView`.
- Create: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationSelectionProjector.cs`
  - Pure projector from semantic selection + tree + shell pane state -> projected view state.

**Navigation coordination**
- Create: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\INavigationCoordinator.cs`
- Create: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationCoordinator.cs`
  - Single intent entry point for start/settings/session/project actions.
  - Coordinate NavVM selection + chat activation + shell content navigation.
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavItemTag.cs`
  - Keep only deterministic semantic tags.

**View adapter**
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml`
  - Consume only projected compact/expanded visual state.
  - Keep templates native-looking.
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml.cs`
  - Reduce to thin adapter that forwards one user-intent path and applies one projection path.

**Tests**
- Modify: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelSelectionTests.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelPaneTests.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\NavigationCoreTests.cs`
- Create: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\NavigationSelectionProjectorTests.cs`
- Create: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\NavigationCoordinatorTests.cs`

**Reference docs**
- Review: `C:\Users\shang\Project\salmon-acp\docs\superpowers\plans\2026-03-18-navigation-selection-ssot-plan.md`
- Review: `C:\Users\shang\Project\salmon-acp\docs\superpowers\specs\2026-03-18-shell-layout-ssot-design.md`

---

## Chunk 1: Freeze Semantic Selection Model

### Task 1: Replace ad-hoc nav selection fields with a first-class semantic state model

**Files:**
- Create: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavigationSelectionState.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs`
- Test: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelSelectionTests.cs`

- [ ] **Step 1: Write the failing tests for explicit semantic selection**

Cover:
- selecting a session stores `Session(sessionId)` semantic state
- selecting start stores `Start`
- selecting settings stores `Settings`
- repeated pane toggles do not change semantic selection
- tree rebuild replacing item instances restores the same semantic selection

Suggested tests:
```csharp
[Fact]
public async Task SemanticSelection_RemainsSessionAcrossPaneToggles()

[Fact]
public void SemanticSelection_UsesExplicitSettingsSentinel()
```

- [ ] **Step 2: Run the targeted tests to verify failure**

Run:
```powershell
dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~MainNavigationViewModelSelectionTests"
```
Expected: FAIL because current VM still mixes semantic state and view projection state.

- [ ] **Step 3: Create `NavigationSelectionState`**

Implement a small immutable semantic state model, for example:
```csharp
public abstract record NavigationSelectionState
{
    public sealed record Start : NavigationSelectionState;
    public sealed record Settings : NavigationSelectionState;
    public sealed record Session(string SessionId) : NavigationSelectionState;
}
```

Rules:
- No UI types.
- No `SelectedItem` references.
- No pane-mode-specific meaning.

- [ ] **Step 4: Refactor `MainNavigationViewModel` to store only semantic selection**

Implementation rules:
- Replace `_selectionKind` / `_selectedSessionId` branching with one `NavigationSelectionState _selection` field.
- `SelectStart`, `SelectSettings`, `SelectSession` only update semantic selection.
- `NormalizeSelectionAfterRebuild()` re-resolves semantic selection, not control item identity.
- `SelectedItem` remains a derived property only.

- [ ] **Step 5: Run tests until green**

Run the same command from Step 2.
Expected: PASS.

---

## Chunk 2: Introduce a Pure Navigation Projection Layer

### Task 2: Move compact/expanded visual selection rules out of `MainPage`

**Files:**
- Create: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavigationViewProjection.cs`
- Create: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationSelectionProjector.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavItemViewModels.cs`
- Test: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\NavigationSelectionProjectorTests.cs`

- [ ] **Step 1: Write the failing projector tests**

Cover:
- expanded + selected session => projected control selected item is session
- compact + selected session => projected control selected item is parent project
- semantic selection remains session in both cases
- compact indicator visible only when selected session is hidden under a project
- start/settings projections remain explicit and stable

Suggested tests:
```csharp
[Fact]
public void Projector_UsesParentControlSelection_WhenCompactAndSessionIsSelected()

[Fact]
public void Projector_KeepsLeafSemanticSelection_WhenCompact()
```

- [ ] **Step 2: Run the projector tests to verify failure**

Run:
```powershell
dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~NavigationSelectionProjectorTests"
```
Expected: FAIL because projector does not exist yet.

- [ ] **Step 3: Create `NavigationViewProjection`**

Suggested shape:
```csharp
public sealed record NavigationViewProjection(
    object? ControlSelectedItem,
    bool IsSettingsSelected,
    IReadOnlyDictionary<string, bool> ActiveProjectIndicators,
    IReadOnlyDictionary<string, bool> LogicalSessionSelection);
```

Rules:
- Keep it UI-agnostic.
- It represents projection outputs, not truth source.

- [ ] **Step 4: Implement `NavigationSelectionProjector` as a pure service**

Inputs:
- semantic selection state
- current nav tree indexes
- shell pane open/display mode

Outputs:
- control-selected item projection
- project ancestor indicator flags
- session logical selection flags

Rules:
- Pure function or nearly pure class.
- No `DispatcherQueue`, no `FrameworkElement`, no `NavigationView`.
- If compact fallback is needed because Uno native ancestor highlighting is unstable, document that in code comments once.

- [ ] **Step 5: Refactor `MainNavigationViewModel` to apply one projection pass**

Implementation rules:
- `ApplySelectionProjection()` becomes a wrapper around projector output.
- `ProjectNavItemViewModel.HasActiveDescendantIndicator` updates from projector result only.
- `SessionNavItemViewModel.IsLogicallySelected` updates from projector result only.
- No scattered special-case mutation in multiple methods.

- [ ] **Step 6: Run projector + selection tests until green**

Run:
```powershell
dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~NavigationSelectionProjectorTests|FullyQualifiedName~MainNavigationViewModelSelectionTests|FullyQualifiedName~MainNavigationViewModelPaneTests"
```
Expected: PASS.

---

## Chunk 3: Centralize User Intent In A Navigation Coordinator

### Task 3: Replace scattered session/start/settings activation logic with one coordinator

**Files:**
- Create: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\INavigationCoordinator.cs`
- Create: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationCoordinator.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml.cs`
- Test: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\NavigationCoordinatorTests.cs`

- [ ] **Step 1: Write failing coordinator tests**

Cover:
- session click updates semantic nav selection and awaits `TrySwitchToSessionAsync`
- start click updates semantic nav selection and navigates content to start
- settings click updates semantic nav selection and navigates to settings
- project click toggles expansion only and does not become semantic selection

Suggested tests:
```csharp
[Fact]
public async Task ActivateSessionAsync_UpdatesNavAndChatInOnePath()

[Fact]
public async Task ActivateProjectAsync_DoesNotMutateSemanticSelection()
```

- [ ] **Step 2: Run tests to verify failure**

Run:
```powershell
dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~NavigationCoordinatorTests"
```
Expected: FAIL because coordinator does not exist yet.

- [ ] **Step 3: Implement `NavigationCoordinator`**

Suggested public API:
```csharp
Task ActivateStartAsync();
Task ActivateSettingsAsync(string settingsKey);
Task ActivateSessionAsync(string sessionId, string? projectId);
Task ToggleProjectAsync(string projectId);
```

Rules:
- Coordinator owns cross-VM orchestration.
- `MainPage` stops manually doing `SelectSession + EnsureChatContent + TrySwitchToSessionAsync`.
- Prefer explicit dependencies over service locator.

- [ ] **Step 4: Update `MainPage.xaml.cs` to delegate only intents**

Implementation rules:
- `OnMainNavItemInvoked` should parse one semantic intent and forward it.
- `MainPage` should no longer manually orchestrate chat/nav synchronization.
- Keep settings special handling minimal because `SettingsItem` is generated by control.

- [ ] **Step 5: Run coordinator tests until green**

Run the same command from Step 2.
Expected: PASS.

---

## Chunk 4: Make `MainPage` A Thin NavigationView Adapter

### Task 4: Reduce `MainPage` nav responsibilities to one-way control projection

**Files:**
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml`

- [ ] **Step 1: Write/extend failing tests around projection stability assumptions**

If no direct UI test harness exists, extend projector/coordinator tests and document why they fully cover the behavior.

- [ ] **Step 2: Remove nav state machine logic from `MainPage`**

Delete or simplify:
- custom selection branching that duplicates VM/coordinator rules
- mixed `ResolveInvokedItem` fallback logic where stable `Tag` intent already exists
- any control-selection decisions that belong in projector/coordinator

Keep only:
- one control-intent path (`ItemInvoked`)
- one control-projection path (`ApplyMainNavSelectionFromProjection`)
- one minimal deferred re-apply path for documented `NavigationView` pane-mode quirks

- [ ] **Step 3: Create a single `ApplyMainNavProjection()` method**

Rules:
- read only from projector/VM projection output
- write only to `MainNavView.SelectedItem` and `SettingsItem`
- do not invent new state
- if deferred re-apply remains necessary, document exact Uno/WinUI quirk in a short comment

- [ ] **Step 4: Update XAML templates to consume VM projection only**

Rules:
- `Project` nodes visually indicate active descendant state without becoming semantic targets
- compact emphasis must not rely on random `InfoBadge` or ad-hoc hacks
- use native control visuals where possible; add only minimal indicator if required
- preserve system layout and spacing

- [ ] **Step 5: Run focused validation**

Run:
```powershell
dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~NavigationSelectionProjectorTests|FullyQualifiedName~NavigationCoordinatorTests|FullyQualifiedName~MainNavigationViewModelSelectionTests|FullyQualifiedName~MainNavigationViewModelPaneTests|FullyQualifiedName~NavigationCoreTests"
```
Expected: PASS.

---

## Chunk 5: Acceptance, Cleanup, And Real-World Guardrails

### Task 5: Final cleanup and verification

**Files:**
- Modify as needed: navigation files above
- Optionally document: `C:\Users\shang\Project\salmon-acp\docs\superpowers\plans\2026-03-18-navigation-selection-ssot-plan.md`

- [ ] **Step 1: Remove dead compatibility leftovers**

Examples to delete if still present:
- obsolete helper methods in `MainPage.xaml.cs`
- now-unused item projection fields
- comments describing superseded workaround paths

- [ ] **Step 2: Run full focused navigation verification**

Run:
```powershell
dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~Navigation"
```
Expected: PASS.

- [ ] **Step 3: Run desktop compile verification**

Run:
```powershell
dotnet build C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\SalmonEgg.csproj -c Debug --framework net10.0-desktop --no-restore -nodeReuse:false
```
Expected: PASS.

- [ ] **Step 4: Manual acceptance checklist**

Verify manually:
- expanded -> current session row is highlighted and chat content matches it
- compact -> parent project has stable visible emphasis, with no random extra badge/dot
- repeated pane toggles do not lose visible focus
- clicking a session always loads that session into chat
- clicking project toggles expansion only
- clicking start/settings updates content and selection correctly

- [ ] **Step 5: Commit**

```bash
git add C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavigationSelectionState.cs \
        C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavigationViewProjection.cs \
        C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationSelectionProjector.cs \
        C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\INavigationCoordinator.cs \
        C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationCoordinator.cs \
        C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs \
        C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavItemViewModel.cs \
        C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavItemViewModels.cs \
        C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Models\Navigation\NavItemTag.cs \
        C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml \
        C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml.cs \
        C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelSelectionTests.cs \
        C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelPaneTests.cs \
        C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\NavigationSelectionProjectorTests.cs \
        C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\NavigationCoordinatorTests.cs \
        C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\NavigationCoreTests.cs

git commit -m "refactor: separate nav semantic state from NavigationView projection"
```

---

## Notes For The Implementer

- Treat `MainPage.xaml.cs` as a control adapter, not as a business-state owner.
- If Uno `NavigationView` still requires one final deferred `SelectedItem` re-apply after pane transitions, keep that workaround confined to one documented adapter method.
- Do not reintroduce fire-and-forget chat-session switching on session click.
- Do not use visual-only hacks like unexplained dots/badges as the long-term compact indicator unless explicitly accepted as product design.
- If a new compact visual is required, prefer a minimal native-feeling indicator and document exactly why the default `NavigationView` visual is insufficient on Uno.
