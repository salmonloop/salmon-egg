# Remote Session Project Affinity Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make imported and remote-bound sessions resolve their project affinity through one shared SSOT-friendly Core resolver, with support for remote/local path mappings, per-conversation local overrides, and consistent projection across Discover, Navigation, Search, and Chat.

**Architecture:** Remote ACP metadata remains the SSOT for session facts such as `cwd`, `title`, and `updatedAt`. Local preferences remain the SSOT for project definitions, profile-scoped path mappings, and per-conversation project-affinity overrides. A new shared resolver service computes the effective project assignment and UI-facing explanation so ViewModels stop duplicating `cwd` prefix logic.

**Tech Stack:** C#/.NET 10, Uno/WinUI MVVM, CommunityToolkit.Mvvm, xUnit, Moq, existing SalmonEgg settings/workspace persistence.

---

## File Structure

### New files

- `src/SalmonEgg.Domain/Models/ProjectAffinity/ProjectPathMapping.cs`
- `src/SalmonEgg.Domain/Models/ProjectAffinity/ProjectAffinityOverride.cs`
- `src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/IProjectAffinityResolver.cs`
- `src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/ProjectAffinityResolver.cs`
- `src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/ProjectAffinityResolution.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/ProjectAffinity/ProjectAffinityResolverTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Discover/DiscoverSessionsPageXamlTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewXamlTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Settings/AcpConnectionSettingsXamlTests.cs`

### Existing files expected to change

- `src/SalmonEgg.Domain/Models/AppSettings.cs`
- `src/SalmonEgg.Domain/Models/Conversation/ConversationDocument.cs`
- `src/SalmonEgg.Infrastructure/Storage/YamlModels/AppSettingsYamlV1.cs`
- `src/SalmonEgg.Infrastructure/Storage/AppSettingsService.cs`
- `src/SalmonEgg.Presentation.Core/ViewModels/Settings/AppPreferencesViewModel.cs`
- `src/SalmonEgg.Presentation.Core/Services/INavigationProjectPreferences.cs`
- `src/SalmonEgg.Presentation.Core/Services/Chat/ChatConversationWorkspace.cs`
- `src/SalmonEgg.Presentation.Core/Services/Chat/ConversationCatalogPresenter.cs`
- `src/SalmonEgg.Presentation.Core/ViewModels/Navigation/MainNavigationViewModel.cs`
- `src/SalmonEgg.Presentation.Core/ViewModels/GlobalSearchViewModel.cs`
- `src/SalmonEgg.Presentation.Core/ViewModels/Discover/DiscoverSessionsViewModel.cs`
- `src/SalmonEgg.Presentation.Core/Services/Chat/DiscoverSessionImportCoordinator.cs`
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
- `SalmonEgg/SalmonEgg/DependencyInjection.cs`
- `SalmonEgg/SalmonEgg/Presentation/Views/Discover/DiscoverSessionsPage.xaml`
- `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml`
- `SalmonEgg/SalmonEgg/Presentation/Views/Settings/AcpConnectionSettingsPage.xaml`
- `src/SalmonEgg.Presentation.Core/ViewModels/Settings/AcpConnectionSettingsViewModel.cs`
- `tests/SalmonEgg.Infrastructure.Tests/Storage/AppSettingsServiceTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Settings/AppPreferencesViewModelTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatConversationWorkspaceTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Discover/DiscoverSessionImportCoordinatorTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Discover/DiscoverSessionsViewModelTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Navigation/ProjectSessionClassifierTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/NavigationCoreTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/SearchInteractionTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Settings/AcpConnectionSettingsViewModelTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`
- `tests/SalmonEgg.GuiTests.Windows/RealUserConfigSmokeTests.cs`

### Parallelization boundaries

**Wave A: parallel-safe foundations**

1. Settings persistence for path mappings
2. Conversation workspace persistence for local overrides
3. Shared resolver and unit tests

These tasks must avoid touching each other’s files.

**Wave B: parallel-safe consumers after Wave A lands**

4. Navigation and Global Search consume resolver
5. Discover consumes resolver and projects preview affinity
6. Chat projects correction affordance and override command
7. Settings UI for path mappings

These tasks may all read the resolver contract but should keep disjoint write scopes, including dedicated surface-specific test files instead of shared umbrella tests.

**Wave C: integration**

8. DI wiring, end-to-end regression tests, and smoke verification

---

## Chunk 1: Foundations

### Task 1: Persist profile-scoped remote path mappings

**Files:**
- Create: `src/SalmonEgg.Domain/Models/ProjectAffinity/ProjectPathMapping.cs`
- Modify: `src/SalmonEgg.Domain/Models/AppSettings.cs`
- Modify: `src/SalmonEgg.Infrastructure/Storage/YamlModels/AppSettingsYamlV1.cs`
- Modify: `src/SalmonEgg.Infrastructure/Storage/AppSettingsService.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Settings/AppPreferencesViewModel.cs`
- Test: `tests/SalmonEgg.Infrastructure.Tests/Storage/AppSettingsServiceTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Settings/AppPreferencesViewModelTests.cs`

- [ ] **Step 1: Write the failing persistence tests**

Cover:

- `AppSettingsService.SaveThenLoad_RoundTripsProjectPathMappings`
- `AppPreferencesViewModel_LoadAsync_RestoresProjectPathMappings`
- `AppPreferencesViewModel_ScheduleSave_PersistsNormalizedProjectPathMappings`

- [ ] **Step 2: Run the failing tests**

Run:

```powershell
dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj' --filter "FullyQualifiedName~AppSettingsServiceTests" --no-restore
dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj' --filter "FullyQualifiedName~AppPreferencesViewModelTests" --no-restore
```

Expected: FAIL because project path mappings do not exist in the model or save/load flow.

- [ ] **Step 3: Add the minimal model and persistence support**

Implement:

- `ProjectPathMapping` with `ProfileId`, `RemoteRootPath`, `LocalRootPath`
- `AppSettings.ProjectPathMappings`
- YAML round-trip support
- `AppPreferencesViewModel` single-writer persistence and normalization helpers

- [ ] **Step 4: Re-run the focused tests**

Run the same commands as Step 2.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Domain/Models/ProjectAffinity/ProjectPathMapping.cs src/SalmonEgg.Domain/Models/AppSettings.cs src/SalmonEgg.Infrastructure/Storage/YamlModels/AppSettingsYamlV1.cs src/SalmonEgg.Infrastructure/Storage/AppSettingsService.cs src/SalmonEgg.Presentation.Core/ViewModels/Settings/AppPreferencesViewModel.cs tests/SalmonEgg.Infrastructure.Tests/Storage/AppSettingsServiceTests.cs tests/SalmonEgg.Presentation.Core.Tests/Settings/AppPreferencesViewModelTests.cs
git commit -m "feat(settings): persist remote project path mappings"
```

### Task 2: Persist per-conversation project-affinity overrides

**Files:**
- Create: `src/SalmonEgg.Domain/Models/ProjectAffinity/ProjectAffinityOverride.cs`
- Modify: `src/SalmonEgg.Domain/Models/Conversation/ConversationDocument.cs`
- Modify: `src/SalmonEgg.Presentation.Core/Services/Chat/ChatConversationWorkspace.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatConversationWorkspaceTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Discover/DiscoverSessionImportCoordinatorTests.cs`

- [ ] **Step 1: Write the failing workspace tests**

Cover:

- `ChatConversationWorkspace_SaveAsync_RoundTripsProjectAffinityOverride`
- `ChatConversationWorkspace_UpdateProjectAffinityOverride_UpdatesCatalogStateWithoutChangingRemoteBinding`
- `DiscoverSessionImportCoordinator_ImportAsync_LeavesProjectAffinityOverrideEmpty`

- [ ] **Step 2: Run the failing tests**

Run:

```powershell
dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj' --filter "FullyQualifiedName~ChatConversationWorkspaceTests|FullyQualifiedName~DiscoverSessionImportCoordinatorTests" --no-restore
```

Expected: FAIL because no override state exists in the conversation document or workspace API.

- [ ] **Step 3: Implement workspace-local override support**

Implement:

- `ConversationRecord.ProjectAffinityOverrideProjectId`
- workspace load/save round-trip
- explicit workspace methods to read/set/clear the override
- keep remote binding data and local override data independent

- [ ] **Step 4: Re-run the focused tests**

Run the same command as Step 2.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Domain/Models/ProjectAffinity/ProjectAffinityOverride.cs src/SalmonEgg.Domain/Models/Conversation/ConversationDocument.cs src/SalmonEgg.Presentation.Core/Services/Chat/ChatConversationWorkspace.cs tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatConversationWorkspaceTests.cs tests/SalmonEgg.Presentation.Core.Tests/Discover/DiscoverSessionImportCoordinatorTests.cs
git commit -m "feat(chat): persist local project affinity overrides"
```

### Task 3: Add the shared project-affinity resolver

**Files:**
- Create: `src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/IProjectAffinityResolver.cs`
- Create: `src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/ProjectAffinityResolution.cs`
- Create: `src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/ProjectAffinityResolver.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/ProjectAffinity/ProjectAffinityResolverTests.cs`
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/Navigation/ProjectSessionClassifierTests.cs`

- [ ] **Step 1: Write the failing resolver tests**

Cover:

- direct project root match
- longest-prefix winner
- no-`cwd` fallback to unclassified
- path mapping hit
- path mapping miss
- override precedence
- deleted override fallback
- slash normalization (`/` vs `\\`)
- trailing-separator normalization
- path-boundary mismatch (`/repo` must not match `/repo2`)

- [ ] **Step 2: Run the failing resolver tests**

Run:

```powershell
dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj' --filter "FullyQualifiedName~ProjectAffinityResolverTests|FullyQualifiedName~ProjectSessionClassifierTests" --no-restore
```

Expected: FAIL because the resolver contract does not exist yet.

- [ ] **Step 3: Implement the resolver**

Implement:

- resolution precedence: override -> profile path mapping -> direct root match -> unclassified
- distinct resolver state for `NeedsMapping` when all are true:
  - the conversation/session is remote-bound or profile-scoped
  - `RemoteCwd` is present
  - no local override exists
  - no configured path mapping matches
  - no direct local project root matches
- structured result with source and reason
- pure Core logic with no UI dependencies
- optionally keep `ProjectSessionClassifier` as a thin helper only if still needed internally
- normalization that protects path boundaries and covers slash, trailing-separator, and case expectations

- [ ] **Step 4: Re-run the focused tests**

Run the same command as Step 2.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/IProjectAffinityResolver.cs src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/ProjectAffinityResolution.cs src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/ProjectAffinityResolver.cs tests/SalmonEgg.Presentation.Core.Tests/ProjectAffinity/ProjectAffinityResolverTests.cs tests/SalmonEgg.Presentation.Core.Tests/Navigation/ProjectSessionClassifierTests.cs
git commit -m "feat(navigation): add shared project affinity resolver"
```

---

## Chunk 2: Consumers

### Task 4: Switch Navigation and Global Search to resolver output

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Navigation/MainNavigationViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/GlobalSearchViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/Services/INavigationProjectPreferences.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Navigation/MainNavigationViewModelSelectionTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Navigation/NavigationCoordinatorTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Start/StartViewModelTests.cs`
- Create: `tests/SalmonEgg.Presentation.Core.Tests/GlobalSearch/GlobalSearchViewModelTests.cs`

- [ ] **Step 1: Write the failing projection tests**

Cover:

- navigation groups a conversation by resolver result, not inline `cwd` logic
- search activates a session with the resolver-derived project id
- no duplicated prefix matcher remains in `GlobalSearchViewModel`
- a regression guard fails if `ResolveProjectId(` or equivalent inline prefix logic reappears in `GlobalSearchViewModel`

- [ ] **Step 2: Run the failing tests**

Run:

```powershell
dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj' --filter "FullyQualifiedName~MainNavigationViewModelSelectionTests|FullyQualifiedName~NavigationCoordinatorTests|FullyQualifiedName~StartViewModelTests|FullyQualifiedName~GlobalSearchViewModelTests" --no-restore
```

Expected: FAIL because the current code still classifies by local `cwd` prefix in multiple places.

- [ ] **Step 3: Implement resolver-backed projection**

Implement:

- inject the resolver into navigation/search
- derive effective project ids through resolver output
- keep `MainNavigationViewModel` focused on projection, not classification policy

- [ ] **Step 4: Re-run the focused tests**

Run the same command as Step 2.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/ViewModels/Navigation/MainNavigationViewModel.cs src/SalmonEgg.Presentation.Core/ViewModels/GlobalSearchViewModel.cs src/SalmonEgg.Presentation.Core/Services/INavigationProjectPreferences.cs tests/SalmonEgg.Presentation.Core.Tests/Navigation/MainNavigationViewModelSelectionTests.cs tests/SalmonEgg.Presentation.Core.Tests/Navigation/NavigationCoordinatorTests.cs tests/SalmonEgg.Presentation.Core.Tests/Start/StartViewModelTests.cs tests/SalmonEgg.Presentation.Core.Tests/GlobalSearch/GlobalSearchViewModelTests.cs
git commit -m "refactor(navigation): consume shared project affinity resolver"
```

### Task 5: Switch Discover to resolver-backed preview affinity

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Discover/DiscoverSessionsViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/Services/Chat/DiscoverSessionImportCoordinator.cs`
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Discover/DiscoverSessionsPage.xaml`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Discover/DiscoverSessionsViewModelTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Discover/DiscoverSessionImportCoordinatorTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Discover/DiscoverSessionsPageXamlTests.cs`

- [ ] **Step 1: Write the failing Discover tests**

Cover:

- discover rows expose resolver-backed project badge text
- rows distinguish `Needs mapping` from `Unclassified` using the resolver result
- rows mark unmapped remote-bound sessions as needing user attention
- import still passes remote `cwd` through untouched while using resolver or selected effective project only for projection/override input

- [ ] **Step 2: Run the failing tests**

Run:

```powershell
dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj' --filter "FullyQualifiedName~DiscoverSessionsViewModelTests|FullyQualifiedName~DiscoverSessionImportCoordinatorTests|FullyQualifiedName~DiscoverSessionsPageXamlTests" --no-restore
```

Expected: FAIL because the row view model does not currently include affinity projection data.

- [ ] **Step 3: Implement minimal Discover projection changes**

Implement:

- resolver-backed row metadata
- lightweight affinity badge and status text in XAML
- explicit projection of `Needs mapping` vs `Unclassified`
- import coordinator plumbing only if needed to carry a chosen effective project/override input
- no local classification logic inside the page/view

- [ ] **Step 4: Re-run the focused tests**

Run the same command as Step 2.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/ViewModels/Discover/DiscoverSessionsViewModel.cs src/SalmonEgg.Presentation.Core/Services/Chat/DiscoverSessionImportCoordinator.cs SalmonEgg/SalmonEgg/Presentation/Views/Discover/DiscoverSessionsPage.xaml tests/SalmonEgg.Presentation.Core.Tests/Discover/DiscoverSessionsViewModelTests.cs tests/SalmonEgg.Presentation.Core.Tests/Discover/DiscoverSessionImportCoordinatorTests.cs tests/SalmonEgg.Presentation.Core.Tests/Discover/DiscoverSessionsPageXamlTests.cs
git commit -m "feat(discover): preview project affinity for remote sessions"
```

### Task 6: Add Chat correction affordance for local overrides

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewXamlTests.cs`

- [ ] **Step 1: Write the failing Chat tests**

Cover:

- remote-bound unclassified conversation shows a correction affordance
- selecting an override updates workspace state and resolver output
- override survives hydration and navigation rebuilds

- [ ] **Step 2: Run the failing tests**

Run:

```powershell
dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj' --filter "FullyQualifiedName~ChatViewModelTests|FullyQualifiedName~ChatViewXamlTests" --no-restore
```

Expected: FAIL because Chat does not expose override-backed project correction state.

- [ ] **Step 3: Implement minimal override interaction**

Implement:

- ViewModel state for current effective project affinity
- command(s) to set/clear the local override
- lightweight text/action surface in `ChatView.xaml`

- [ ] **Step 4: Re-run the focused tests**

Run the same command as Step 2.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewXamlTests.cs
git commit -m "feat(chat): allow correcting project affinity locally"
```

### Task 7: Add ACP settings UI for remote-to-local path mappings

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Settings/AcpConnectionSettingsViewModel.cs`
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Settings/AcpConnectionSettingsPage.xaml`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Settings/AcpConnectionSettingsViewModelTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Settings/AcpConnectionSettingsXamlTests.cs`

- [ ] **Step 1: Write the failing settings tests**

Cover:

- selected profile exposes editable path mapping rows
- add/remove/update mapping actions update `AppPreferencesViewModel`
- settings XAML exposes stable controls for automation

- [ ] **Step 2: Run the failing tests**

Run:

```powershell
dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj' --filter "FullyQualifiedName~AcpConnectionSettingsViewModelTests|FullyQualifiedName~AcpConnectionSettingsXamlTests" --no-restore
```

Expected: FAIL because path-mapping UI state does not yet exist.

- [ ] **Step 3: Implement the settings editor**

Implement:

- per-profile mapping list in the ACP settings page
- commands for add/remove/edit
- `x:Bind`-driven projection with no business logic in code-behind

- [ ] **Step 4: Re-run the focused tests**

Run the same command as Step 2.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/ViewModels/Settings/AcpConnectionSettingsViewModel.cs SalmonEgg/SalmonEgg/Presentation/Views/Settings/AcpConnectionSettingsPage.xaml tests/SalmonEgg.Presentation.Core.Tests/Settings/AcpConnectionSettingsViewModelTests.cs tests/SalmonEgg.Presentation.Core.Tests/Settings/AcpConnectionSettingsXamlTests.cs
git commit -m "feat(settings): edit remote project path mappings"
```

---

## Chunk 3: Integration

### Task 8: Wire DI, catalog projection, and end-to-end verification

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/Services/Chat/ConversationCatalogPresenter.cs`
- Modify: `SalmonEgg/SalmonEgg/DependencyInjection.cs`
- Modify: `tests/SalmonEgg.GuiTests.Windows/RealUserConfigSmokeTests.cs`
- Optionally adjust: any focused test helpers needed by Tasks 4-7

- [ ] **Step 1: Write the failing integration tests**

Cover:

- DI resolves one shared `IProjectAffinityResolver`
- catalog/search/navigation all agree on project affinity
- GUI smoke: import matched session, import unmapped session, override it, restart, reopen, and verify grouping remains stable

- [ ] **Step 2: Run the failing integration tests**

Run:

```powershell
dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj' --filter "FullyQualifiedName~NavigationCoreTests|FullyQualifiedName~ChatViewModelTests|FullyQualifiedName~DiscoverSessionsViewModelTests" --no-restore
$env:SALMONEGG_GUI='1'; dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj' --filter "FullyQualifiedName~RealUserConfigSmokeTests" -v n
```

Expected: FAIL until the resolver is wired end-to-end and the smoke covers reassociation/restart.

- [ ] **Step 3: Implement the integration glue**

Implement:

- DI registration for the resolver
- any catalog presenter changes needed so consumers can access resolver-derived projection data cleanly
- smoke updates that assert the new UX contract

- [ ] **Step 4: Run focused and full verification**

Run:

```powershell
dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj' --no-restore
dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj' --no-restore
./run.bat msix
$env:SALMONEGG_GUI='1'; dotnet test 'C:/Users/shang/Project/salmon-acp/tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj' --filter "FullyQualifiedName~RealUserConfigSmokeTests" -v n
```

Expected: PASS with the existing no-warning MSIX expectation preserved.

- [ ] **Step 5: Commit**

```bash
git add src/SalmonEgg.Presentation.Core/Services/Chat/ConversationCatalogPresenter.cs SalmonEgg/SalmonEgg/DependencyInjection.cs tests/SalmonEgg.GuiTests.Windows/RealUserConfigSmokeTests.cs
git commit -m "feat(project-affinity): wire resolver across app surfaces"
```

---

## Execution notes for subagents

- Task 1, Task 2, and Task 3 can run in parallel immediately.
- Task 4, Task 5, Task 6, and Task 7 can run in parallel after the foundation contracts are merged locally.
- Task 8 must run after the consumer tasks land.
- Do not let two workers edit the same file set.
- Any worker touching XAML must preserve `x:Bind` and existing automation-id conventions.
- Any worker touching persistence must preserve backward-compatible load behavior for existing saved data.

## Recommended dispatch order

1. Dispatch three workers in parallel for Task 1, Task 2, Task 3.
2. Integrate their changes locally and rerun focused tests.
3. Dispatch four workers in parallel for Task 4, Task 5, Task 6, Task 7.
4. Integrate and run focused tests again.
5. Dispatch one worker for Task 8 or execute Task 8 locally if integration conflicts appear.
