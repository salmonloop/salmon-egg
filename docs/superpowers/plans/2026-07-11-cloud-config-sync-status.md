# Cloud Config Sync Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persistent, accessible two-layer connection and synchronization status experience to cloud configuration sync.

**Architecture:** `DataStorageSettingsViewModel` owns explicit connection and transfer state enums and derives all UI text, visibility, and command availability from them. Existing cloud sync service results remain authoritative; XAML only projects ViewModel state through `x:Bind`.

**Tech Stack:** .NET 10, C#, CommunityToolkit.Mvvm, Uno Platform 6.5, WinUI 3 XAML, xUnit v3, Microsoft.Testing.Platform.

## Global Constraints

- Keep strict MVVM: no cloud-sync visual state in code-behind.
- Keep Core and Presentation.Core cross-platform and free of UI types.
- Use `x:Bind` and native WinUI/Uno controls.
- Preserve one authoritative cloud-sync state chain and reject stale async results.
- Localize all user-visible text in English, English (US), and Simplified Chinese resources.

---

### Task 1: State projection behavior

**Files:**
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/ViewModels/Settings/DataStorageSettingsViewModelTests.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Settings/DataStorageSettingsViewModel.cs`

**Interfaces:**
- Produces: `CloudConfigConnectionState`, `CloudConfigTransferState`, and derived status properties on `DataStorageSettingsViewModel`.

- [ ] Add failing tests for disconnected initialization, successful WebDAV upload, restore, conflict, connection failure, sync failure after connection, field edits after connection, and disconnect.
- [ ] Run the focused test class and confirm failures are caused by missing state properties/transitions.
- [ ] Add the two enums and minimal ViewModel state projection required by the tests.
- [ ] Run the focused test class and confirm all state transition tests pass.

### Task 2: Localized status copy

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.resx`
- Modify: `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.en.resx`
- Modify: `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.en-US.resx`
- Modify: `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.zh-Hans.resx`

**Interfaces:**
- Consumes: ViewModel state properties from Task 1.
- Produces: localized connection, transfer, retry, reconnect, progress, and error copy.

- [ ] Add resource keys used by the tested ViewModel projections.
- [ ] Run resource/localization tests and confirm all cultures contain matching keys.

### Task 3: Persistent status summary UI

**Files:**
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Settings/DataStorageSettingsPage.xaml`
- Modify: `SalmonEgg/SalmonEgg/Strings/en/Resources.resw`
- Modify: `SalmonEgg/SalmonEgg/Strings/en-US/Resources.resw`
- Modify: `SalmonEgg/SalmonEgg/Strings/zh-Hans/Resources.resw`
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/Ui/XamlComplianceTests.cs`
- Modify: `tests/SalmonEgg.GuiTests.Windows/CloudSyncSettingsSmokeTests.cs`

**Interfaces:**
- Consumes: status headline/detail/error/target, progress visibility, and adaptive command text from Task 1.
- Produces: automation IDs `DataStorage.CloudSync.ConnectionStatus`, `DataStorage.CloudSync.TransferStatus`, `DataStorage.CloudSync.Error`, `DataStorage.CloudSync.Progress`, and `DataStorage.CloudSync.RemoteTarget`.

- [ ] Add failing XAML compliance assertions for the persistent summary, `x:Bind`, native `ProgressRing`, and automation IDs.
- [ ] Add failing Windows smoke assertions for the visible connection and transfer status elements.
- [ ] Implement the summary and contextual action layout with native controls and theme resources.
- [ ] Run XAML compliance and cloud-sync GUI test compilation.

### Task 4: Full verification

**Files:**
- Verify all modified files.

- [ ] Run focused Presentation.Core tests.
- [ ] Run the complete Presentation.Core test project.
- [ ] Build `SalmonEgg.sln` in Release.
- [ ] Build the Windows GUI test project.
- [ ] Build the native MSIX artifact and run the cloud-sync GUI smoke against that artifact when the Windows environment supports it.
- [ ] Inspect the final diff for state duplication, UI-type leakage, non-localized text, stale status paths, and unrelated changes.

