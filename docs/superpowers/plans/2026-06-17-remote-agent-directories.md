# Remote Agent Directories Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace legacy local-to-remote ACP path mappings with per-agent remote directory choices, and make remote WebSocket/HTTP sessions use only explicitly configured remote working directories.

**Architecture:** Treat remote directories as profile-owned configuration, not path translation. The settings page edits `AgentRemoteDirectory` rows for the selected ACP profile. The Start project selector shows unclassified, local projects, and remote directories together; under remote profiles, unclassified and local projects remain visible but disabled, while configured remote directories are selectable and provide the `session/new.cwd`.

**Tech Stack:** C#/.NET, Uno/WinUI XAML, CommunityToolkit.Mvvm, xUnit, YAML settings persistence, ACP JSON-RPC session lifecycle.

---

## Required Context

- Repo root: `C:\Users\shang\Project\salmon-acp`.
- Follow `AGENTS.md`, `docs/coding-standards.md`, and `docs/hard-constraints-session-navigation-and-search.md`.
- ACP protocol boundary: `session/new.cwd` is required and must be an absolute path meaningful to the Agent host. The client must not send local Windows paths to a remote WebSocket/HTTP Agent.
- Current legacy type: `src/SalmonEgg.Domain/Models/ProjectAffinity/ProjectPathMapping.cs`.
- Current resolver risk: `src/SalmonEgg.Presentation.Core/Services/Chat/AcpSessionNewCwdResolver.cs` returns the original cwd when no mapping matches. That must be removed.
- Current Start cwd source: `src/SalmonEgg.Presentation.Core/ViewModels/Start/StartViewModel.cs` uses local project roots via `TryGetProjectRootPath`. Remote profiles must instead select a configured remote directory option.
- Current settings XAML path: `SalmonEgg/SalmonEgg/Presentation/Views/Settings/AcpConnectionSettingsPage.xaml`.
- Current path-mapping settings AutomationIds are `Acp.PathMappings.*`; replace them with `Acp.RemoteDirectories.*`.
- Do not leave compatibility code for `ProjectPathMappings`, `RemoteRootPath`, or `LocalRootPath`. Unknown old YAML fields may remain ignored by deserialization, but the app must not expose, normalize, save, or test them.

## Reviewer Corrections (verified against code on 2026-06-17)

These were added after auditing the live codebase. They refine — not replace — the original tasks.

1. **The strict resolver approach is sound; resume is not affected.** Resuming an existing remote conversation goes through the hydration path (`ChatViewModel.RemoteConversationLifecycle.RunRemoteSessionLoadRecoveryProjectionAsync`), which calls `session/load` with the *stored* cwd and **does not pass through `AcpSessionNewCwdResolver`**. On `session/load` failure it transitions the conversation to `ConversationRuntimePhase.Stale` and surfaces an error (it does NOT silently `session/new`). `EnsureRemoteSessionAsync`'s `session/new` branch only fires for conversations with no bound remote session id (genuinely new), whose cwd comes from the Start remote-directory selection. Therefore making the resolver strict for remote does not break reconnect/resync.
2. **Validate absolute paths at the settings layer (Task 2/Task 5).** `AcpClient` only enforces `ProtocolPathRules.IsAbsolutePath` at the transport boundary (`session/new`, `session/load`), so a non-absolute `RemotePath` would save successfully, appear selectable, and only fail when a session is created. Reject non-absolute rows during normalization and guard in the resolver. Reuse the existing `SalmonEgg.Domain.Models.Protocol.ProtocolPathRules.IsAbsolutePath` (it accepts POSIX `/…`, UNC `\\…`, and `C:\…`; do not invent a new check, and do not restrict to POSIX-only).
3. **Update the `MissingRemoteCwdMessage` text (Task 5/Task 8).** The current constant literally contains the phrase "remote path mapping", which the Task 8 legacy-name scan for `path mapping` will flag. The message must be rewritten to remote-directory wording or Task 8 will fail.
4. **The affinity classification downgrade is intentional but lossy (Task 6).** `ProjectAffinityResolver.ResolveMapping` currently classifies discovered remote sessions by translating remote cwd → local path → local project. Removing mappings drops this entirely. See Task 6 for the (optional) replacement that classifies a discovered remote session when its cwd exactly matches a configured `AgentRemoteDirectory.RemotePath`.

## File Map

- Create `src/SalmonEgg.Domain/Models/AgentRemoteDirectory.cs`: profile-owned remote cwd option.
- Delete `src/SalmonEgg.Domain/Models/ProjectAffinity/ProjectPathMapping.cs`: remove old mapping model.
- Modify `src/SalmonEgg.Domain/Models/AppSettings.cs`: replace `ProjectPathMappings` with `AgentRemoteDirectories`.
- Modify `src/SalmonEgg.Infrastructure/Storage/YamlModels/AppSettingsYamlV1.cs`: replace YAML model property.
- Modify `src/SalmonEgg.Infrastructure/Storage/AppSettingsService.cs`: clone and trim `AgentRemoteDirectories`; do not read/write `ProjectPathMappings`.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Settings/AppPreferencesViewModel.cs`: expose `ObservableCollection<AgentRemoteDirectory> AgentRemoteDirectories`, normalize, persist, and observe it.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Settings/AcpConnectionSettingsViewModel.cs`: replace path-mapping rows with remote-directory rows.
- Modify `SalmonEgg/SalmonEgg/Presentation/Views/Settings/AcpConnectionSettingsPage.xaml`: replace settings module title, hint, fields, bindings, and AutomationIds.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Start/StartProjectOptionViewModel.cs`: add option kind/selectability/remote cwd fields.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ProjectSelectorPolicy.cs`: project disabled options as non-selectable dropdown items and block submit if the selected option is not selectable.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Start/StartViewModel.cs`: build remote directory options for remote profiles; keep local projects visible but disabled; resolve cwd from selected remote directory.
- Modify `src/SalmonEgg.Presentation.Core/Services/Chat/AcpSessionNewCwdResolver.cs`: remote profiles require the requested cwd to match a configured remote directory for that profile.
- Modify all interfaces that currently expose path mappings: `INavigationProjectPreferences`, `IAcpConnectionState`, and related adapters.
- Modify project affinity/discovery/search files to remove mapping semantics. Remote discovered sessions with unmatched cwd should remain unclassified/needs-attention without automatic local resolution.
- Modify resource files containing mapping copy: `src/SalmonEgg.Presentation.Core/Resources/CoreStrings*.resx`.
- Modify tests under `tests/SalmonEgg.*` listed in `rg "ProjectPathMapping|ProjectPathMappings|PathMapping|RemoteRootPath|LocalRootPath"`.

---

### Task 1: Replace The Domain And Settings Persistence Model

**Files:**
- Create: `src/SalmonEgg.Domain/Models/AgentRemoteDirectory.cs`
- Delete: `src/SalmonEgg.Domain/Models/ProjectAffinity/ProjectPathMapping.cs`
- Modify: `src/SalmonEgg.Domain/Models/AppSettings.cs`
- Modify: `src/SalmonEgg.Infrastructure/Storage/YamlModels/AppSettingsYamlV1.cs`
- Modify: `src/SalmonEgg.Infrastructure/Storage/AppSettingsService.cs`
- Test: `tests/SalmonEgg.Infrastructure.Tests/Storage/AppSettingsServiceTests.cs`

- [ ] **Step 1: Write the failing persistence test**

Replace the existing `SaveThenLoad_RoundTripsProjectPathMappings` test with:

```csharp
[Fact]
public async Task SaveThenLoad_RoundTripsAgentRemoteDirectories()
{
    var fileStore = new InMemoryAppFileStore();
    var appData = new TestAppDataService("C:\\AppData");
    var service = new AppSettingsService(fileStore, appData);

    await service.SaveAsync(new AppSettings
    {
        AgentRemoteDirectories = new List<AgentRemoteDirectory>
        {
            new()
            {
                ProfileId = " profile-a ",
                DirectoryId = " dir-a ",
                DisplayName = " Alpha ",
                RemotePath = " /remote/alpha "
            },
            new()
            {
                ProfileId = "profile-b",
                DirectoryId = "dir-b",
                DisplayName = "Beta",
                RemotePath = "/remote/beta"
            }
        }
    });

    var loaded = await service.LoadAsync();

    Assert.Collection(
        loaded.AgentRemoteDirectories,
        first =>
        {
            Assert.Equal("profile-a", first.ProfileId);
            Assert.Equal("dir-a", first.DirectoryId);
            Assert.Equal("Alpha", first.DisplayName);
            Assert.Equal("/remote/alpha", first.RemotePath);
        },
        second =>
        {
            Assert.Equal("profile-b", second.ProfileId);
            Assert.Equal("dir-b", second.DirectoryId);
            Assert.Equal("Beta", second.DisplayName);
            Assert.Equal("/remote/beta", second.RemotePath);
        });
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\SalmonEgg.Infrastructure.Tests\SalmonEgg.Infrastructure.Tests.csproj --filter FullyQualifiedName~AppSettingsServiceTests.SaveThenLoad_RoundTripsAgentRemoteDirectories
```

Expected: FAIL or compile error because `AgentRemoteDirectory` / `AgentRemoteDirectories` do not exist yet.

- [ ] **Step 3: Create the new domain model**

Create `src/SalmonEgg.Domain/Models/AgentRemoteDirectory.cs`:

```csharp
namespace SalmonEgg.Domain.Models;

public sealed class AgentRemoteDirectory
{
    public string ProfileId { get; set; } = string.Empty;

    public string DirectoryId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string RemotePath { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Replace AppSettings properties**

In `src/SalmonEgg.Domain/Models/AppSettings.cs`, replace:

```csharp
public List<ProjectPathMapping> ProjectPathMappings { get; set; } = new();
```

with:

```csharp
public List<AgentRemoteDirectory> AgentRemoteDirectories { get; set; } = new();
```

Remove the `using SalmonEgg.Domain.Models.ProjectAffinity;` line if it becomes unused.

- [ ] **Step 5: Replace YAML model properties**

In `src/SalmonEgg.Infrastructure/Storage/YamlModels/AppSettingsYamlV1.cs`, remove the `ProjectPathMappings` property and add:

```csharp
public List<AgentRemoteDirectory> AgentRemoteDirectories { get; set; } = new();
```

Remove the `using SalmonEgg.Domain.Models.ProjectAffinity;` line.

- [ ] **Step 6: Replace storage clone logic**

In `src/SalmonEgg.Infrastructure/Storage/AppSettingsService.cs`, remove `CloneProjectPathMappings` and add:

```csharp
private static List<AgentRemoteDirectory> CloneAgentRemoteDirectories(IEnumerable<AgentRemoteDirectory>? directories)
{
    var clone = new List<AgentRemoteDirectory>();
    if (directories is null)
    {
        return clone;
    }

    foreach (var directory in directories)
    {
        if (directory is null)
        {
            continue;
        }

        clone.Add(new AgentRemoteDirectory
        {
            ProfileId = directory.ProfileId?.Trim() ?? string.Empty,
            DirectoryId = directory.DirectoryId?.Trim() ?? string.Empty,
            DisplayName = directory.DisplayName?.Trim() ?? string.Empty,
            RemotePath = directory.RemotePath?.Trim() ?? string.Empty
        });
    }

    return clone;
}
```

Update load and save assignments:

```csharp
AgentRemoteDirectories = CloneAgentRemoteDirectories(model.AgentRemoteDirectories),
```

and:

```csharp
AgentRemoteDirectories = CloneAgentRemoteDirectories(settings.AgentRemoteDirectories),
```

- [ ] **Step 7: Delete the old model file**

Delete `src/SalmonEgg.Domain/Models/ProjectAffinity/ProjectPathMapping.cs`.

- [ ] **Step 8: Run test to verify it passes**

Run:

```powershell
dotnet test tests\SalmonEgg.Infrastructure.Tests\SalmonEgg.Infrastructure.Tests.csproj --filter FullyQualifiedName~AppSettingsServiceTests.SaveThenLoad_RoundTripsAgentRemoteDirectories
```

Expected: PASS.

---

### Task 2: Replace App Preferences Collection And Settings Page ViewModel

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Settings/AppPreferencesViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Settings/AcpConnectionSettingsViewModel.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Settings/AppPreferencesViewModelTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Settings/AcpConnectionSettingsViewModelTests.cs`

- [ ] **Step 1: Write failing AppPreferences tests**

Replace the project-path-mapping tests with:

```csharp
[Fact]
public async Task LoadAsync_RestoresAgentRemoteDirectories()
{
    var settingsService = new FakeAppSettingsService(new AppSettings
    {
        AgentRemoteDirectories = new List<AgentRemoteDirectory>
        {
            new()
            {
                ProfileId = "profile-a",
                DirectoryId = "dir-a",
                DisplayName = "Alpha",
                RemotePath = "/remote/alpha"
            }
        }
    });
    var vm = CreateViewModel(settingsService);

    await settingsService.LoadCompletion;

    var directory = Assert.Single(vm.AgentRemoteDirectories);
    Assert.Equal("profile-a", directory.ProfileId);
    Assert.Equal("dir-a", directory.DirectoryId);
    Assert.Equal("Alpha", directory.DisplayName);
    Assert.Equal("/remote/alpha", directory.RemotePath);
}

[Fact]
public async Task ScheduleSave_PersistsNormalizedAgentRemoteDirectories()
{
    var settingsService = new FakeAppSettingsService(new AppSettings());
    var vm = CreateViewModel(settingsService);
    await settingsService.LoadCompletion;

    vm.AgentRemoteDirectories.Add(new AgentRemoteDirectory
    {
        ProfileId = " profile ",
        DirectoryId = " dir ",
        DisplayName = " Workspace ",
        RemotePath = " /remote/workspace "
    });

    await WaitForConditionAsync(() =>
        settingsService.LastSaved?.AgentRemoteDirectories.Count == 1
        && settingsService.LastSaved.AgentRemoteDirectories[0].ProfileId == "profile"
        && settingsService.LastSaved.AgentRemoteDirectories[0].DirectoryId == "dir"
        && settingsService.LastSaved.AgentRemoteDirectories[0].DisplayName == "Workspace"
        && settingsService.LastSaved.AgentRemoteDirectories[0].RemotePath == "/remote/workspace");
}
```

- [ ] **Step 2: Write failing settings ViewModel tests**

Replace path-mapping row tests with:

```csharp
[Fact]
public async Task RemoteDirectoryRows_SelectedProfile_ExposesOnlyProfileDirectories()
{
    var preferences = CreatePreferences();
    preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
    {
        ProfileId = "profile-a",
        DirectoryId = "dir-a-1",
        DisplayName = "Alpha One",
        RemotePath = "/remote/a-1"
    });
    preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
    {
        ProfileId = "profile-b",
        DirectoryId = "dir-b-1",
        DisplayName = "Beta One",
        RemotePath = "/remote/b-1"
    });
    var viewModel = await CreateViewModelAsync(preferences);

    SelectProfile(viewModel, "profile-a");

    var row = Assert.Single(viewModel.RemoteDirectoryRows);
    Assert.Equal("Alpha One", row.DisplayName);
    Assert.Equal("/remote/a-1", row.RemotePath);
}

[Fact]
public async Task RemoteDirectoryRows_AddUpdateRemove_UpdatesAppPreferencesDirectories()
{
    var preferences = CreatePreferences();
    var viewModel = await CreateViewModelAsync(preferences);
    SelectProfile(viewModel, "profile-a");

    viewModel.AddRemoteDirectoryCommand.Execute(null);
    var row = Assert.Single(viewModel.RemoteDirectoryRows);
    row.DisplayName = " Workspace ";
    row.RemotePath = " /remote/workspace ";

    var directory = Assert.Single(preferences.AgentRemoteDirectories.Where(d => d.ProfileId == "profile-a"));
    Assert.False(string.IsNullOrWhiteSpace(directory.DirectoryId));
    Assert.Equal("Workspace", directory.DisplayName);
    Assert.Equal("/remote/workspace", directory.RemotePath);

    row.RemoveCommand.Execute(null);

    Assert.Empty(preferences.AgentRemoteDirectories.Where(d => d.ProfileId == "profile-a"));
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~AppPreferencesViewModelTests|FullyQualifiedName~AcpConnectionSettingsViewModelTests"
```

Expected: FAIL or compile errors for missing `AgentRemoteDirectories`, `RemoteDirectoryRows`, and `AddRemoteDirectoryCommand`.

- [ ] **Step 4: Update AppPreferencesViewModel**

Replace:

```csharp
public ObservableCollection<ProjectPathMapping> ProjectPathMappings { get; } = new();
```

with:

```csharp
public ObservableCollection<AgentRemoteDirectory> AgentRemoteDirectories { get; } = new();
```

Rename collection changed handler to `OnAgentRemoteDirectoriesChanged`, load from `settings.AgentRemoteDirectories`, save to `AgentRemoteDirectories = NormalizeAgentRemoteDirectories(AgentRemoteDirectories)`, and add:

```csharp
private static List<AgentRemoteDirectory> NormalizeAgentRemoteDirectories(IEnumerable<AgentRemoteDirectory>? directories)
{
    var normalized = new List<AgentRemoteDirectory>();
    if (directories is null)
    {
        return normalized;
    }

    foreach (var directory in directories)
    {
        if (directory is null)
        {
            continue;
        }

        var profileId = directory.ProfileId?.Trim();
        var directoryId = directory.DirectoryId?.Trim();
        var remotePath = directory.RemotePath?.Trim();
        if (string.IsNullOrWhiteSpace(profileId)
            || string.IsNullOrWhiteSpace(directoryId)
            || string.IsNullOrWhiteSpace(remotePath)
            || !ProtocolPathRules.IsAbsolutePath(remotePath))
        {
            // A non-absolute RemotePath would only fail later at session/new
            // (AcpClient.ValidateRequiredAbsolutePath). Drop it here so the row
            // never becomes selectable. Requires: using SalmonEgg.Domain.Models.Protocol;
            continue;
        }

        normalized.Add(new AgentRemoteDirectory
        {
            ProfileId = profileId,
            DirectoryId = directoryId,
            DisplayName = string.IsNullOrWhiteSpace(directory.DisplayName)
                ? remotePath
                : directory.DisplayName.Trim(),
            RemotePath = remotePath
        });
    }

    return normalized;
}
```

- [ ] **Step 5: Update AcpConnectionSettingsViewModel**

Rename all mapping concepts:

```csharp
private bool _suppressRemoteDirectoryProjection;

public ObservableCollection<AcpRemoteDirectoryRowViewModel> RemoteDirectoryRows { get; } = new();

public bool CanEditRemoteDirectories => !string.IsNullOrWhiteSpace(ResolveSelectedProfileId());
```

Add command:

```csharp
[RelayCommand(CanExecute = nameof(CanEditRemoteDirectories))]
private void AddRemoteDirectory()
{
    var profileId = ResolveSelectedProfileId();
    if (string.IsNullOrWhiteSpace(profileId))
    {
        return;
    }

    _preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
    {
        ProfileId = profileId,
        DirectoryId = Guid.NewGuid().ToString("N"),
        DisplayName = string.Empty,
        RemotePath = string.Empty
    });
}
```

Replace row update with:

```csharp
internal void UpdateRemoteDirectory(AcpRemoteDirectoryRowViewModel row)
{
    var index = _preferences.AgentRemoteDirectories.IndexOf(row.Directory);
    if (index < 0)
    {
        return;
    }

    var updated = new AgentRemoteDirectory
    {
        ProfileId = row.Directory.ProfileId,
        DirectoryId = row.Directory.DirectoryId,
        DisplayName = (row.DisplayName ?? string.Empty).Trim(),
        RemotePath = (row.RemotePath ?? string.Empty).Trim()
    };

    if (string.Equals(row.Directory.DisplayName, updated.DisplayName, StringComparison.Ordinal)
        && string.Equals(row.Directory.RemotePath, updated.RemotePath, StringComparison.Ordinal))
    {
        return;
    }

    _suppressRemoteDirectoryProjection = true;
    try
    {
        _preferences.AgentRemoteDirectories[index] = updated;
    }
    finally
    {
        _suppressRemoteDirectoryProjection = false;
    }

    row.ReplaceDirectory(updated);
}
```

Add row ViewModel:

```csharp
public sealed partial class AcpRemoteDirectoryRowViewModel : ObservableObject
{
    private readonly AcpConnectionSettingsViewModel _owner;
    private bool _isApplyingModel;

    internal AcpRemoteDirectoryRowViewModel(AgentRemoteDirectory directory, AcpConnectionSettingsViewModel owner)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _displayName = directory.DisplayName;
        _remotePath = directory.RemotePath;
        RemoveCommand = new RelayCommand(Remove);
    }

    internal AgentRemoteDirectory Directory { get; private set; }

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _remotePath;

    public IRelayCommand RemoveCommand { get; }

    partial void OnDisplayNameChanged(string value)
    {
        UpdateOwner();
    }

    partial void OnRemotePathChanged(string value)
    {
        UpdateOwner();
    }

    internal void ReplaceDirectory(AgentRemoteDirectory directory)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _isApplyingModel = true;
        try
        {
            DisplayName = Directory.DisplayName;
            RemotePath = Directory.RemotePath;
        }
        finally
        {
            _isApplyingModel = false;
        }
    }

    private void UpdateOwner()
    {
        if (_isApplyingModel)
        {
            return;
        }

        _owner.UpdateRemoteDirectory(this);
    }

    private void Remove()
    {
        _owner.RemoveRemoteDirectory(this);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~AppPreferencesViewModelTests|FullyQualifiedName~AcpConnectionSettingsViewModelTests"
```

Expected: PASS.

---

### Task 3: Replace Settings XAML And XAML Contract Tests

**Files:**
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Settings/AcpConnectionSettingsPage.xaml`
- Modify: `tests/SalmonEgg.Presentation.Core.Tests/Settings/AcpConnectionSettingsXamlTests.cs`

- [ ] **Step 1: Write failing XAML tests**

Replace path-mapping assertions with:

```csharp
[Fact]
public void AcpConnectionSettingsPage_RemoteDirectoriesEditor_UsesViewModelDrivenBindings()
{
    var xaml = ReadAcpSettingsXaml();

    Assert.Contains("ItemsSource=\"{x:Bind ViewModel.RemoteDirectoryRows, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    Assert.Contains("Command=\"{x:Bind ViewModel.AddRemoteDirectoryCommand}\"", xaml, StringComparison.Ordinal);
    Assert.Contains("Text=\"{x:Bind DisplayName, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
    Assert.Contains("Text=\"{x:Bind RemotePath, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
    Assert.DoesNotContain("PathMappingRows", xaml, StringComparison.Ordinal);
    Assert.DoesNotContain("LocalRootPath", xaml, StringComparison.Ordinal);
}

[Fact]
public void AcpConnectionSettingsPage_RemoteDirectoriesEditor_ExposesStableAutomationIds()
{
    var xaml = ReadAcpSettingsXaml();

    Assert.Contains("AutomationProperties.AutomationId=\"Acp.RemoteDirectories.Section\"", xaml, StringComparison.Ordinal);
    Assert.Contains("AutomationProperties.AutomationId=\"Acp.RemoteDirectories.List\"", xaml, StringComparison.Ordinal);
    Assert.Contains("AutomationProperties.AutomationId=\"Acp.RemoteDirectories.Add\"", xaml, StringComparison.Ordinal);
    Assert.DoesNotContain("Acp.PathMappings", xaml, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter FullyQualifiedName~AcpConnectionSettingsXamlTests
```

Expected: FAIL because XAML still references mapping rows.

- [ ] **Step 3: Update XAML**

Replace the path mapping section with:

```xml
<!-- Remote directories -->
<StackPanel Spacing="12"
            AutomationProperties.AutomationId="Acp.RemoteDirectories.Section">
    <Grid ColumnDefinitions="*,Auto">
        <TextBlock x:Uid="Acp_RemoteDirectoriesTitle"
                   Text="远端目录"
                   Style="{StaticResource SettingsSectionTitleTextStyle}"
                   VerticalAlignment="Center"/>
        <Button Grid.Column="1"
                x:Name="AcpRemoteDirectoriesAddButton"
                x:Uid="Acp_RemoteDirectoriesAdd"
                Content="新增目录"
                Command="{x:Bind ViewModel.AddRemoteDirectoryCommand}"
                AutomationProperties.AutomationId="Acp.RemoteDirectories.Add"/>
    </Grid>

    <TextBlock x:Uid="Acp_RemoteDirectoriesHint"
               Text="为当前 Agent 配置可用于新建远程 ACP 会话的远端工作目录。Salmon Egg 不会把本机路径发送给远端 Agent。"
               Style="{StaticResource SettingsRowDescriptionTextStyle}"/>

    <Border Style="{StaticResource SettingsSectionContainerStyle}">
        <ListView ItemsSource="{x:Bind ViewModel.RemoteDirectoryRows, Mode=OneWay}"
                  SelectionMode="None"
                  MinHeight="120"
                  Background="Transparent"
                  ItemContainerStyle="{StaticResource AgentListItemStyle}"
                  AutomationProperties.AutomationId="Acp.RemoteDirectories.List">
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="vm:AcpRemoteDirectoryRowViewModel">
                    <Grid Style="{StaticResource SettingsRowGridStyle}"
                          ColumnDefinitions="*,Auto"
                          RowDefinitions="Auto,Auto"
                          RowSpacing="8">
                        <TextBox Grid.Row="0"
                                 Grid.Column="0"
                                 x:Uid="Acp_RemoteDirectoriesDisplayName"
                                 Header="显示名称"
                                 PlaceholderText="Workspace"
                                 Text="{x:Bind DisplayName, Mode=TwoWay}"/>

                        <TextBox Grid.Row="1"
                                 Grid.Column="0"
                                 x:Uid="Acp_RemoteDirectoriesRemotePath"
                                 Header="远端目录"
                                 PlaceholderText="/home/user/project"
                                 Text="{x:Bind RemotePath, Mode=TwoWay}"/>

                        <Button Grid.Row="0"
                                Grid.RowSpan="2"
                                Grid.Column="1"
                                x:Uid="Acp_RemoteDirectoriesRemove"
                                Command="{x:Bind RemoveCommand}"
                                VerticalAlignment="Center"
                                Style="{StaticResource BorderlessIconButtonStyle}"
                                Content="&#xE74D;"/>
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Border>
</StackPanel>
```

- [ ] **Step 4: Run XAML tests to verify they pass**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter FullyQualifiedName~AcpConnectionSettingsXamlTests
```

Expected: PASS.

---

### Task 4: Rework Start Project Selector For Remote Profiles

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Start/StartProjectOptionViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ProjectSelectorPolicy.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Start/StartViewModel.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/ProjectSelectorPolicyTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Start/StartViewModelTests.cs`

- [ ] **Step 1: Write failing selector policy test**

Add:

```csharp
[Fact]
public void Project_WhenSelectedProjectIsDisabled_BlocksSubmitAndKeepsDisabledItemVisible()
{
    var policy = new ProjectSelectorPolicy();

    var projection = policy.Project(new ProjectSelectorPolicyInput(
        Identity: "project|remote",
        Projects: new[]
        {
            new StartProjectOptionViewModel(NavigationProjectIds.Unclassified, "Unclassified", isSelectable: false),
            new StartProjectOptionViewModel("local-a", "Local A", isSelectable: false),
            new StartProjectOptionViewModel("remote-directory:dir-a", "Remote A", isSelectable: true, remoteCwd: "/remote/a")
        },
        SelectedProjectId: NavigationProjectIds.Unclassified,
        PendingProjectIntentResolved: true,
        HasLegalFallback: false,
        Labels: Labels()));

    Assert.True(projection.Placeholder!.BlocksSubmit);
    Assert.Equal(3, projection.RealItems.Count);
    Assert.False(projection.RealItems[0].IsSelectable);
    Assert.False(projection.RealItems[1].IsSelectable);
    Assert.True(projection.RealItems[2].IsSelectable);
}
```

- [ ] **Step 2: Write failing StartViewModel behavior tests**

Add:

```csharp
[Fact]
public async Task StartProjectSelector_RemoteProfile_DisablesUnclassifiedAndLocalProjectsButEnablesRemoteDirectories()
{
    var originalContext = SynchronizationContext.Current;
    var syncContext = new ImmediateSynchronizationContext();
    SynchronizationContext.SetSynchronizationContext(syncContext);
    try
    {
        var preferences = CreatePreferences();
        preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = "local-a",
            Name = "Local A",
            RootPath = @"C:\Repo\A"
        });
        preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
        {
            ProfileId = "profile-remote",
            DirectoryId = "dir-a",
            DisplayName = "Remote A",
            RemotePath = "/remote/a"
        });

        using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
        chat.ViewModel.AcpProfileList.Add(new ServerConfiguration
        {
            Id = "profile-remote",
            Name = "Remote",
            Transport = TransportType.WebSocket,
            ServerUrl = "ws://127.0.0.1:3010/"
        });
        chat.ViewModel.SelectedAcpProfile = chat.ViewModel.AcpProfileList[0];

        using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
        var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, Mock.Of<IChatLaunchWorkflow>());

        var items = startViewModel.StartProjectSelectorItems;

        Assert.Contains(items, item => item.SemanticValue == NavigationProjectIds.Unclassified && !item.IsSelectable);
        Assert.Contains(items, item => item.SemanticValue == "local-a" && !item.IsSelectable);
        Assert.Contains(items, item => item.SemanticValue == "remote-directory:dir-a" && item.IsSelectable);
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(originalContext);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ProjectSelectorPolicyTests|FullyQualifiedName~StartViewModelTests.StartProjectSelector_RemoteProfile"
```

Expected: FAIL or compile errors because selector option metadata does not exist.

- [ ] **Step 4: Extend StartProjectOptionViewModel**

Replace file contents with:

```csharp
namespace SalmonEgg.Presentation.ViewModels.Start;

public sealed class StartProjectOptionViewModel
{
    public StartProjectOptionViewModel(
        string projectId,
        string displayName,
        bool isSelectable = true,
        string? remoteCwd = null)
    {
        ProjectId = projectId ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        IsSelectable = isSelectable;
        RemoteCwd = string.IsNullOrWhiteSpace(remoteCwd) ? null : remoteCwd.Trim();
    }

    public string ProjectId { get; }

    public string DisplayName { get; }

    public bool IsSelectable { get; }

    public string? RemoteCwd { get; }
}
```

- [ ] **Step 5: Update ProjectSelectorPolicy**

Create real items with selectability:

```csharp
var realItems = (input.Projects ?? Array.Empty<StartProjectOptionViewModel>())
    .Where(static project => !string.IsNullOrWhiteSpace(project.ProjectId))
    .Select(project =>
    {
        var item = ComposerSelectorItemViewModel.Real(
            ComposerSelectorKind.Project,
            project.ProjectId,
            string.IsNullOrWhiteSpace(project.DisplayName) ? project.ProjectId : project.DisplayName,
            input.Identity);
        return project.IsSelectable ? item : item.AsDisabled();
    })
    .ToArray();
```

Before the final return, add:

```csharp
var selectedProjectId = string.IsNullOrWhiteSpace(input.SelectedProjectId)
    ? NavigationProjectIds.Unclassified
    : input.SelectedProjectId;
var selectedItem = realItems.FirstOrDefault(item =>
    string.Equals(item.SemanticValue, selectedProjectId, StringComparison.Ordinal));
if (selectedItem is not null && !selectedItem.IsSelectable)
{
    var blocked = ComposerSelectorItemViewModel.Placeholder(
        ComposerSelectorKind.Project,
        SelectorPlaceholderKind.Unresolved,
        input.Labels.Unresolved,
        input.Identity,
        blocksSubmit: true);

    return new SelectorPolicyProjection(
        realItems,
        selectedProjectId,
        blocked,
        ReplaceSelectionWithPlaceholder: true,
        DisableRealItems: false,
        SelectorEnabled: realItems.Length > 0);
}
```

Copy note: the blocked-selection placeholder above reuses `input.Labels.Unresolved` ("未就绪"), but the real situation here is "the selected local project is not usable under a remote Agent — pick a remote directory." Reusing the unresolved label is misleading. Add a dedicated label (e.g. `ProjectSelectorPlaceholderLabels.RemoteSelectionRequired`, backed by a new `CoreStrings` key) and use it for this branch. Wire it through `ProjectSelectorPlaceholderLabels` and its construction sites.

- [ ] **Step 6: Update StartViewModel option building**

Add helpers:

```csharp
private bool IsSelectedProfileRemote()
    => Chat.SelectedAcpProfile?.Transport is TransportType.WebSocket or TransportType.HttpSse;

private static string BuildRemoteDirectoryProjectId(string directoryId)
    => $"remote-directory:{directoryId}";

private StartProjectOptionViewModel? ResolveSelectedProjectOption()
    => StartProjectOptions.FirstOrDefault(option =>
        string.Equals(option.ProjectId, SelectedStartProjectId, StringComparison.Ordinal));
```

Update `BuildStartProjectOptions()` so remote profiles include:

```csharp
var isRemoteProfile = IsSelectedProfileRemote();
var options = new List<StartProjectOptionViewModel>
{
    new(NavigationProjectIds.Unclassified, Localize("Nav_Unclassified", "未归类"), isSelectable: !isRemoteProfile)
};

foreach (var project in _projectPreferences.Projects
             .Where(IsSelectableProject)
             .OrderBy(project => project.Name, StringComparer.Ordinal))
{
    options.Add(new StartProjectOptionViewModel(project.ProjectId, project.Name, isSelectable: !isRemoteProfile));
}

if (isRemoteProfile && !string.IsNullOrWhiteSpace(Chat.SelectedAcpProfile?.Id))
{
    foreach (var directory in _preferences.AgentRemoteDirectories
                 .Where(directory => string.Equals(directory.ProfileId, Chat.SelectedAcpProfile.Id, StringComparison.Ordinal))
                 .Where(directory => !string.IsNullOrWhiteSpace(directory.DirectoryId)
                                     && !string.IsNullOrWhiteSpace(directory.RemotePath))
                 .OrderBy(directory => string.IsNullOrWhiteSpace(directory.DisplayName) ? directory.RemotePath : directory.DisplayName, StringComparer.Ordinal))
    {
        options.Add(new StartProjectOptionViewModel(
            BuildRemoteDirectoryProjectId(directory.DirectoryId),
            string.IsNullOrWhiteSpace(directory.DisplayName) ? directory.RemotePath : directory.DisplayName,
            isSelectable: true,
            remoteCwd: directory.RemotePath));
    }
}

return options;
```

Update `ResolvePreviewCwd()`:

```csharp
private string? ResolvePreviewCwd()
{
    var selectedOption = ResolveSelectedProjectOption();
    if (!string.IsNullOrWhiteSpace(selectedOption?.RemoteCwd))
    {
        return selectedOption.RemoteCwd;
    }

    return _projectPreferences.TryGetProjectRootPath(SelectedStartProjectId);
}
```

Update `ResolveDefaultCwd()` to use the same remote-cwd precedence.

- [ ] **Step 7: Run tests to verify they pass**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ProjectSelectorPolicyTests|FullyQualifiedName~StartViewModelTests.StartProjectSelector_RemoteProfile"
```

Expected: PASS.

---

### Task 5: Make ACP Session/New CWD Protocol-Faithful

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/Services/Chat/AcpSessionNewCwdResolver.cs`
- Modify: `src/SalmonEgg.Presentation.Core/Services/Chat/IAcpConnectionState.cs`
- Modify: `src/SalmonEgg.Presentation.Core/Services/Chat/AcpSessionCommandOrchestrator.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.NewSessionDraft.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.CommandWorkflow.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Services/Chat/AcpSessionNewCwdResolverTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Start/StartViewModelTests.cs`

- [ ] **Step 1: Write failing resolver tests**

Replace existing mapping tests with:

```csharp
[Fact]
public void Resolve_RemoteWithConfiguredRemoteDirectory_ReturnsRemotePath()
{
    var profile = new ServerConfiguration
    {
        Id = "profile-1",
        Transport = TransportType.WebSocket
    };
    var directories = new[]
    {
        new AgentRemoteDirectory
        {
            ProfileId = "profile-1",
            DirectoryId = "dir-1",
            DisplayName = "Workspace",
            RemotePath = "/home/user/project"
        }
    };

    var result = AcpSessionNewCwdResolver.Resolve(
        requestedCwd: " /home/user/project ",
        profile: profile,
        remoteDirectories: directories);

    Assert.True(result.IsSuccess);
    Assert.Equal("/home/user/project", result.Cwd);
}

[Fact]
public void Resolve_RemoteWithUnconfiguredCwd_ReturnsFailure()
{
    var profile = new ServerConfiguration
    {
        Id = "profile-1",
        Transport = TransportType.WebSocket
    };

    var result = AcpSessionNewCwdResolver.Resolve(
        requestedCwd: @"C:\repos\local",
        profile: profile,
        remoteDirectories: Array.Empty<AgentRemoteDirectory>());

    Assert.False(result.IsSuccess);
    Assert.Null(result.Cwd);
    Assert.Equal(AcpSessionNewCwdResolver.MissingRemoteCwdMessage, result.ErrorMessage);
}
```

- [ ] **Step 2: Run resolver tests to verify they fail**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter FullyQualifiedName~AcpSessionNewCwdResolverTests
```

Expected: FAIL or compile error because resolver still accepts path mappings.

- [ ] **Step 3: Replace resolver signature and logic**

Use:

```csharp
public static AcpSessionNewCwdResolution Resolve(
    string? requestedCwd,
    ServerConfiguration? profile,
    IReadOnlyList<AgentRemoteDirectory>? remoteDirectories)
{
    var trimmedCwd = TrimOrNull(requestedCwd);
    if (profile?.Transport == TransportType.Stdio)
    {
        if (!string.IsNullOrWhiteSpace(trimmedCwd))
        {
            return new AcpSessionNewCwdResolution(true, trimmedCwd, null);
        }

        return new AcpSessionNewCwdResolution(true, GetDefaultStdioUserProfileDirectory(), null);
    }

    if (string.IsNullOrWhiteSpace(trimmedCwd)
        || string.IsNullOrWhiteSpace(profile?.Id)
        || !IsConfiguredRemoteDirectory(trimmedCwd, profile.Id, remoteDirectories))
    {
        return new AcpSessionNewCwdResolution(false, null, MissingRemoteCwdMessage);
    }

    return new AcpSessionNewCwdResolution(true, trimmedCwd, null);
}

private static bool IsConfiguredRemoteDirectory(
    string requestedCwd,
    string profileId,
    IReadOnlyList<AgentRemoteDirectory>? remoteDirectories)
{
    if (remoteDirectories is not { Count: > 0 })
    {
        return false;
    }

    foreach (var directory in remoteDirectories)
    {
        if (directory is null)
        {
            continue;
        }

        if (string.Equals(directory.ProfileId?.Trim(), profileId.Trim(), StringComparison.Ordinal)
            && string.Equals(directory.RemotePath?.Trim(), requestedCwd, StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}
```

Also rewrite the `MissingRemoteCwdMessage` constant so it no longer contains the phrase "path mapping" (the original text is `"Select a project or configure a remote path mapping before creating a remote session."`, which the Task 8 scan for `path mapping` will flag). Use, for example:

```csharp
public const string MissingRemoteCwdMessage =
    "Select a configured remote directory before creating a remote session.";
```

The resolver does not need an extra absolute-path check: `IsConfiguredRemoteDirectory` only matches rows that were already validated as absolute in Task 2's normalization, so any non-absolute `requestedCwd` simply fails to match and returns the failure result.

- [ ] **Step 4: Update all resolver callers**

Replace `_preferences.ProjectPathMappings` with `_preferences.AgentRemoteDirectories`.

Replace `sink.GetProjectPathMappings()` with `sink.GetAgentRemoteDirectories()`.

Update `IAcpConnectionState` default method:

```csharp
IReadOnlyList<AgentRemoteDirectory> GetAgentRemoteDirectories() => [];
```

Update the ChatViewModel implementation:

```csharp
public IReadOnlyList<AgentRemoteDirectory> GetAgentRemoteDirectories()
    => _owner._preferences.AgentRemoteDirectories;
```

- [ ] **Step 5: Add durable logging at cwd failures**

In new-session draft and command orchestrator failure paths, log structured profile and cwd facts without string interpolation:

```csharp
Logger.LogInformation(
    "ACP remote session cwd resolution rejected. profileId={ProfileId} transport={Transport} requestedCwd={RequestedCwd} reason={Reason}",
    profileId,
    profile?.Transport,
    cwd,
    cwdResolution.ErrorMessage ?? AcpSessionNewCwdResolver.MissingRemoteCwdMessage);
```

Keep logs at `Information` for user-actionable configuration failures and `Warning` only when an established session cannot be recovered due to missing cwd.

- [ ] **Step 6: Run tests to verify they pass**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~AcpSessionNewCwdResolverTests|FullyQualifiedName~StartViewModelTests"
```

Expected: PASS.

---

### Task 6: Remove Legacy Mapping From Affinity, Search, Discovery, Navigation

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/ProjectAffinityResolver.cs`
- Modify: `src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/IProjectAffinityResolver.cs`
- Modify: `src/SalmonEgg.Presentation.Core/Services/ProjectAffinity/ProjectAffinityResolution.cs`
- Modify: `src/SalmonEgg.Presentation.Core/Services/INavigationProjectPreferences.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/GlobalSearchViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Discover/DiscoverSessionsViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Navigation/MainNavigationViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatShellViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.SessionPresentation.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ProjectAffinity/*.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/ProjectAffinity/ProjectAffinityResolverTests.cs`
- Test: affected tests found by `rg "PathMapping|NeedsMapping|ProjectPathMappings" tests/SalmonEgg.Presentation.Core.Tests`

- [ ] **Step 1: Write failing affinity tests**

Replace mapping-match tests with:

```csharp
[Fact]
public void Resolve_RemoteBoundNoDirectLocalProjectMatch_ReturnsNeedsMapping()
{
    var resolver = new ProjectAffinityResolver();

    var result = resolver.Resolve(new ProjectAffinityRequest(
        RemoteSessionId: "remote-1",
        BoundProfileId: "profile-1",
        RemoteCwd: "/remote/repo",
        OverrideProjectId: null,
        Projects: new[]
        {
            new ProjectDefinition
            {
                ProjectId = "local",
                Name = "Local",
                RootPath = @"C:\Local\Repo"
            }
        },
        UnclassifiedProjectId: NavigationProjectIds.Unclassified));

    Assert.Equal(NavigationProjectIds.Unclassified, result.EffectiveProjectId);
    Assert.Equal(ProjectAffinitySource.NeedsMapping, result.Source);
    Assert.True(result.NeedsUserAttention);
}
```

- [ ] **Step 2: Remove path mapping from affinity request types**

Remove `PathMappings` from `ProjectAffinityRequest` and from every call site.

Remove `ProjectAffinitySource.PathMapping`.

Keep `ProjectAffinitySource.NeedsMapping` if the product still uses it to mean “remote session has a cwd that does not correspond to any local project and needs manual classification”.

- [ ] **Step 3: Simplify ProjectAffinityResolver**

Delete `ResolveMapping` and `TryResolveMappedPath`.

Resolution order becomes:

1. explicit override,
2. missing cwd -> unclassified,
3. direct local project match,
4. remote-bound fallback -> needs mapping/user attention,
5. local/unbound fallback -> unclassified.

**Scope note (intentional behavior change):** today `ResolveMapping` classifies a discovered remote session by translating its remote cwd back to a local project. Deleting mappings removes that capability, so previously-classified remote sessions become "needs attention". This is intended per the goal of decoupling remote directories from local paths.

**Required parity enhancement (decided 2026-06-17):** the new model still knows that `RemotePath = "/remote/a"` is named "Remote A" for a given profile. Between steps 3 and 4, add a classification step: if the request is remote-bound and its normalized cwd exactly matches a configured `AgentRemoteDirectory.RemotePath` for the bound profile, classify it (surface the directory `DisplayName`) instead of dropping straight to `NeedsMapping`. This requires threading `IReadOnlyList<AgentRemoteDirectory>` into `ProjectAffinityRequest` (replacing the removed `PathMappings`), and adding a new `ProjectAffinitySource` value (e.g. `RemoteDirectory`) so callers can render the directory name. Add a resolver unit test that a remote-bound request whose cwd matches a configured directory resolves to that classification (not `NeedsMapping`).

- [ ] **Step 4: Update callers**

Remove every argument named `PathMappings`.

Replace old status copy:

```csharp
ProjectAffinitySource.PathMapping => ...
```

with no branch. For `NeedsMapping`, change user-facing text to avoid saying “path mapping”; use “Remote working directory needs a project assignment.”

- [ ] **Step 5: Run affinity/search/discovery tests**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ProjectAffinity|FullyQualifiedName~DiscoverSessionsViewModelTests|FullyQualifiedName~GlobalSearchViewModelTests|FullyQualifiedName~MainNavigationViewModelSelectionTests"
```

Expected: PASS.

---

### Task 7: Update GUI Smoke Contracts And Automation IDs

**Files:**
- Modify: `tests/SalmonEgg.GuiTests.Windows/ChatSkeletonSmokeTests.cs`
- Modify: `tests/SalmonEgg.GuiTests.Windows/RealUserConfigSmokeTests.cs`

- [ ] **Step 1: Replace AutomationIds**

Replace:

```csharp
"Acp.PathMappings.Section"
"Acp.PathMappings.List"
```

with:

```csharp
"Acp.RemoteDirectories.Section"
"Acp.RemoteDirectories.List"
```

- [ ] **Step 2: Update smoke wording**

If a smoke assertion message mentions path mappings, change it to remote directories.

- [ ] **Step 3: Run GUI test compile gate**

Run:

```powershell
dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --no-restore --filter FullyQualifiedName~RealUserConfigSmokeTests
```

Expected: If the test runner requires an installed app or UI session, record the exact environmental failure. Compile errors are not acceptable.

---

### Task 8: Remove Legacy Names Completely

**Files:**
- All source and tests.

- [ ] **Step 1: Run legacy-name scan**

Run:

```powershell
rg -n "ProjectPathMapping|ProjectPathMappings|AcpPathMapping|PathMappingRows|AddPathMapping|RemoteRootPath|LocalRootPath|Acp\\.PathMappings|path mapping|路径映射" src tests SalmonEgg
```

Expected: no hits except in this plan document if the scan includes `docs`.

- [ ] **Step 2: Fix remaining hits**

For each hit:

- If it is product code, remove or rename it.
- If it is a test, update the test to the new remote-directory semantic.
- If it is resource text, replace “path mapping” with “remote directory” or “project assignment” depending on context.

- [ ] **Step 3: Run source scan again**

Run:

```powershell
rg -n "ProjectPathMapping|ProjectPathMappings|AcpPathMapping|PathMappingRows|AddPathMapping|RemoteRootPath|LocalRootPath|Acp\\.PathMappings|path mapping|路径映射" src tests SalmonEgg
```

Expected: no output.

---

### Task 9: Final Verification

**Files:**
- No additional code files.

- [ ] **Step 1: Run targeted unit tests**

Run:

```powershell
dotnet test tests\SalmonEgg.Domain.Tests\SalmonEgg.Domain.Tests.csproj
dotnet test tests\SalmonEgg.Infrastructure.Tests\SalmonEgg.Infrastructure.Tests.csproj
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~AcpSessionNewCwdResolverTests|FullyQualifiedName~StartViewModelTests|FullyQualifiedName~ProjectSelectorPolicyTests|FullyQualifiedName~AcpConnectionSettingsViewModelTests|FullyQualifiedName~AcpConnectionSettingsXamlTests|FullyQualifiedName~ProjectAffinityResolverTests"
```

Expected: PASS.

- [ ] **Step 2: Run broader affected tests**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~DiscoverSessionsViewModelTests|FullyQualifiedName~GlobalSearchViewModelTests|FullyQualifiedName~MainNavigationViewModelSelectionTests|FullyQualifiedName~ChatViewModelTests"
```

Expected: PASS.

- [ ] **Step 3: Build**

Run:

```powershell
dotnet build SalmonEgg.sln
```

Expected: build succeeds. If existing unrelated warnings exist, record them; do not introduce new warnings.

- [ ] **Step 4: Optional GUI smoke**

Run only if a fresh app package or local run instance is available:

```powershell
dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~RealUserConfigSmokeTests|FullyQualifiedName~ChatSkeletonSmokeTests"
```

Expected: settings page exposes `Acp.RemoteDirectories.*`, and there are no `Acp.PathMappings.*` elements.

- [ ] **Step 5: Final git scan**

Run:

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors. Review changed files and ensure no unrelated formatting churn.

---

## Logging Requirements

- Use structured logging templates only.
- Keep logs at durable business boundaries:
  - remote cwd rejected because no configured remote directory,
  - remote directory row added/removed only if there is an existing settings logger path that already logs settings mutations,
  - `session/new` skipped due to missing remote cwd.
- Do not log on every selector projection refresh.
- Do not log raw prompt content or credentials.
- Include `ProfileId`, `Transport`, `RequestedCwd`, and `Reason` when rejecting remote cwd.

## Completion Criteria

- `ProjectPathMapping` type no longer exists.
- `AppSettings` and YAML save output no longer include `ProjectPathMappings`.
- Settings page has a remote directories module with stable `Acp.RemoteDirectories.*` AutomationIds.
- Remote Start project selector shows unclassified and local projects disabled under remote profiles.
- Remote Start project selector shows configured remote directories enabled under their owning profile.
- `session/new.cwd` for remote profiles can only come from an explicitly configured remote directory.
- A remote directory with a non-absolute `RemotePath` is rejected during normalization (reusing `ProtocolPathRules.IsAbsolutePath`) and never becomes selectable.
- `MissingRemoteCwdMessage` no longer contains the phrase "path mapping".
- Local stdio behavior remains unchanged: missing cwd falls back to `Environment.SpecialFolder.UserProfile`.
- Tests prove stale/disabled project selections cannot submit a remote session.
- Legacy-name scan returns no product/test hits.
