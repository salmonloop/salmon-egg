# ACP Model Selector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an ACP-standard model selector to the chat/start composer, driven only by `category="model"` session config options and applied with `session/set_config_option`.

**Architecture:** Treat model as the same family of ACP session config state as mode, but without inventing a non-standard model API. Config options remain the authoritative source: protocol response -> `AcpSessionUpdateProjector` -> store slices -> `ChatSessionOptionsPresenter` -> ViewModel selector projections -> XAML `ComboBox` slots. Active sessions and new-session drafts use the same presenter/policy pipeline so Start and Chat do not fork model semantics.

**Tech Stack:** C#/.NET 10, Uno/WinUI 3 XAML, CommunityToolkit.Mvvm, xUnit, Moq, ACP `session/set_config_option`.

## Global Constraints

- Strict MVVM: View is driven by ViewModel projections; no code-behind selector state machine.
- ACP compliance: model selection uses only `SessionConfigOption` with `category="model"` and `session/set_config_option`.
- No dirty patches: no custom ACP extension, no local string guessing beyond standard category matching, no duplicated state owner.
- Graceful degradation: when the agent does not return a `category="model"` config option with choices, the model selector slot is hidden.
- Existing selector architecture must be followed: policy -> projection presenter -> `ComposerSelectorSlotsPresentation` -> `ChatInputArea`.
- Verification must include focused unit tests, XAML contract tests, build/test gates, and a smoke path for visible selector behavior.

---

## File Structure

- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ComposerSelectorKind.cs`
  - Add `Model` as a first-class selector kind.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorPlaceholderLabels.cs`
  - Add `ModelSelectorPlaceholderLabels`.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ModelSelectorPolicy.cs`
  - Keep model selector projection behavior isolated and parallel to mode/project selector policies.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Composer/ComposerSelectorSlotsPresentation.cs`
  - Add a `Model` slot.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Chat/SessionOptions/ChatSessionOptionsProjection.cs`
  - Project model choices, config id, and selected value from config options.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Chat/SessionOptions/ChatSessionOptionsPresenter.cs`
  - Resolve `category="model"` from config options and expose it as model selector state.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
  - Add active-session and draft model selector projection state.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.SessionPresentation.cs`
  - Store model projection fields when session state changes.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.CommandWorkflow.cs`
  - Add active chat model selector projection and set-config command path.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.NewSessionDraft.cs`
  - Add draft model option projection and draft set-config command path.
- Modify `src/SalmonEgg.Presentation.Core/ViewModels/Start/StartViewModel.cs`
  - Expose model selector in the start composer using the draft model state.
- Modify `SalmonEgg/SalmonEgg/Controls/ChatInputArea.xaml`
  - Add the model selector `ComboBox` bound through `SelectorSlots.Model`.
- Modify `SalmonEgg/SalmonEgg/Controls/ChatInputArea.xaml.cs`
  - Add `ModelSelectorAutomationId`, generic model selection forwarding, and focus topology participation.
- Modify `SalmonEgg/SalmonEgg/Presentation/Views/Start/StartView.xaml`
  - Set `ModelSelectorAutomationId="StartView.ModelSelector"`.
- Modify `src/SalmonEgg.Presentation.Core/Resources/CoreStrings*.resx`
  - Add localized model selector placeholder strings.
- Modify tests:
  - `tests/SalmonEgg.Presentation.Core.Tests/Chat/SessionOptions/ChatSessionOptionsPresenterTests.cs`
  - `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/ModelSelectorPolicyTests.cs`
  - `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`
  - `tests/SalmonEgg.Presentation.Core.Tests/Start/StartViewModelTests.cs`
  - `tests/SalmonEgg.Presentation.Core.Tests/Ui/XamlComplianceTests.cs`

---

### Task 1: Normalize Session Option Projection For Model

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ComposerSelectorKind.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorPlaceholderLabels.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ModelSelectorPolicy.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Composer/ComposerSelectorSlotsPresentation.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/SessionOptions/ChatSessionOptionsProjection.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/SessionOptions/ChatSessionOptionsPresenter.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/SessionOptions/ChatSessionOptionsPresenterTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/ModelSelectorPolicyTests.cs`

**Interfaces:**
- Consumes: existing `ConversationConfigOptionSnapshot`, `ConfigOptionViewModel`, `OptionValueViewModel`, `SelectorProjectionPresenter`.
- Produces:
  - `ComposerSelectorKind.Model`
  - `ChatSessionOptionsProjection.ModelOptions : IReadOnlyList<OptionValueViewModel>`
  - `ChatSessionOptionsProjection.ModelConfigId : string?`
  - `ChatSessionOptionsProjection.SelectedModelValue : string?`
  - `ChatSessionOptionsPresenter.ResolveSelectedModelOption(IReadOnlyList<OptionValueViewModel>, string?)`
  - `ModelSelectorPolicy.Project(ModelSelectorPolicyInput)`

- [ ] **Step 1: Write failing presenter tests**

Add these tests to `ChatSessionOptionsPresenterTests.cs`:

```csharp
[Fact]
public void Present_WithModelConfigOption_ProjectsModelSelectorState()
{
    var projection = _sut.Present(
        availableModes: [],
        selectedModeId: null,
        configOptions:
        [
            new ConversationConfigOptionSnapshot
            {
                Id = "model",
                Name = "Model",
                Category = "model",
                SelectedValue = "claude-sonnet",
                Options =
                [
                    new ConversationConfigOptionChoiceSnapshot { Value = "claude-haiku", Name = "Haiku" },
                    new ConversationConfigOptionChoiceSnapshot { Value = "claude-sonnet", Name = "Sonnet" }
                ]
            }
        ],
        showConfigOptionsPanel: true);

    Assert.Equal("model", projection.ModelConfigId);
    Assert.Equal("claude-sonnet", projection.SelectedModelValue);
    Assert.Equal(["claude-haiku", "claude-sonnet"], projection.ModelOptions.Select(option => option.Value).ToArray());

    var selected = _sut.ResolveSelectedModelOption(projection.ModelOptions, projection.SelectedModelValue);
    Assert.NotNull(selected);
    Assert.Equal("claude-sonnet", selected!.Value);
}

[Fact]
public void Present_WithoutModelCategory_DoesNotProjectModelSelectorState()
{
    var projection = _sut.Present(
        availableModes: [],
        selectedModeId: null,
        configOptions:
        [
            new ConversationConfigOptionSnapshot
            {
                Id = "temperature",
                Name = "Temperature",
                Category = "sampling",
                SelectedValue = "0.7",
                Options =
                [
                    new ConversationConfigOptionChoiceSnapshot { Value = "0.7", Name = "0.7" }
                ]
            }
        ],
        showConfigOptionsPanel: true);

    Assert.Null(projection.ModelConfigId);
    Assert.Null(projection.SelectedModelValue);
    Assert.Empty(projection.ModelOptions);
}
```

- [ ] **Step 2: Write failing model policy tests**

Create `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/ModelSelectorPolicyTests.cs`:

```csharp
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Selectors;

public sealed class ModelSelectorPolicyTests
{
    [Fact]
    public void Project_WhenNoModelConfig_ReturnsDisabledEmptyProjection()
    {
        var projection = new ModelSelectorPolicy().Project(new ModelSelectorPolicyInput(
            Identity: "profile|conn|cwd|model|0",
            CurrentIdentity: "profile|conn|cwd|model|0",
            ModelOptions: Array.Empty<OptionValueViewModel>(),
            SelectedModelValue: null,
            IsAuthoritative: true,
            IsLoading: false,
            HasError: false,
            Labels: Labels()));

        Assert.Empty(projection.RealItems);
        Assert.Null(projection.Placeholder);
        Assert.False(projection.SelectorEnabled);
    }

    [Fact]
    public void Project_WhenReadyWithModels_UsesRealItemsWithoutPlaceholder()
    {
        var projection = new ModelSelectorPolicy().Project(new ModelSelectorPolicyInput(
            Identity: "profile|conn|cwd|model|2",
            CurrentIdentity: "profile|conn|cwd|model|2",
            ModelOptions:
            [
                Model("claude-haiku", "Haiku"),
                Model("claude-sonnet", "Sonnet")
            ],
            SelectedModelValue: "claude-sonnet",
            IsAuthoritative: true,
            IsLoading: false,
            HasError: false,
            Labels: Labels()));

        Assert.Null(projection.Placeholder);
        Assert.True(projection.SelectorEnabled);
        Assert.Equal("claude-sonnet", projection.SelectedSemanticValue);
        Assert.Equal(ComposerSelectorKind.Model, projection.RealItems[0].Kind);
        Assert.Equal(["claude-haiku", "claude-sonnet"], projection.RealItems.Select(item => item.SemanticValue).ToArray());
    }

    [Fact]
    public void Project_WhenLoadingWithExistingModels_UsesNonBlockingLoadingPlaceholder()
    {
        var projection = new ModelSelectorPolicy().Project(new ModelSelectorPolicyInput(
            Identity: "profile|conn|cwd|model|1",
            CurrentIdentity: "profile|conn|cwd|model|1",
            ModelOptions: [Model("claude-sonnet", "Sonnet")],
            SelectedModelValue: "claude-sonnet",
            IsAuthoritative: false,
            IsLoading: true,
            HasError: false,
            Labels: Labels()));

        Assert.Equal(SelectorPlaceholderKind.Loading, projection.Placeholder!.PlaceholderKind);
        Assert.False(projection.Placeholder.BlocksSubmit);
        Assert.True(projection.ReplaceSelectionWithPlaceholder);
    }

    private static OptionValueViewModel Model(string value, string name)
        => new()
        {
            Value = value,
            Name = name,
            Description = string.Empty
        };

    private static ModelSelectorPlaceholderLabels Labels()
        => new(
            Unresolved: "model-unresolved",
            Loading: "model-loading",
            Error: "model-error");
}
```

- [ ] **Step 3: Run tests and verify they fail**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ChatSessionOptionsPresenterTests|FullyQualifiedName~ModelSelectorPolicyTests"
```

Expected: failure because the projection and policy are not fully wired.

- [ ] **Step 4: Implement selector contracts**

Use these final shapes.

`ComposerSelectorKind.cs`:

```csharp
namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public enum ComposerSelectorKind
{
    Agent = 0,
    Mode = 1,
    Project = 2,
    Model = 3
}
```

Append this to `SelectorPlaceholderLabels.cs`:

```csharp
public sealed record ModelSelectorPlaceholderLabels(
    string Unresolved,
    string Loading,
    string Error);
```

Replace `ModelSelectorPolicy.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record ModelSelectorPolicyInput(
    string Identity,
    string CurrentIdentity,
    IReadOnlyList<OptionValueViewModel> ModelOptions,
    string? SelectedModelValue,
    bool IsAuthoritative,
    bool IsLoading,
    bool HasError,
    ModelSelectorPlaceholderLabels Labels);

public sealed class ModelSelectorPolicy
{
    public SelectorPolicyProjection Project(ModelSelectorPolicyInput input)
    {
        var realItems = ToRealItems(input.ModelOptions, input.CurrentIdentity);

        if (!string.Equals(input.Identity, input.CurrentIdentity, StringComparison.Ordinal))
        {
            return WithPlaceholder(realItems, input.SelectedModelValue, SelectorPlaceholderKind.Unresolved, input.Labels.Unresolved, input.CurrentIdentity);
        }

        if (input.IsLoading)
        {
            return WithPlaceholder(realItems, input.SelectedModelValue, SelectorPlaceholderKind.Loading, input.Labels.Loading, input.CurrentIdentity);
        }

        if (input.HasError)
        {
            return WithPlaceholder(realItems, input.SelectedModelValue, SelectorPlaceholderKind.Error, input.Labels.Error, input.CurrentIdentity);
        }

        if (!input.IsAuthoritative)
        {
            return WithPlaceholder(realItems, input.SelectedModelValue, SelectorPlaceholderKind.Unresolved, input.Labels.Unresolved, input.CurrentIdentity);
        }

        return realItems.Count > 0
            ? new SelectorPolicyProjection(realItems, input.SelectedModelValue, Placeholder: null, ReplaceSelectionWithPlaceholder: false, DisableRealItems: false, SelectorEnabled: true)
            : new SelectorPolicyProjection(realItems, input.SelectedModelValue, Placeholder: null, ReplaceSelectionWithPlaceholder: false, DisableRealItems: false, SelectorEnabled: false);
    }

    private static IReadOnlyList<ComposerSelectorItemViewModel> ToRealItems(
        IReadOnlyList<OptionValueViewModel> options,
        string identity)
        => options
            .Where(static option => !string.IsNullOrWhiteSpace(option.Value))
            .Select(option => ComposerSelectorItemViewModel.Real(
                ComposerSelectorKind.Model,
                option.Value,
                string.IsNullOrWhiteSpace(option.Name) ? option.Value : option.Name,
                identity))
            .ToArray();

    private static SelectorPolicyProjection WithPlaceholder(
        IReadOnlyList<ComposerSelectorItemViewModel> realItems,
        string? selectedValue,
        SelectorPlaceholderKind kind,
        string displayName,
        string identity)
    {
        var placeholder = ComposerSelectorItemViewModel.Placeholder(
            ComposerSelectorKind.Model,
            kind,
            displayName,
            identity,
            blocksSubmit: false);

        return new SelectorPolicyProjection(
            realItems,
            selectedValue,
            placeholder,
            ReplaceSelectionWithPlaceholder: true,
            DisableRealItems: true,
            SelectorEnabled: true);
    }
}
```

`ComposerSelectorSlotsPresentation.cs`:

```csharp
namespace SalmonEgg.Presentation.Core.ViewModels.Composer;

public sealed record ComposerSelectorSlotsPresentation(
    ComposerSelectorSlotPresentation Agent,
    ComposerSelectorSlotPresentation Mode,
    ComposerSelectorSlotPresentation Project,
    ComposerSelectorSlotPresentation Model)
{
    public static ComposerSelectorSlotsPresentation Empty { get; } = new(
        Agent: ComposerSelectorSlotPresentation.Hidden(),
        Mode: ComposerSelectorSlotPresentation.Hidden(),
        Project: ComposerSelectorSlotPresentation.Hidden(),
        Model: ComposerSelectorSlotPresentation.Hidden());
}
```

`ChatSessionOptionsProjection.cs`:

```csharp
using System.Collections.Generic;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.SessionOptions;

public sealed record ChatSessionOptionsProjection(
    IReadOnlyList<SessionModeViewModel> AvailableModes,
    string? SelectedModeId,
    IReadOnlyList<ConfigOptionViewModel> ConfigOptions,
    bool ShowConfigOptionsPanel,
    string? ModeConfigId,
    IReadOnlyList<OptionValueViewModel> ModelOptions,
    string? ModelConfigId,
    string? SelectedModelValue);
```

In `ChatSessionOptionsPresenter.cs`, change the model resolver to:

```csharp
public bool OptionCollectionMatches(
    IReadOnlyList<OptionValueViewModel> current,
    IReadOnlyList<OptionValueViewModel> projected)
{
    ArgumentNullException.ThrowIfNull(current);
    ArgumentNullException.ThrowIfNull(projected);

    if (current.Count != projected.Count)
    {
        return false;
    }

    for (var i = 0; i < current.Count; i++)
    {
        if (!string.Equals(current[i].Value, projected[i].Value, StringComparison.Ordinal)
            || !string.Equals(current[i].Name, projected[i].Name, StringComparison.Ordinal)
            || !string.Equals(current[i].Description, projected[i].Description, StringComparison.Ordinal))
        {
            return false;
        }
    }

    return true;
}

public OptionValueViewModel? ResolveSelectedModelOption(
    IReadOnlyList<OptionValueViewModel> modelOptions,
    string? selectedModelValue)
{
    ArgumentNullException.ThrowIfNull(modelOptions);

    if (modelOptions.Count == 0)
    {
        return null;
    }

    if (string.IsNullOrWhiteSpace(selectedModelValue))
    {
        return modelOptions[0];
    }

    return modelOptions.FirstOrDefault(option =>
               string.Equals(option.Value, selectedModelValue, StringComparison.Ordinal))
           ?? modelOptions[0];
}

private static (string? ModelConfigId, IReadOnlyList<OptionValueViewModel> ModelOptions, string? SelectedModelValue) TryResolveModelConfigSelection(
    IReadOnlyList<ConfigOptionViewModel> projectedConfigOptions)
{
    var modelOption = projectedConfigOptions.FirstOrDefault(option =>
        string.Equals(option.Category, "model", StringComparison.OrdinalIgnoreCase));

    if (modelOption is null || modelOption.Options.Count == 0)
    {
        return (null, Array.Empty<OptionValueViewModel>(), null);
    }

    var modelOptions = modelOption.Options
        .Select(static option => new OptionValueViewModel
        {
            Value = option.Value,
            Name = option.Name,
            Description = option.Description
        })
        .ToArray();

    var selectedValue = modelOption.SelectedOption?.Value ?? modelOption.TextValue;
    return (modelOption.Id, modelOptions, selectedValue);
}
```

Update the `Present(...)` return call so it passes:

```csharp
modelConfigSelection.ModelOptions,
modelConfigSelection.ModelConfigId,
modelConfigSelection.SelectedModelValue
```

- [ ] **Step 5: Run tests and verify they pass**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ChatSessionOptionsPresenterTests|FullyQualifiedName~ModelSelectorPolicyTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors src/SalmonEgg.Presentation.Core/ViewModels/Composer/ComposerSelectorSlotsPresentation.cs src/SalmonEgg.Presentation.Core/ViewModels/Chat/SessionOptions tests/SalmonEgg.Presentation.Core.Tests/Chat/SessionOptions/ChatSessionOptionsPresenterTests.cs tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/ModelSelectorPolicyTests.cs
git commit -m "feat(chat): project ACP model config options"
```

---

### Task 2: Wire Active Chat Model Selection Through ChatViewModel

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.SessionPresentation.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.CommandWorkflow.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`

**Interfaces:**
- Consumes: `ChatSessionOptionsProjection.ModelOptions`, `ModelConfigId`, `SelectedModelValue`.
- Produces:
  - `ChatViewModel.ChatModelSelectorProjection`
  - `ChatViewModel.ChatModelSelectorItems`
  - `ChatViewModel.SelectedChatModelSelectorItem`
  - generated `SelectChatModelDisplayCommand`
  - private `ApplyModelSelectionAsync(string conversationId, string remoteSessionId, string? modelValue)`

- [ ] **Step 1: Write failing ViewModel tests**

Add these tests near the existing chat selector tests in `ChatViewModelTests.cs`:

```csharp
[Fact]
public async Task ComposerSelectorSlots_ShowModelOnlyWhenModelConfigExists()
{
    await using var fixture = CreateViewModel();
    var viewModel = fixture.ViewModel;

    Assert.False(viewModel.ComposerSelectorSlots.Model.IsVisible);

    await fixture.DispatchAsync(new SetBindingSliceAction(new ConversationBindingSlice("conv-1", "remote-1", "profile-1")));
    await fixture.DispatchAsync(new SelectConversationAction("conv-1"));
    await fixture.DispatchAsync(new SetConversationSessionStateAction(
        "conv-1",
        ImmutableList<ConversationModeOptionSnapshot>.Empty,
        selectedModeId: null,
        ConfigOptions: CreateModelConfigSnapshots("claude-sonnet").ToImmutableList(),
        ShowConfigOptionsPanel: true));

    viewModel.IsConnected = true;
    viewModel.IsSessionActive = true;

    var slots = viewModel.ComposerSelectorSlots;
    Assert.True(slots.Model.IsVisible);
    Assert.True(slots.Model.IsEnabled);
    Assert.Same(viewModel.SelectChatModelDisplayCommand, slots.Model.SelectionCommand);
    Assert.Equal("claude-sonnet", slots.Model.SelectedItem?.SemanticValue);
}

[Fact]
public async Task SelectChatModelDisplay_WhenModelConfigExists_SetsAcpConfigOption()
{
    await using var fixture = CreateViewModel();
    var viewModel = fixture.ViewModel;
    var chatService = CreateConnectedChatService();
    chatService
        .Setup(service => service.SetSessionConfigOptionAsync(It.Is<SessionSetConfigOptionParams>(p =>
            p.SessionId == "remote-1"
            && p.ConfigId == "model"
            && p.Value == "claude-opus")))
        .ReturnsAsync(new SessionSetConfigOptionResponse(CreateModelConfigOptions("claude-opus")));
    viewModel.ReplaceChatService(chatService.Object);

    await fixture.DispatchAsync(new SetBindingSliceAction(new ConversationBindingSlice("conv-1", "remote-1", "profile-1")));
    await fixture.DispatchAsync(new SelectConversationAction("conv-1"));
    await fixture.DispatchAsync(new SetConversationSessionStateAction(
        "conv-1",
        ImmutableList<ConversationModeOptionSnapshot>.Empty,
        selectedModeId: null,
        ConfigOptions: CreateModelConfigSnapshots("claude-sonnet").ToImmutableList(),
        ShowConfigOptionsPanel: true));

    viewModel.IsConnected = true;
    viewModel.IsSessionActive = true;

    var opus = viewModel.ChatModelSelectorItems.Single(item => item.SemanticValue == "claude-opus");
    viewModel.SelectChatModelDisplayCommand.Execute(opus);

    chatService.Verify(service => service.SetSessionConfigOptionAsync(It.IsAny<SessionSetConfigOptionParams>()), Times.Once);
    chatService.Verify(service => service.SetSessionModeAsync(It.IsAny<SessionSetModeParams>()), Times.Never);
}
```

Add helpers near `CreateModeConfigOptions`:

```csharp
private static List<ConfigOption> CreateModelConfigOptions(string currentValue)
    => new()
    {
        new ConfigOption
        {
            Id = "model",
            Name = "Model",
            Category = "model",
            Type = "select",
            CurrentValue = currentValue,
            Options = new List<ConfigOptionValue>
            {
                new() { Value = "claude-sonnet", Name = "Sonnet" },
                new() { Value = "claude-opus", Name = "Opus" }
            }
        }
    };

private static List<ConversationConfigOptionSnapshot> CreateModelConfigSnapshots(string selectedValue)
    => new()
    {
        new ConversationConfigOptionSnapshot
        {
            Id = "model",
            Name = "Model",
            Category = "model",
            ValueType = "select",
            SelectedValue = selectedValue,
            Options = new List<ConversationConfigOptionChoiceSnapshot>
            {
                new() { Value = "claude-sonnet", Name = "Sonnet" },
                new() { Value = "claude-opus", Name = "Opus" }
            }
        }
    };
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ComposerSelectorSlots_ShowModelOnlyWhenModelConfigExists|FullyQualifiedName~SelectChatModelDisplay_WhenModelConfigExists_SetsAcpConfigOption"
```

Expected: failure because ChatViewModel has no model slot or command.

- [ ] **Step 3: Implement ChatViewModel active-session state**

In `ChatViewModel.cs`, add fields near `_modeSelectorPolicy` and `_modeConfigId`:

```csharp
private readonly ModelSelectorPolicy _modelSelectorPolicy = new();
private IReadOnlyList<OptionValueViewModel> _modelOptions = Array.Empty<OptionValueViewModel>();
private string? _modelConfigId;
private string? _selectedModelValue;
```

Add properties near `ChatModeSelectorProjection`:

```csharp
public SelectorProjectionResult ChatModelSelectorProjection => ResolveChatModelSelectorProjection();

public IReadOnlyList<ComposerSelectorItemViewModel> ChatModelSelectorItems
    => ChatModelSelectorProjection.DisplayItems;

public ComposerSelectorItemViewModel? SelectedChatModelSelectorItem
    => ChatModelSelectorProjection.SelectedDisplayItem;
```

Update `ComposerSelectorSlots`:

```csharp
public ComposerSelectorSlotsPresentation ComposerSelectorSlots
    => new(
        Agent: ComposerSelectorSlotPresentation.Hidden(),
        Mode: new(
            IsVisible: true,
            IsEnabled: AreComposerToolsEnabled,
            Items: ChatModeSelectorItems,
            SelectedItem: SelectedChatModeSelectorItem,
            SelectionCommand: SelectChatModeDisplayCommand),
        Project: ComposerSelectorSlotPresentation.Hidden(),
        Model: string.IsNullOrWhiteSpace(_modelConfigId)
            ? ComposerSelectorSlotPresentation.Hidden()
            : new ComposerSelectorSlotPresentation(
                IsVisible: true,
                IsEnabled: AreComposerToolsEnabled && ChatModelSelectorProjection.IsEnabled,
                Items: ChatModelSelectorItems,
                SelectedItem: SelectedChatModelSelectorItem,
                SelectionCommand: SelectChatModelDisplayCommand));
```

Update `NotifyComposerProjectionChanged()`:

```csharp
OnPropertyChanged(nameof(ChatModelSelectorProjection));
OnPropertyChanged(nameof(ChatModelSelectorItems));
OnPropertyChanged(nameof(SelectedChatModelSelectorItem));
```

In `ChatViewModel.SessionPresentation.cs`, update `ApplySessionStateProjection(...)`:

```csharp
_modeConfigId = projection.ModeConfigId;
_modelConfigId = projection.ModelConfigId;
_modelOptions = projection.ModelOptions;
_selectedModelValue = projection.SelectedModelValue;
```

- [ ] **Step 4: Implement active model projection and command**

In `ChatViewModel.CommandWorkflow.cs`, add:

```csharp
private SelectorProjectionResult ResolveChatModelSelectorProjection()
{
    if (string.IsNullOrWhiteSpace(_modelConfigId))
    {
        return SelectorProjectionResult.Empty(ComposerSelectorKind.Model);
    }

    var identity = BuildModelSelectorIdentity(
        SelectedProfileId,
        ConnectionInstanceId,
        GetActiveSessionCwdOrDefault(),
        _modelConfigId,
        _modelOptions.Count);

    var policy = _modelSelectorPolicy.Project(new ModelSelectorPolicyInput(
        Identity: identity,
        CurrentIdentity: identity,
        ModelOptions: _modelOptions,
        SelectedModelValue: _selectedModelValue,
        IsAuthoritative: IsSessionActive,
        IsLoading: IsConnecting || IsInitializing,
        HasError: HasConnectionError,
        Labels: ResolveModelSelectorPlaceholderLabels()));

    return _selectorProjectionPresenter.Present(new SelectorProjectionInput(
        ComposerSelectorKind.Model,
        policy.RealItems,
        policy.SelectedSemanticValue,
        policy.Placeholder,
        policy.ReplaceSelectionWithPlaceholder,
        policy.DisableRealItems,
        policy.SelectorEnabled && AreComposerToolsEnabled));
}

private static string BuildModelSelectorIdentity(
    string? profileId,
    string? connectionInstanceId,
    string? cwd,
    string? modelConfigId,
    long version)
    => string.Join(
        "|",
        profileId ?? string.Empty,
        connectionInstanceId ?? string.Empty,
        cwd ?? string.Empty,
        modelConfigId ?? string.Empty,
        version.ToString(CultureInfo.InvariantCulture));

private ModelSelectorPlaceholderLabels ResolveModelSelectorPlaceholderLabels()
    => new(
        Unresolved: Localize("Selector_Model_Unresolved", "模型尚未就绪"),
        Loading: Localize("Selector_Model_Loading", "正在加载模型..."),
        Error: Localize("Selector_Model_Error", "模型不可用"));

[RelayCommand]
private void SelectChatModelDisplay(ComposerSelectorItemViewModel? item)
{
    if (item is null
        || item.Kind != ComposerSelectorKind.Model
        || item.IsPlaceholder
        || !item.IsSelectable
        || string.IsNullOrWhiteSpace(item.SemanticValue))
    {
        return;
    }

    var current = ChatModelSelectorItems.FirstOrDefault(candidate =>
        string.Equals(candidate.SemanticValue, item.SemanticValue, StringComparison.Ordinal)
        && string.Equals(candidate.Identity, item.Identity, StringComparison.Ordinal));
    if (current is null)
    {
        return;
    }

    _ = SetModelAsync(item.SemanticValue);
}

private async Task SetModelAsync(string? modelValue)
{
    if (string.IsNullOrWhiteSpace(modelValue)
        || string.Equals(_selectedModelValue, modelValue, StringComparison.Ordinal))
    {
        return;
    }

    try
    {
        IsBusy = true;
        ClearError();

        var activeBinding = await ResolveActiveConversationBindingAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(activeBinding?.RemoteSessionId))
        {
            return;
        }

        await ApplyModelSelectionAsync(
            activeBinding.ConversationId,
            activeBinding.RemoteSessionId!,
            modelValue).ConfigureAwait(true);
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Failed to switch model");
        SetError($"Failed to switch model: {ex.Message}");
        await ApplyCurrentStoreProjectionAsync().ConfigureAwait(true);
    }
    finally
    {
        IsBusy = false;
    }
}

private async Task ApplyModelSelectionAsync(
    string conversationId,
    string remoteSessionId,
    string? modelValue)
{
    if (_chatService is null
        || string.IsNullOrWhiteSpace(remoteSessionId)
        || string.IsNullOrWhiteSpace(_modelConfigId)
        || string.IsNullOrWhiteSpace(modelValue))
    {
        return;
    }

    var response = await _chatService.SetSessionConfigOptionAsync(
        new SessionSetConfigOptionParams(remoteSessionId, _modelConfigId, modelValue)).ConfigureAwait(true);
    await ApplySessionConfigOptionResponseAsync(conversationId, response, remoteSessionId).ConfigureAwait(true);
}
```

- [ ] **Step 5: Run tests and verify they pass**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ComposerSelectorSlots_ShowModelOnlyWhenModelConfigExists|FullyQualifiedName~SelectChatModelDisplay_WhenModelConfigExists_SetsAcpConfigOption|FullyQualifiedName~ChatModeSelection_WhenProjectedSelectedModeRaisesSelectionChanged_DoesNotDispatchDuplicateRemoteModeSet"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.SessionPresentation.cs src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.CommandWorkflow.cs tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs
git commit -m "feat(chat): wire active ACP model selection"
```

---

### Task 3: Wire New-Session Draft Model State

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.NewSessionDraft.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`

**Interfaces:**
- Consumes: `NewSessionDraftState.ConfigOptions` and `ChatSessionOptionsProjection.ModelOptions`.
- Produces:
  - `ChatViewModel.NewSessionDraftModelOptions : ReadOnlyObservableCollection<OptionValueViewModel>`
  - `ChatViewModel.SelectedNewSessionDraftModelOption : OptionValueViewModel?`
  - `ChatViewModel.SelectedNewSessionDraftModelValue : string?`
  - `ChatViewModel.HasNewSessionDraftModelSelector : bool`

- [ ] **Step 1: Write failing draft test**

Add this test to `ChatViewModelTests.cs` near `SelectedNewSessionDraftMode_WhenDraftUsesConfigOptions_UpdatesRemoteDraftSession`:

```csharp
[Fact]
public async Task SelectedNewSessionDraftModel_WhenDraftUsesModelConfigOption_UpdatesRemoteDraftSession()
{
    await using var fixture = CreateViewModel();
    var chatService = CreateConnectedChatService();
    chatService.SetupGet(service => service.AgentCapabilities)
        .Returns(new AgentCapabilities(sessionCapabilities: new SessionCapabilities
        {
            Close = new SessionCloseCapabilities()
        }));
    chatService.Setup(service => service.CreateSessionAsync(It.IsAny<SessionNewParams>()))
        .ReturnsAsync(new SessionNewResponse(
            "remote-draft",
            configOptions: CreateModelConfigOptions("claude-sonnet")));
    chatService.Setup(service => service.SetSessionConfigOptionAsync(
            It.Is<SessionSetConfigOptionParams>(p =>
                p.SessionId == "remote-draft"
                && p.ConfigId == "model"
                && p.Value == "claude-opus")))
        .ReturnsAsync(new SessionSetConfigOptionResponse(CreateModelConfigOptions("claude-opus")));

    await fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object);
    await fixture.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
    await fixture.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
    await fixture.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
    await fixture.ViewModel.EnsureNewSessionDraftAsync(@"C:\Repo\App");

    var opus = fixture.ViewModel.NewSessionDraftModelOptions.Single(model => model.Value == "claude-opus");
    fixture.ViewModel.SelectedNewSessionDraftModelOption = opus;

    await WaitForConditionAsync(() => Task.FromResult(
        string.Equals(fixture.ViewModel.SelectedNewSessionDraftModelValue, "claude-opus", StringComparison.Ordinal)));

    chatService.Verify(service => service.SetSessionConfigOptionAsync(It.IsAny<SessionSetConfigOptionParams>()), Times.Once);
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~SelectedNewSessionDraftModel_WhenDraftUsesModelConfigOption_UpdatesRemoteDraftSession"
```

Expected: failure because the draft model state is not exposed.

- [ ] **Step 3: Add draft model properties and backing state**

In `ChatViewModel.cs`, add fields near `_newSessionDraftModeOptions`:

```csharp
private readonly ObservableCollection<OptionValueViewModel> _newSessionDraftModelOptions = new();
private CancellationTokenSource? _newSessionDraftModelSelectionCts;
private bool _suppressNewSessionDraftModelSelectionDispatch;
private string? _newSessionDraftModelConfigId;
private OptionValueViewModel? _selectedNewSessionDraftModelOption;
```

Add public properties near `NewSessionDraftModeOptions`:

```csharp
public ReadOnlyObservableCollection<OptionValueViewModel> NewSessionDraftModelOptions { get; }

public OptionValueViewModel? SelectedNewSessionDraftModelOption
{
    get => _selectedNewSessionDraftModelOption;
    set
    {
        if (string.Equals(_selectedNewSessionDraftModelOption?.Value, value?.Value, StringComparison.Ordinal))
        {
            return;
        }

        if (SetProperty(ref _selectedNewSessionDraftModelOption, value))
        {
            OnPropertyChanged(nameof(SelectedNewSessionDraftModelValue));
            if (!_suppressNewSessionDraftModelSelectionDispatch)
            {
                QueueNewSessionDraftModelSelection(value);
            }
        }
    }
}

public string? SelectedNewSessionDraftModelValue => SelectedNewSessionDraftModelOption?.Value;

public bool HasNewSessionDraftModelSelector => !string.IsNullOrWhiteSpace(_newSessionDraftModelConfigId);
```

Initialize the read-only collection in the constructor:

```csharp
NewSessionDraftModelOptions = new ReadOnlyObservableCollection<OptionValueViewModel>(_newSessionDraftModelOptions);
```

Dispose the CTS beside `_newSessionDraftModeSelectionCts`:

```csharp
try { _newSessionDraftModelSelectionCts?.Cancel(); _newSessionDraftModelSelectionCts?.Dispose(); } catch { }
```

- [ ] **Step 4: Add draft projection and remote update logic**

In `ChatViewModel.NewSessionDraft.cs`, add:

```csharp
private void QueueNewSessionDraftModelSelection(OptionValueViewModel? model)
{
    try
    {
        _newSessionDraftModelSelectionCts?.Cancel();
        _newSessionDraftModelSelectionCts?.Dispose();
    }
    catch
    {
    }

    if (model is null || string.IsNullOrWhiteSpace(model.Value))
    {
        return;
    }

    _newSessionDraftModelSelectionCts = new CancellationTokenSource();
    var token = _newSessionDraftModelSelectionCts.Token;
    _ = SetNewSessionDraftModelAsync(model.Value, token);
}

private async Task SetNewSessionDraftModelAsync(string modelValue, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();

    await _newSessionDraftGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
        var draft = connectionState.NewSessionDraft;
        if (draft is null
            || draft.Phase != NewSessionDraftPhase.Ready
            || string.IsNullOrWhiteSpace(draft.RemoteSessionId)
            || string.Equals(ResolveSelectedModelValue(draft.ConfigOptions), modelValue, StringComparison.Ordinal))
        {
            return;
        }

        if (!IsCurrentNewSessionDraft(connectionState, draft)
            || _chatService is not { IsConnected: true, IsInitialized: true } chatService)
        {
            await ClearNewSessionDraftStateAsync().ConfigureAwait(false);
            return;
        }

        var modelConfigId = ResolveModelConfigId(draft.ConfigOptions);
        if (string.IsNullOrWhiteSpace(modelConfigId))
        {
            return;
        }

        var response = await chatService.SetSessionConfigOptionAsync(
            new SessionSetConfigOptionParams(draft.RemoteSessionId!, modelConfigId!, modelValue)).ConfigureAwait(false);
        if (response.ConfigOptions is null)
        {
            await ApplyNewSessionDraftProjectionAsync(connectionState).ConfigureAwait(false);
            return;
        }

        var delta = _acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(
            draft.RemoteSessionId!,
            new ConfigOptionUpdate
            {
                ConfigOptions = response.ConfigOptions
            }));

        var updatedDraft = MergeNewSessionDraftDelta(draft, delta);
        await _chatConnectionStore.Dispatch(new SetNewSessionDraftAction(updatedDraft)).ConfigureAwait(false);
        await ApplyNewSessionDraftProjectionAsync(
            await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
    }
    catch (Exception ex)
    {
        Logger.LogWarning(ex, "Failed to switch ACP new-session draft model.");
        await ApplyNewSessionDraftProjectionAsync(
            await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false)).ConfigureAwait(false);
    }
    finally
    {
        _newSessionDraftGate.Release();
    }
}
```

In `ApplyNewSessionDraftProjectionAsync(...)`, after mode option projection, add:

```csharp
if (!_sessionOptionsPresenter.OptionCollectionMatches(_newSessionDraftModelOptions, projection.ModelOptions))
{
    _newSessionDraftModelOptions.Clear();
    foreach (var model in projection.ModelOptions)
    {
        _newSessionDraftModelOptions.Add(model);
    }
}

_newSessionDraftModelConfigId = projection.ModelConfigId;
SetSelectedNewSessionDraftModelWithoutDispatch(
    _sessionOptionsPresenter.ResolveSelectedModelOption(_newSessionDraftModelOptions, projection.SelectedModelValue));
OnPropertyChanged(nameof(NewSessionDraftModelOptions));
OnPropertyChanged(nameof(HasNewSessionDraftModelSelector));
```

Add helpers:

```csharp
private void SetSelectedNewSessionDraftModelWithoutDispatch(OptionValueViewModel? model)
{
    _suppressNewSessionDraftModelSelectionDispatch = true;
    try
    {
        SelectedNewSessionDraftModelOption = model;
    }
    finally
    {
        _suppressNewSessionDraftModelSelectionDispatch = false;
    }
}

private static string? ResolveModelConfigId(IReadOnlyList<ConversationConfigOptionSnapshot> configOptions)
    => configOptions.FirstOrDefault(option =>
        string.Equals(option.Category, "model", StringComparison.OrdinalIgnoreCase))?.Id;

private static string? ResolveSelectedModelValue(IReadOnlyList<ConversationConfigOptionSnapshot> configOptions)
    => configOptions.FirstOrDefault(option =>
        string.Equals(option.Category, "model", StringComparison.OrdinalIgnoreCase))?.SelectedValue;
```

- [ ] **Step 5: Run tests and verify they pass**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~SelectedNewSessionDraftModel_WhenDraftUsesModelConfigOption_UpdatesRemoteDraftSession|FullyQualifiedName~SelectedNewSessionDraftMode_WhenDraftUsesConfigOptions_UpdatesRemoteDraftSession"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.NewSessionDraft.cs tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs
git commit -m "feat(chat): support draft ACP model selection"
```

---

### Task 4: Expose Model Selector In StartViewModel

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Start/StartViewModel.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Start/StartViewModelTests.cs`

**Interfaces:**
- Consumes: `ChatViewModel.NewSessionDraftModelOptions`, `SelectedNewSessionDraftModelOption`, `HasNewSessionDraftModelSelector`.
- Produces:
  - `StartViewModel.StartModelSelectorProjection`
  - `StartViewModel.StartModelSelectorItems`
  - `StartViewModel.SelectedStartModelSelectorItem`
  - generated `SelectStartModelDisplayCommand`

- [ ] **Step 1: Write failing StartViewModel test**

Update `ComposerSelectorSlots_ExposeThreeVisibleStartSelectors` to expect model only when the draft has a model config. Add a new focused test:

```csharp
[Fact]
public async Task ComposerSelectorSlots_WhenDraftHasModelConfig_ExposeModelSelector()
{
    await using var fixture = CreateViewModel();
    var startViewModel = CreateStartViewModel(fixture);

    await fixture.DispatchConnectionAsync(new SetNewSessionDraftAction(new NewSessionDraftState(
        ProfileId: "profile-1",
        Cwd: @"C:\Repo\App",
        RemoteSessionId: "remote-draft",
        ConnectionInstanceId: "conn-1",
        Phase: NewSessionDraftPhase.Ready,
        Version: 1,
        AvailableModes: ImmutableList<ConversationModeOptionSnapshot>.Empty,
        SelectedModeId: null,
        ConfigOptions: CreateModelConfigSnapshots("claude-sonnet").ToImmutableList(),
        ShowConfigOptionsPanel: true,
        AvailableCommands: ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
        SessionInfo: null)));
    await fixture.ApplyNewSessionDraftProjectionAsync();

    var slots = startViewModel.ComposerSelectorSlots;

    Assert.True(slots.Model.IsVisible);
    Assert.True(slots.Model.IsEnabled);
    Assert.Same(startViewModel.SelectStartModelDisplayCommand, slots.Model.SelectionCommand);
    Assert.Equal("claude-sonnet", slots.Model.SelectedItem?.SemanticValue);
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ComposerSelectorSlots_WhenDraftHasModelConfig_ExposeModelSelector"
```

Expected: failure because StartViewModel has no model slot.

- [ ] **Step 3: Implement StartViewModel model selector**

In `StartViewModel.cs`, add field:

```csharp
private readonly ModelSelectorPolicy _modelSelectorPolicy = new();
```

Add properties:

```csharp
public IReadOnlyList<OptionValueViewModel> StartModelOptions => Chat.NewSessionDraftModelOptions;

public OptionValueViewModel? SelectedStartModel
{
    get => Chat.SelectedNewSessionDraftModelOption;
    set => Chat.SelectedNewSessionDraftModelOption = value;
}

public SelectorProjectionResult StartModelSelectorProjection => ResolveStartModelSelectorProjection();

public IReadOnlyList<ComposerSelectorItemViewModel> StartModelSelectorItems
    => StartModelSelectorProjection.DisplayItems;

public ComposerSelectorItemViewModel? SelectedStartModelSelectorItem
    => StartModelSelectorProjection.SelectedDisplayItem;
```

Update `ComposerSelectorSlots`:

```csharp
Model: Chat.HasNewSessionDraftModelSelector
    ? new ComposerSelectorSlotPresentation(
        IsVisible: true,
        IsEnabled: IsInputEnabled && StartModelSelectorProjection.IsEnabled,
        Items: StartModelSelectorItems,
        SelectedItem: SelectedStartModelSelectorItem,
        SelectionCommand: SelectStartModelDisplayCommand)
    : ComposerSelectorSlotPresentation.Hidden()
```

Add command target:

```csharp
private void SelectStartModelDisplay(ComposerSelectorItemViewModel? item)
{
    if (!CanCommitSelectorItem(item, ComposerSelectorKind.Model, StartModelSelectorItems))
    {
        return;
    }

    var model = StartModelOptions.FirstOrDefault(candidate =>
        string.Equals(candidate.Value, item!.SemanticValue, StringComparison.Ordinal));
    if (model is not null)
    {
        SelectedStartModel = model;
    }
}
```

Add projection resolver:

```csharp
private SelectorProjectionResult ResolveStartModelSelectorProjection()
{
    if (!Chat.HasNewSessionDraftModelSelector)
    {
        return SelectorProjectionResult.Empty(ComposerSelectorKind.Model);
    }

    var identity = BuildStartModelIdentity();
    var showRemoteDirectoryPrompt = IsExpectedRemoteDirectorySelectionState();
    var hasDraftError = Chat.HasNewSessionDraftError && !showRemoteDirectoryPrompt;
    var hasConnectionError = Chat.HasConnectionError && !showRemoteDirectoryPrompt;
    var hasModelError = hasDraftError || hasConnectionError;
    var isConnectionInProgress = IsConnectionInProgressForStart();
    IReadOnlyList<OptionValueViewModel> modelOptions = showRemoteDirectoryPrompt
        ? Array.Empty<OptionValueViewModel>()
        : StartModelOptions;

    var policy = _modelSelectorPolicy.Project(new ModelSelectorPolicyInput(
        identity,
        identity,
        modelOptions,
        showRemoteDirectoryPrompt ? null : Chat.SelectedNewSessionDraftModelValue,
        !showRemoteDirectoryPrompt && Chat.IsNewSessionDraftReady,
        !showRemoteDirectoryPrompt && (isConnectionInProgress || _isNewSessionDraftRefreshPending || Chat.IsNewSessionDraftLoading),
        hasModelError,
        ResolveModelSelectorPlaceholderLabels()));

    return _selectorProjectionPresenter.Present(new SelectorProjectionInput(
        ComposerSelectorKind.Model,
        policy.RealItems,
        policy.SelectedSemanticValue,
        policy.Placeholder,
        policy.ReplaceSelectionWithPlaceholder,
        policy.DisableRealItems,
        policy.SelectorEnabled && IsInputEnabled));
}

private string BuildStartModelIdentity()
    => string.Join(
        "|",
        Chat.SelectedAcpProfile?.Id ?? string.Empty,
        Chat.ConnectionInstanceId ?? string.Empty,
        ResolvePreviewCwd() ?? string.Empty,
        StartModelOptions.Count.ToString(CultureInfo.InvariantCulture));

private ModelSelectorPlaceholderLabels ResolveModelSelectorPlaceholderLabels()
    => new(
        Unresolved: Localize("Selector_Model_Unresolved", "模型尚未就绪"),
        Loading: Localize("Selector_Model_Loading", "正在加载模型..."),
        Error: Localize("Selector_Model_Error", "模型不可用"));
```

Update `CanStartSessionAndSend()`:

```csharp
&& !StartModelSelectorProjection.IsSubmitBlocked
```

Update `RefreshAllSelectorProjections()`:

```csharp
OnPropertyChanged(nameof(StartModelSelectorProjection));
OnPropertyChanged(nameof(StartModelSelectorItems));
OnPropertyChanged(nameof(SelectedStartModelSelectorItem));
```

Subscribe to and handle model collection changes:

```csharp
((INotifyCollectionChanged)Chat.NewSessionDraftModelOptions).CollectionChanged += OnStartModelOptionsChanged;

private void OnStartModelOptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
{
    RefreshAllSelectorProjections();
    StartSessionAndSendCommand.NotifyCanExecuteChanged();
    OnPropertyChanged(nameof(CanStartSessionAndSendUi));
}
```

In `OnChatPropertyChanged(...)`, add cases for:

```csharp
nameof(ChatViewModel.SelectedNewSessionDraftModelOption)
nameof(ChatViewModel.SelectedNewSessionDraftModelValue)
nameof(ChatViewModel.NewSessionDraftModelOptions)
nameof(ChatViewModel.HasNewSessionDraftModelSelector)
```

Each case should refresh selector projections and command enabled state, mirroring the existing mode handling.

- [ ] **Step 4: Run tests and verify they pass**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ComposerSelectorSlots_WhenDraftHasModelConfig_ExposeModelSelector|FullyQualifiedName~ComposerSelectorSlots_ExposeThreeVisibleStartSelectors"
```

Expected: PASS after updating the older "three selectors" assertion to keep model hidden when no model config exists.

- [ ] **Step 5: Commit**

```powershell
git add src/SalmonEgg.Presentation.Core/ViewModels/Start/StartViewModel.cs tests/SalmonEgg.Presentation.Core.Tests/Start/StartViewModelTests.cs
git commit -m "feat(start): expose ACP model selector in draft composer"
```

---

### Task 5: Add Model Selector To ChatInputArea XAML

**Files:**
- Modify: `SalmonEgg/SalmonEgg/Controls/ChatInputArea.xaml`
- Modify: `SalmonEgg/SalmonEgg/Controls/ChatInputArea.xaml.cs`
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Start/StartView.xaml`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Ui/XamlComplianceTests.cs`

**Interfaces:**
- Consumes: `ComposerSelectorSlotsPresentation.Model`.
- Produces: XAML `ComboBox x:Name="ModelSelectorHost"` and `ModelSelectorAutomationId` dependency property.

- [ ] **Step 1: Write failing XAML contract test**

Update `ChatInputArea_ExposesAgentAndProjectSlotsAsCapabilities`:

```csharp
Assert.Contains("x:Name=\"ModelSelectorHost\"", xaml);
Assert.Contains("Visibility=\"{x:Bind SelectorSlots.Model.IsVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml);
Assert.Contains("AutomationProperties.AutomationId=\"{x:Bind ModelSelectorAutomationId, Mode=OneWay}\"", xaml);
Assert.Contains("ItemsSource=\"{x:Bind SelectorSlots.Model.Items, Mode=OneWay}\"", xaml);
Assert.Contains("SelectedItem=\"{x:Bind SelectorSlots.Model.SelectedItem, Mode=OneWay}\"", xaml);
Assert.DoesNotContain("ShowModelSelector", code, StringComparison.Ordinal);
```

Update `ChatInputArea_CodeBehind_TreatsDeferredSelectorsAsOptional`:

```csharp
Assert.DoesNotContain("ModelSelectorHost.XamlRoot", code, StringComparison.Ordinal);
```

Update the mode binding contract test:

```csharp
Assert.Contains("ItemsSource=\"{x:Bind SelectorSlots.Model.Items, Mode=OneWay}\"", chatInputXaml);
Assert.Contains("SelectedItem=\"{x:Bind SelectorSlots.Model.SelectedItem, Mode=OneWay}\"", chatInputXaml);
Assert.Contains("SelectionChanged=\"OnModelSelectorSelectionChanged\"", chatInputXaml);
Assert.Contains("ModelSelectorAutomationId=\"StartView.ModelSelector\"", startViewXaml);
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ChatInputArea_ExposesAgentAndProjectSlotsAsCapabilities|FullyQualifiedName~ChatInputArea_CodeBehind_TreatsDeferredSelectorsAsOptional"
```

Expected: failure because the XAML and code-behind do not expose a model selector.

- [ ] **Step 3: Add XAML slot**

Insert this `ComboBox` after `ModeSelectorHost` and before `ProjectSelectorHost` in `ChatInputArea.xaml`:

```xml
<ComboBox x:Name="ModelSelectorHost"
          x:Uid="ChatModelSelector"
          Visibility="{x:Bind SelectorSlots.Model.IsVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
          AutomationProperties.AutomationId="{x:Bind ModelSelectorAutomationId, Mode=OneWay}"
          MinWidth="{StaticResource ComposerProfileSelectorMinWidth}"
          MaxWidth="{StaticResource ComposerProfileSelectorMaxWidth}"
          FontSize="12"
          Background="Transparent"
          BorderThickness="0"
          HorizontalAlignment="Stretch"
          IsEnabled="{x:Bind SelectorSlots.Model.IsEnabled, Mode=OneWay}"
          ItemsSource="{x:Bind SelectorSlots.Model.Items, Mode=OneWay}"
          ItemContainerStyle="{StaticResource ComposerSelectorComboBoxItemStyle}"
          SelectedItem="{x:Bind SelectorSlots.Model.SelectedItem, Mode=OneWay}"
          SelectionChanged="OnModelSelectorSelectionChanged"
          IsFocusEngagementEnabled="True"
          DropDownOpened="OnSelectorDropDownOpened"
          DropDownClosed="OnSelectorDropDownClosed">
    <ComboBox.ItemTemplate>
        <DataTemplate x:DataType="selectors:ComposerSelectorItemViewModel">
            <TextBlock Text="{x:Bind DisplayName, Mode=OneWay}"
                       AutomationProperties.AutomationId="{x:Bind AutomationId, Mode=OneWay}"
                       TextTrimming="CharacterEllipsis"
                       MaxLines="1"
                       Opacity="{x:Bind IsSelectable, Mode=OneWay, Converter={StaticResource BoolToOpacityConverter}}"/>
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

- [ ] **Step 4: Add code-behind dependency property and generic handler**

In `ChatInputArea.xaml.cs`, add dependency property:

```csharp
public static readonly DependencyProperty ModelSelectorAutomationIdProperty =
    DependencyProperty.Register(
        nameof(ModelSelectorAutomationId),
        typeof(string),
        typeof(ChatInputArea),
        new PropertyMetadata("ChatInputArea.ModelSelector"));
```

Add property:

```csharp
public string ModelSelectorAutomationId
{
    get => (string)GetValue(ModelSelectorAutomationIdProperty);
    set => SetValue(ModelSelectorAutomationIdProperty, value);
}
```

Add model selector to `GetVisibleSelectors()`:

```csharp
GetLoadedSelector(nameof(ModelSelectorHost)),
```

Add handler:

```csharp
private void OnModelSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    ExecuteSelectorCommand(sender, SelectorSlots.Model.SelectionCommand);
}
```

In `StartView.xaml`, add:

```xml
ModelSelectorAutomationId="StartView.ModelSelector"
```

- [ ] **Step 5: Run tests and verify they pass**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ChatInputArea_ExposesAgentAndProjectSlotsAsCapabilities|FullyQualifiedName~ChatInputArea_CodeBehind_TreatsDeferredSelectorsAsOptional|FullyQualifiedName~ChatInputArea_ComposerSelectorSlots"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add SalmonEgg/SalmonEgg/Controls/ChatInputArea.xaml SalmonEgg/SalmonEgg/Controls/ChatInputArea.xaml.cs SalmonEgg/SalmonEgg/Presentation/Views/Start/StartView.xaml tests/SalmonEgg.Presentation.Core.Tests/Ui/XamlComplianceTests.cs
git commit -m "feat(ui): bind ACP model selector slot"
```

---

### Task 6: Add Localized Model Selector Strings

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.resx`
- Modify: `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.en.resx`
- Modify: `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.en-US.resx`
- Modify: `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.zh-Hans.resx`

**Interfaces:**
- Consumes: `ResolveModelSelectorPlaceholderLabels()`.
- Produces resource keys:
  - `Selector_Model_Unresolved`
  - `Selector_Model_Loading`
  - `Selector_Model_Error`

- [ ] **Step 1: Add resource entries**

Add these values to English resource files:

```xml
<data name="Selector_Model_Unresolved" xml:space="preserve">
  <value>Model is not ready</value>
</data>
<data name="Selector_Model_Loading" xml:space="preserve">
  <value>Loading models...</value>
</data>
<data name="Selector_Model_Error" xml:space="preserve">
  <value>Models unavailable</value>
</data>
```

Add these values to Chinese resource files:

```xml
<data name="Selector_Model_Unresolved" xml:space="preserve">
  <value>模型尚未就绪</value>
</data>
<data name="Selector_Model_Loading" xml:space="preserve">
  <value>正在加载模型...</value>
</data>
<data name="Selector_Model_Error" xml:space="preserve">
  <value>模型不可用</value>
</data>
```

- [ ] **Step 2: Run resource/build check**

Run:

```powershell
dotnet build src/SalmonEgg.Presentation.Core/SalmonEgg.Presentation.Core.csproj --no-restore
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```powershell
git add src/SalmonEgg.Presentation.Core/Resources/CoreStrings.resx src/SalmonEgg.Presentation.Core/Resources/CoreStrings.en.resx src/SalmonEgg.Presentation.Core/Resources/CoreStrings.en-US.resx src/SalmonEgg.Presentation.Core/Resources/CoreStrings.zh-Hans.resx
git commit -m "feat(chat): localize model selector state"
```

---

### Task 7: Full Verification And Smoke

**Files:**
- No source edits unless verification exposes a defect.

**Interfaces:**
- Consumes all previous tasks.
- Produces verified evidence that the implementation is not a dirty patch and does not regress mode/project/input behavior.

- [ ] **Step 1: Run focused unit tests**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ChatSessionOptionsPresenterTests|FullyQualifiedName~ModelSelectorPolicyTests|FullyQualifiedName~ChatViewModelTests|FullyQualifiedName~StartViewModelTests|FullyQualifiedName~XamlComplianceTests"
```

Expected: PASS.

- [ ] **Step 2: Run full Presentation.Core test suite**

Run:

```powershell
dotnet test tests/SalmonEgg.Presentation.Core.Tests/SalmonEgg.Presentation.Core.Tests.csproj
```

Expected: PASS.

- [ ] **Step 3: Build Windows target**

Run:

```powershell
dotnet build SalmonEgg/SalmonEgg.csproj -f net10.0-windows10.0.26100.0 --no-restore
```

Expected: PASS.

- [ ] **Step 4: Smoke the real app build**

Run the current build output, not an old install:

```powershell
.tools/run-winui3-msix.ps1 -Configuration Debug
```

Smoke checklist:
- Start screen without a model config shows Agent, Mode, Project; no Model selector.
- Start screen with a test ACP agent returning `category="model"` shows `StartView.ModelSelector`.
- Selecting a model in Start sends exactly one `session/set_config_option` with the draft session id, `configId="model"`, and selected value.
- Chat composer for an active session with `category="model"` shows `ChatInputArea.ModelSelector`.
- Selecting a model in Chat sends exactly one `session/set_config_option`; it does not call `session/set_mode`.
- Typing in the chat input with a Chinese IME still keeps focus in the input box; the new selector does not steal focus.
- Existing mode selector still selects via `session/set_config_option` when mode is config-backed.
- Existing mode selector still falls back to `session/set_mode` only for legacy non-config-backed mode.

- [ ] **Step 5: Inspect implementation for dirty-patch signals**

Run:

```powershell
rg -n "model|ModelSelector|SetSessionConfigOptionAsync|SetSessionModeAsync|FocusManager|DispatcherQueue|Delay|Task.Delay|SelectedItem =" src/SalmonEgg.Presentation.Core SalmonEgg/SalmonEgg tests/SalmonEgg.Presentation.Core.Tests
```

Expected evidence:
- Model matching is centralized in `ChatSessionOptionsPresenter` and draft helper `ResolveModelConfigId`, both using `Category == "model"`.
- No custom ACP method is introduced.
- No code-behind writes selector state except forwarding `SelectionChanged` to `SelectorSlots.Model.SelectionCommand`.
- No `DispatcherQueue`, `Task.Delay`, or focus manipulation is added to make the selector appear or keep focus.
- No tests assert private implementation placement except existing XAML contract checks.

- [ ] **Step 6: Commit final verification fixes if needed**

If verification required a fix:

```powershell
git add <changed-files>
git commit -m "fix(chat): stabilize ACP model selector integration"
```

If verification required no fix, do not create an empty commit.

---

## Self-Review

**Spec coverage:** The plan covers ACP standard semantics, active chat sessions, new-session draft/start composer, XAML binding, localization, tests, build, and GUI smoke. It explicitly hides the selector when there is no `category="model"` config option.

**Placeholder scan:** Every code-writing step contains concrete code or concrete snippets with exact paths, and each verification step has a concrete command plus expected result.

**Type consistency:** Model options are consistently represented as `OptionValueViewModel` in presentation selector layers. Protocol `ConfigOption` remains only at transport/test response boundaries. Selection writes use `SessionSetConfigOptionParams(sessionId, configId, value)` and never introduce a model-specific protocol method.
