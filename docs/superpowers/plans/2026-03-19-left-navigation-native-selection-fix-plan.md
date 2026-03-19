# Left Navigation Native Selection Fix Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore native WinUI/Uno hierarchical selection behavior for the left navigation while keeping session as the single semantic selection target.

**Architecture:** Keep `MainNavigationViewModel` as the semantic owner of `Start` / `Settings` / `Session` selection only. Remove the compact-mode parent-selection projection, make project items non-selectable grouping nodes again, and route “more sessions” activation through the unified coordinator path so navigation and chat activation stay synchronized.

**Tech Stack:** Uno Platform, WinUI 3 `NavigationView`, CommunityToolkit.Mvvm, xUnit, Moq.

---

## Chunk 1: Core Selection Semantics

### Task 1: Stop projecting compact selection to parent projects

**Files:**
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationSelectionProjector.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\NavigationSelectionProjectorTests.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\MainNavigationViewModelSelectionTests.cs`

- [ ] **Step 1: Write the failing tests**
- [ ] **Step 2: Run the targeted tests to verify they fail**
- [ ] **Step 3: Update projection logic so session remains the projected selected item regardless of pane openness**
- [ ] **Step 4: Run the targeted tests to verify they pass**

### Task 2: Route “more sessions” picks through the unified activation path

**Files:**
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\NavigationCoordinator.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\src\SalmonEgg.Presentation.Core\Services\INavigationCoordinator.cs`
- Modify: `C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\Navigation\NavigationCoordinatorTests.cs`

- [ ] **Step 1: Write the failing regression test for session activation through the overflow/list path**
- [ ] **Step 2: Run the targeted tests to verify they fail**
- [ ] **Step 3: Implement the minimal coordinator-based activation hook**
- [ ] **Step 4: Run the targeted tests to verify they pass**

## Chunk 2: UI Native Behavior

### Task 3: Make project items grouping-only again

**Files:**
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\MainPage.xaml`
- Modify: `C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\Presentation\Navigation\MainNavigationViewAdapter.cs`

- [ ] **Step 1: Update the XAML so project items no longer select on invoke**
- [ ] **Step 2: Keep project invoke handling limited to expand/collapse**
- [ ] **Step 3: Verify the adapter still applies selected item/state through one path**

## Chunk 3: Verification

### Task 4: Run focused verification

**Files:**
- Verify only

- [ ] **Step 1: Run `dotnet test C:\Users\shang\Project\salmon-acp\tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj -c Debug --no-restore -nodeReuse:false --filter "FullyQualifiedName~Navigation"`**
- [ ] **Step 2: Run `dotnet build C:\Users\shang\Project\salmon-acp\SalmonEgg\SalmonEgg\SalmonEgg.csproj -c Debug --framework net10.0-desktop --no-restore -nodeReuse:false`**
