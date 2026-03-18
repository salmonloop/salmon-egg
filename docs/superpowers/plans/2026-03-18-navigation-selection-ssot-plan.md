# Navigation Selection SSOT Refactor Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild main shell navigation selection so the active chat session remains the single logical selection, while compact/collapsed visuals derive from that state without mutating `NavigationView.SelectedItem` between child and parent nodes.

**Architecture:** Treat navigation selection as a single semantic state owned by `MainNavigationViewModel`, represented by a semantic key/sentinel rather than a cached `SelectedItem` object. Keep `NavigationView` as a complex view control that receives one-way projection from the ViewModel, uses a single user-intent input path, and prefers the control's native hierarchical selection semantics before introducing any custom ancestor-highlighting fallback.

**Tech Stack:** Uno Platform, WinUI 3 `NavigationView`, CommunityToolkit.Mvvm, Uno/Reactive MVUX shell layout state, xUnit.

---

## File Map

**Core navigation selection model**
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs`
  - Own the single logical selection key/sentinel.
  - Resolve the projected selected leaf item from the key after rebuilds.
  - Stop using `SelectedItem` as a dual-purpose parent/child state bucket or persistent truth source.
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavItemViewModels.cs`
  - Add explicit, derived visual state for project/session nodes (`IsSelected`, `IsActiveDescendant`, or equivalent) without embedding UI control references.

**View adapter**
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml`
  - Ensure `NavigationView` receives selection one-way.
  - Mark non-leaf project/group nodes non-selectable when they do not represent real navigation targets.
  - Prefer native `NavigationView` hierarchical selection behavior before custom visual fallback.
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml.cs`
  - Restrict code-behind to a thin adapter for `NavigationView` and settings item edge cases.
  - Use a single View -> VM user-intent path.
  - Remove ad-hoc selection rewriting tied to pane mode changes.

**Tests**
- Modify: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelPaneTests.cs`
  - Cover single-source logical selection across expanded/compact toggles.
- Create or modify: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelSelectionTests.cs`
  - Focus on selection semantics without WinUI control timing.

**References**
- Review: `C:\Users\shang\Project\salmon-acp\docs\superpowers\specs\2026-03-18-shell-layout-ssot-design.md`
- Review: Microsoft Learn `NavigationView` guidance and Uno `NavigationView` implementation notes (already researched in this session)

---

## Chunk 1: Reframe Selection Model In ViewModel

### Task 1: Add explicit logical selection model

**Files:**
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs`
- Test: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelSelectionTests.cs`

- [ ] **Step 1: Write the failing test for single logical selection**

Create tests that prove:
- Selecting a session keeps the same logical session selected when pane opens/closes repeatedly.
- Pane mode changes do not change the logical selection identity from child session to parent project.
- Start page selection and settings selection remain explicit sentinel states.
- Tree rebuilds that replace item instances still restore the same semantic selection.

Suggested test cases:
```csharp
[Fact]
public async Task LogicalSelection_RemainsActiveSession_WhenPaneClosesAndReopens()

[Fact]
public async Task VisualAncestorState_DoesNotChangeLogicalSelectedItem()
```

- [ ] **Step 2: Run the targeted test to verify it fails for the right reason**

Run:
```powershell
dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~MainNavigationViewModelSelectionTests"
```
Expected: FAIL because current VM still conflates logical and visual selection.

- [ ] **Step 3: Introduce explicit logical selection state in `MainNavigationViewModel`**

Implementation rules:
- Add a single semantic selection key or selection discriminated state, for example:
  - `SelectedSessionId`
  - `SelectedRootKind` (`Start`, `Session`, `Settings`)
- Keep `SelectedItem` as a derived, short-lived projection only.
- Never derive logical state from `NavigationView.SelectedItem`.
- Session selection methods should update only the logical selection source, then call a projection method.
- Pane state changes should only re-project visuals; they must not mutate logical selection.
- Do not persist control item object identity as selection truth; always re-resolve from semantic key after `RebuildTree()`.

- [ ] **Step 4: Implement projection helpers**

Add helpers that separate concerns cleanly:
- `ProjectLogicalSelectionToSelectedItem()`
- `ProjectVisualSelectionState()`
- `ResolveLogicalSelection()`

Rules:
- `SelectedItem` should always resolve to the selected leaf/session item when a session is active.
- Parent project highlighting must not replace `SelectedItem` with the parent.
- The first implementation pass should assume native hierarchical `NavigationView` selection semantics are sufficient unless a focused test demonstrates Uno divergence.
- Keep methods pure or nearly pure where possible.

- [ ] **Step 5: Run the targeted tests until green**

Run:
```powershell
dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~MainNavigationViewModelSelectionTests"
```
Expected: PASS.

### Task 2: Add explicit visual ancestor state only as a compatibility fallback

**Files:**
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavItemViewModels.cs`
- Test: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelSelectionTests.cs`

- [ ] **Step 1: Write the failing test for ancestor highlighting**

Add tests that prove:
- If native `NavigationView` behavior is insufficient on Uno, project `P` gets `IsActiveDescendant == true` when session `S` under `P` is logically selected.
- Sibling projects remain false.
- The active session item can still expose its own selected state.
- If native behavior is sufficient, this task can be skipped and documented.

- [ ] **Step 2: Run the test to verify failure**

Use the same targeted command as above.
Expected: FAIL only if native behavior is proven insufficient and a VM fallback is actually required.

- [ ] **Step 3: Add derived visual properties**

Recommended additions:
- On `ProjectNavItemViewModel`: `IsActiveDescendant`
- On `SessionNavItemViewModel`: `IsLogicallySelected`
- Optionally on base type: a small selection-state abstraction if it simplifies projection without leaking UI concepts.

Rules:
- No `NavigationViewItem`, `FrameworkElement`, or other UI types in Presentation.Core.
- These properties are projection outputs only.
- They must update from `MainNavigationViewModel` in one place after selection/tree rebuild changes.
- Do not add these properties unless focused verification shows native hierarchical selection/highlighting is insufficient on Uno/WinUI for this tree shape.

- [ ] **Step 4: Implement one centralized projection pass**

After any of these events:
- session selection changes
- tree rebuilds
- pane state changes

Run one projection pass that:
- clears previous visual states
- marks the active session node
- marks the ancestor project node

Avoid scattered per-node mutation across multiple event handlers.
- If this task is skipped because native behavior works, replace implementation with a short decision note in the plan execution log.

- [ ] **Step 5: Re-run tests**

Expected: PASS for both logical selection and ancestor-state tests.

---

## Chunk 2: Make The View A Thin Adapter

### Task 3: Simplify `NavigationView` interaction to one-way projection plus a single intent entry point

**Files:**
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml`
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml.cs`

- [ ] **Step 1: Write/extend a failing test that covers projection stability assumptions**

If a pure unit test is enough, assert that repeated pane toggles do not change logical selection and keep ancestor visual state stable.
If no additional unit test is needed, document why existing tests fully cover the projection behavior and move to Step 2.

- [ ] **Step 2: Audit `MainPage` for selection anti-patterns**

Remove or rewrite the following if still present:
- any pane-mode-specific swapping between project and session `SelectedItem`
- any `DispatcherQueue` retry loops whose only purpose is to fight `NavigationView`
- any `TwoWay` binding on `SelectedItem`
- any dual input path that handles both `SelectionChanged` and `ItemInvoked` as navigation intent

Keep only:
- a single explicit user intent handler, preferably `ItemInvoked`
- one-way projection from `NavVM.SelectedItem` to `MainNavView.SelectedItem`
- settings-item special handling, because `SettingsItem` is generated by the control

- [ ] **Step 3: Create a single controlled adapter path in code-behind**

Implement one method, e.g. `ApplyNavigationSelectionFromViewModel()`, with rules:
- it reads only `NavVM.SelectedItem`
- it writes only `MainNavView.SelectedItem`
- it never invents alternative selection state
- it suppresses re-entrant `SelectionChanged` while applying

Trigger it only on:
- page loaded
- `NavVM.SelectedItem` change
- tree rebuild completion if needed
- settings page activation

Do **not** trigger it from every pane animation event unless a documented control quirk requires that final re-apply.
If a deferred re-apply remains necessary, note the exact WinUI/Uno control behavior in a comment.
- If `SelectionChanged` is kept for an unavoidable platform reason, document precisely why `ItemInvoked` alone is insufficient and ensure it is treated as observation, not intent.

- [ ] **Step 4: Update XAML templates to consume VM visual state**

Project nodes that are purely grouping containers should use `SelectsOnInvoked="False"` and should not become the selected navigation target.
Project nodes should render ancestor-active visuals from VM state only if native `NavigationView` behavior proves insufficient.
Use existing system brushes/styles where possible; avoid bespoke pixel hacks.

Examples:
- bind project icon/text emphasis to `IsActiveDescendant`
- keep compact visuals native-looking
- do not replace default control template unless absolutely necessary
- verify whether `NavigationViewItem.IsChildSelected` / native ancestor selection visuals are already enough before introducing custom emphasis

- [ ] **Step 5: Run focused validation**

Run:
```powershell
dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~MainNavigationViewModelSelectionTests|FullyQualifiedName~MainNavigationViewModelPaneTests"
```
Expected: PASS.

### Task 4: Preserve settings/start edge cases without polluting session selection

**Files:**
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml.cs`
- Test: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelSelectionTests.cs`

- [ ] **Step 1: Write the failing tests for Start/Settings transitions**

Cover:
- selecting Start clears active session visual state
- navigating to Settings selects only the settings sentinel/path
- returning from Settings to Chat re-projects the active session correctly
- non-leaf project/group items do not become semantic navigation targets

- [ ] **Step 2: Run the test and verify failure**

Use the same focused command.

- [ ] **Step 3: Implement explicit non-session selection sentinels**

Rules:
- Do not overload nulls/implicit states if an explicit enum or record makes the logic clearer.
- Settings should remain a special view concern only where required by WinUI `SettingsItem` generation.
- `MainNavigationViewModel` should still be the authoritative source for which semantic destination is selected.

- [ ] **Step 4: Re-run tests**

Expected: PASS.

---

## Chunk 3: Verification And Acceptance

### Task 5: Regression suite for rebuild + rapid pane toggles

**Files:**
- Modify: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelPaneTests.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelSelectionTests.cs`

- [ ] **Step 1: Add regression tests for rapid toggles and tree rebuilds**

Cover:
- active session remains logically selected after repeated open/close transitions
- ancestor visual state remains stable after repeated projection passes
- `RebuildTree()` does not drop selection when the active session still exists
- archive/remove cases still fall back to Start cleanly

- [ ] **Step 2: Run focused tests**

Run:
```powershell
dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~MainNavigationViewModelSelectionTests|FullyQualifiedName~MainNavigationViewModelPaneTests|FullyQualifiedName~ShellLayoutViewModelTests|FullyQualifiedName~ShellLayoutStoreTests|FullyQualifiedName~ShellLayoutReducerTests"
```
Expected: PASS.

- [ ] **Step 3: Run a lightweight desktop compile**

Only after focused tests pass, and only if the human explicitly approves the compile in that execution session:
```powershell
dotnet build C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\SalmonEgg.csproj -c Debug --framework net10.0-desktop
```
Expected: PASS with no new warnings/errors in touched areas.

- [ ] **Step 4: Manual acceptance checklist**

Verify in the running app:
- open a session under a project
- collapse to compact repeatedly
- current session remains the logical target
- parent project remains visually emphasized in compact mode
- re-expand and confirm session item is still selected
- Start and Settings still behave correctly

---

## Implementation Rules

- Keep `SelectedItem` single-meaning. It must not alternate between session and project depending on pane mode.
- Keep logical selection as a semantic key/sentinel, not as a cached item object.
- Parent/project highlight must be a separate derived visual state.
- Prefer native hierarchical `NavigationView` selection/ancestor behavior before adding custom ancestor-highlight state.
- Do not put WinUI/Uno control references or control state in Presentation.Core.
- Do not use `TwoWay` binding on `NavigationView.SelectedItem`.
- Use a single user-intent path from View to VM; do not let both `SelectionChanged` and `ItemInvoked` drive navigation unless a documented platform constraint requires it.
- Grouping parent nodes that are not real destinations must be non-selectable (`SelectsOnInvoked=False`).
- Do not rely on repeated `DispatcherQueue` retries as the primary design.
- If a single deferred apply remains necessary for a documented control quirk, isolate it in `MainPage.xaml.cs` and comment the exact reason.
- Prefer `x:Bind` and system resources/styles.
- Avoid replacing `NavigationView` templates unless absolutely necessary.
- No unrelated cleanup or formatting churn.
- Do not create commits during plan review/execution unless the human explicitly asks.

## Acceptance Criteria

- Active chat session is the single logical navigation selection source.
- Compact mode no longer rewrites selection identity to parent project.
- `RebuildTree()` can replace item instances without losing the semantic selection.
- Grouping parent nodes are not semantic navigation targets.
- Parent project emphasis in compact mode is stable under repeated rapid toggles.
- Start/Settings navigation still works.
- No service or view writes selection state behind the VM's back.
- Focused tests pass; desktop compile is only run with explicit approval in execution.

Plan complete and saved to `docs/superpowers/plans/2026-03-18-navigation-selection-ssot-plan.md`. Ready to execute?
