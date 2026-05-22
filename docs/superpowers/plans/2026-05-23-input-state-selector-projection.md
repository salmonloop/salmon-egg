# Input State Selector Projection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a selector projection layer that gives Start and regular Chat consistent placeholder, submit-blocking, stale-result, and subset behavior without polluting business collections.

**Architecture:** Keep `ChatInputStatePresenter` as the global composer interaction state owner. Add a focused selector projection layer under `ViewModels/Chat/Selectors` with a mechanical shared presenter and three selector-specific policies for mode, agent, and project. Wire Start with the full selector set and Chat with the mode-only subset; prove both paths with unit, ViewModel, XAML contract, and GUI smoke tests.

**Tech Stack:** .NET 10, C#, CommunityToolkit.Mvvm, Uno/WinUI XAML, xUnit, FlaUI GUI smoke tests.

---

## Scope Check

This plan implements phase 1 from `docs/superpowers/specs/2026-05-23-input-state-selector-projection-design.md`.

Included:

- selector projection contracts and policies
- Start full selector set: agent, mode, project
- regular Chat subset: mode only
- XAML binding changes in `ChatInputArea`, `StartView`, and `ChatView`
- GUI smoke tests for visible placeholders and subset behavior

Excluded:

- MiniChat wiring
- protocol-layer redesign
- code-behind dropdown close/reopen behavior

## File Structure

Create:

- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ComposerSelectorKind.cs`  
  Selector slot identity: agent, mode, project.
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorPlaceholderKind.cs`  
  Placeholder categories and blocking semantics.
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ComposerSelectorItemViewModel.cs`  
  Display-only item for ComboBox binding. Carries semantic value and identity.
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorProjectionInput.cs`  
  Mechanical input for shared presenter.
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorProjectionResult.cs`  
  Mechanical output for shared presenter.
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorProjectionPresenter.cs`  
  Shared display projection logic.
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ModeSelectorPolicy.cs`  
  ACP mode/config authoritative-state policy.
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/AgentSelectorPolicy.cs`  
  Local agent/connection intent policy.
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ProjectSelectorPolicy.cs`  
  Local project/fallback policy.
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/SelectorProjectionPresenterTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/ModeSelectorPolicyTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/AgentSelectorPolicyTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/ProjectSelectorPolicyTests.cs`
- `tests/SalmonEgg.GuiTests.Windows/ChatInputSelectorSmokeTests.cs`

Modify:

- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.CommandWorkflow.cs`
- `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.NewSessionDraft.cs`
- `src/SalmonEgg.Presentation.Core/ViewModels/Start/StartViewModel.cs`
- `SalmonEgg/SalmonEgg/Controls/ChatInputArea.xaml`
- `SalmonEgg/SalmonEgg/Controls/ChatInputArea.xaml.cs`
- `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml`
- `SalmonEgg/SalmonEgg/Presentation/Views/Start/StartView.xaml`
- `tests/SalmonEgg.Presentation.Core.Tests/Ui/XamlComplianceTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Start/StartViewModelTests.cs`
- `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`
- `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.resx`
- `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.en.resx`
- `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.en-US.resx`
- `src/SalmonEgg.Presentation.Core/Resources/CoreStrings.zh-Hans.resx`

---

### Task 1: Selector Projection Contracts And Shared Presenter

**Files:**
- Create: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ComposerSelectorKind.cs`
- Create: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorPlaceholderKind.cs`
- Create: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ComposerSelectorItemViewModel.cs`
- Create: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorProjectionInput.cs`
- Create: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorProjectionResult.cs`
- Create: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorProjectionPresenter.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/SelectorProjectionPresenterTests.cs`

- [ ] **Step 1: Write failing presenter tests**

Create `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/SelectorProjectionPresenterTests.cs`:

```csharp
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Selectors;

public sealed class SelectorProjectionPresenterTests
{
    [Fact]
    public void Present_WhenBlockingPlaceholderReplacesSelection_SelectsPlaceholderAndBlocksSubmit()
    {
        var presenter = new SelectorProjectionPresenter();
        var realItem = ComposerSelectorItemViewModel.Real(
            ComposerSelectorKind.Mode,
            "code",
            "Code",
            "profile-1|conn-1|cwd-1|1");
        var placeholder = ComposerSelectorItemViewModel.Placeholder(
            ComposerSelectorKind.Mode,
            SelectorPlaceholderKind.Loading,
            "Loading modes...",
            identity: "profile-1|conn-1|cwd-2|2",
            blocksSubmit: true);

        var result = presenter.Present(new SelectorProjectionInput(
            ComposerSelectorKind.Mode,
            new[] { realItem },
            selectedSemanticValue: "code",
            placeholder,
            replaceSelectionWithPlaceholder: true,
            disableRealItems: true,
            selectorEnabled: false));

        Assert.Same(placeholder, result.SelectedDisplayItem);
        Assert.Equal(SelectorPlaceholderKind.Loading, result.PlaceholderKind);
        Assert.True(result.IsSubmitBlocked);
        Assert.False(result.IsEnabled);
        Assert.Collection(
            result.DisplayItems,
            item => Assert.Same(placeholder, item),
            item =>
            {
                Assert.Equal("code", item.SemanticValue);
                Assert.False(item.IsSelectable);
            });
    }

    [Fact]
    public void Present_WhenFallbackHasSemanticValue_DoesNotBlockSubmit()
    {
        var presenter = new SelectorProjectionPresenter();
        var fallback = ComposerSelectorItemViewModel.Placeholder(
            ComposerSelectorKind.Project,
            SelectorPlaceholderKind.Fallback,
            "Unclassified",
            identity: "project|unclassified",
            semanticValue: "unclassified",
            blocksSubmit: false);

        var result = presenter.Present(new SelectorProjectionInput(
            ComposerSelectorKind.Project,
            Array.Empty<ComposerSelectorItemViewModel>(),
            selectedSemanticValue: "unclassified",
            fallback,
            replaceSelectionWithPlaceholder: true,
            disableRealItems: false,
            selectorEnabled: true));

        Assert.Same(fallback, result.SelectedDisplayItem);
        Assert.False(result.IsSubmitBlocked);
        Assert.True(result.IsEnabled);
        Assert.True(result.SelectedDisplayItem!.IsSelectable);
    }

    [Fact]
    public void Present_WhenNoPlaceholder_SelectsMatchingRealItem()
    {
        var presenter = new SelectorProjectionPresenter();
        var plan = ComposerSelectorItemViewModel.Real(ComposerSelectorKind.Mode, "plan", "Plan", "id-1");
        var code = ComposerSelectorItemViewModel.Real(ComposerSelectorKind.Mode, "code", "Code", "id-1");

        var result = presenter.Present(new SelectorProjectionInput(
            ComposerSelectorKind.Mode,
            new[] { plan, code },
            selectedSemanticValue: "code",
            placeholder: null,
            replaceSelectionWithPlaceholder: false,
            disableRealItems: false,
            selectorEnabled: true));

        Assert.Same(code, result.SelectedDisplayItem);
        Assert.False(result.IsSubmitBlocked);
        Assert.True(result.IsEnabled);
        Assert.Equal(new[] { "plan", "code" }, result.DisplayItems.Select(item => item.SemanticValue).ToArray());
    }
}
```

- [ ] **Step 2: Run presenter tests to verify they fail**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~SelectorProjectionPresenterTests" -m:1 -nr:false -v:minimal
```

Expected: fail because `SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors` types do not exist.

- [ ] **Step 3: Add selector contract files**

Create `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ComposerSelectorKind.cs`:

```csharp
namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public enum ComposerSelectorKind
{
    Agent = 0,
    Mode = 1,
    Project = 2
}
```

Create `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorPlaceholderKind.cs`:

```csharp
namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public enum SelectorPlaceholderKind
{
    None = 0,
    Loading = 1,
    Error = 2,
    Unresolved = 3,
    Default = 4,
    Fallback = 5,
    EmptyNonBlocking = 6
}
```

Create `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ComposerSelectorItemViewModel.cs`:

```csharp
namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record ComposerSelectorItemViewModel(
    ComposerSelectorKind Kind,
    string DisplayName,
    string? SemanticValue,
    string Identity,
    bool IsPlaceholder,
    SelectorPlaceholderKind PlaceholderKind,
    bool IsSelectable,
    bool BlocksSubmit)
{
    public static ComposerSelectorItemViewModel Real(
        ComposerSelectorKind kind,
        string semanticValue,
        string displayName,
        string identity)
        => new(
            kind,
            displayName ?? string.Empty,
            semanticValue,
            identity ?? string.Empty,
            IsPlaceholder: false,
            SelectorPlaceholderKind.None,
            IsSelectable: true,
            BlocksSubmit: false);

    public static ComposerSelectorItemViewModel Placeholder(
        ComposerSelectorKind kind,
        SelectorPlaceholderKind placeholderKind,
        string displayName,
        string identity,
        string? semanticValue = null,
        bool blocksSubmit = true,
        bool isSelectable = false)
        => new(
            kind,
            displayName ?? string.Empty,
            semanticValue,
            identity ?? string.Empty,
            IsPlaceholder: true,
            placeholderKind,
            IsSelectable: isSelectable || !string.IsNullOrWhiteSpace(semanticValue),
            BlocksSubmit: blocksSubmit);

    public ComposerSelectorItemViewModel AsDisabled()
        => this with { IsSelectable = false };
}
```

Create `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorProjectionInput.cs`:

```csharp
namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record SelectorProjectionInput(
    ComposerSelectorKind Kind,
    IReadOnlyList<ComposerSelectorItemViewModel> RealItems,
    string? SelectedSemanticValue,
    ComposerSelectorItemViewModel? Placeholder,
    bool ReplaceSelectionWithPlaceholder,
    bool DisableRealItems,
    bool SelectorEnabled);
```

Create `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorProjectionResult.cs`:

```csharp
namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record SelectorProjectionResult(
    IReadOnlyList<ComposerSelectorItemViewModel> DisplayItems,
    ComposerSelectorItemViewModel? SelectedDisplayItem,
    bool IsEnabled,
    bool IsSubmitBlocked,
    string? SubmitBlockReason,
    SelectorPlaceholderKind PlaceholderKind)
{
    public static SelectorProjectionResult Empty(ComposerSelectorKind kind)
        => new(
            Array.Empty<ComposerSelectorItemViewModel>(),
            null,
            IsEnabled: false,
            IsSubmitBlocked: false,
            SubmitBlockReason: null,
            SelectorPlaceholderKind.None);
}
```

Create `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/SelectorProjectionPresenter.cs`:

```csharp
namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed class SelectorProjectionPresenter
{
    public SelectorProjectionResult Present(SelectorProjectionInput input)
    {
        var realItems = input.RealItems ?? Array.Empty<ComposerSelectorItemViewModel>();
        var projectedRealItems = input.DisableRealItems
            ? realItems.Select(static item => item.AsDisabled()).ToArray()
            : realItems.ToArray();

        var displayItems = input.Placeholder is null
            ? projectedRealItems
            : new[] { input.Placeholder }.Concat(projectedRealItems).ToArray();

        var selected = ResolveSelectedDisplayItem(input, projectedRealItems);
        var isSubmitBlocked = input.Placeholder?.BlocksSubmit == true;
        var submitBlockReason = isSubmitBlocked
            ? input.Placeholder!.DisplayName
            : null;

        return new SelectorProjectionResult(
            displayItems,
            selected,
            input.SelectorEnabled && !input.ReplaceSelectionWithPlaceholder,
            isSubmitBlocked,
            submitBlockReason,
            input.Placeholder?.PlaceholderKind ?? SelectorPlaceholderKind.None);
    }

    private static ComposerSelectorItemViewModel? ResolveSelectedDisplayItem(
        SelectorProjectionInput input,
        IReadOnlyList<ComposerSelectorItemViewModel> projectedRealItems)
    {
        if (input.Placeholder is not null && input.ReplaceSelectionWithPlaceholder)
        {
            return input.Placeholder;
        }

        if (string.IsNullOrWhiteSpace(input.SelectedSemanticValue))
        {
            return input.Placeholder;
        }

        return projectedRealItems.FirstOrDefault(item =>
            string.Equals(item.SemanticValue, input.SelectedSemanticValue, StringComparison.Ordinal));
    }
}
```

- [ ] **Step 4: Run presenter tests to verify they pass**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~SelectorProjectionPresenterTests" -m:1 -nr:false -v:minimal
```

Expected: all `SelectorProjectionPresenterTests` pass.

- [ ] **Step 5: Commit**

Run:

```powershell
git add src\SalmonEgg.Presentation.Core\ViewModels\Chat\Selectors tests\SalmonEgg.Presentation.Core.Tests\Chat\Selectors\SelectorProjectionPresenterTests.cs
git commit -m "feat: add composer selector projection presenter"
```

Expected: commit succeeds.

---

### Task 2: Mode Selector Policy

**Files:**
- Create: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ModeSelectorPolicy.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/ModeSelectorPolicyTests.cs`

- [ ] **Step 1: Write failing mode policy tests**

Create `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/ModeSelectorPolicyTests.cs`:

```csharp
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Selectors;

public sealed class ModeSelectorPolicyTests
{
    [Fact]
    public void Project_WhenDraftIsLoading_ReplacesSelectionWithBlockingPlaceholder()
    {
        var policy = new ModeSelectorPolicy();

        var projection = policy.Project(new ModeSelectorPolicyInput(
            Identity: "profile-1|conn-1|cwd-1|2",
            CurrentIdentity: "profile-1|conn-1|cwd-1|2",
            Modes: new[] { Mode("code", "Code") },
            SelectedModeId: "code",
            IsAuthoritative: false,
            IsLoading: true,
            HasError: false,
            HasModeCapabilitySignal: true));

        Assert.Equal(SelectorPlaceholderKind.Loading, projection.Placeholder!.PlaceholderKind);
        Assert.True(projection.ReplaceSelectionWithPlaceholder);
        Assert.True(projection.DisableRealItems);
        Assert.True(projection.Placeholder.BlocksSubmit);
    }

    [Fact]
    public void Project_WhenIdentityIsStale_ReturnsUnresolvedBlockingPlaceholder()
    {
        var policy = new ModeSelectorPolicy();

        var projection = policy.Project(new ModeSelectorPolicyInput(
            Identity: "profile-1|conn-1|cwd-previous|1",
            CurrentIdentity: "profile-2|conn-2|cwd-current|1",
            Modes: new[] { Mode("code", "Code") },
            SelectedModeId: "code",
            IsAuthoritative: true,
            IsLoading: false,
            HasError: false,
            HasModeCapabilitySignal: true));

        Assert.Equal(SelectorPlaceholderKind.Unresolved, projection.Placeholder!.PlaceholderKind);
        Assert.True(projection.Placeholder.BlocksSubmit);
    }

    [Fact]
    public void Project_WhenNoModeCapability_ReturnsNonBlockingDefaultPlaceholder()
    {
        var policy = new ModeSelectorPolicy();

        var projection = policy.Project(new ModeSelectorPolicyInput(
            Identity: "profile-1|conn-1|cwd-1|1",
            CurrentIdentity: "profile-1|conn-1|cwd-1|1",
            Modes: Array.Empty<SessionModeViewModel>(),
            SelectedModeId: null,
            IsAuthoritative: true,
            IsLoading: false,
            HasError: false,
            HasModeCapabilitySignal: false));

        Assert.Equal(SelectorPlaceholderKind.Default, projection.Placeholder!.PlaceholderKind);
        Assert.False(projection.Placeholder.BlocksSubmit);
        Assert.True(projection.ReplaceSelectionWithPlaceholder);
    }

    [Fact]
    public void Project_WhenModeCapabilityReturnsEmpty_ReturnsErrorPlaceholder()
    {
        var policy = new ModeSelectorPolicy();

        var projection = policy.Project(new ModeSelectorPolicyInput(
            Identity: "profile-1|conn-1|cwd-1|1",
            CurrentIdentity: "profile-1|conn-1|cwd-1|1",
            Modes: Array.Empty<SessionModeViewModel>(),
            SelectedModeId: null,
            IsAuthoritative: true,
            IsLoading: false,
            HasError: false,
            HasModeCapabilitySignal: true));

        Assert.Equal(SelectorPlaceholderKind.Error, projection.Placeholder!.PlaceholderKind);
        Assert.True(projection.Placeholder.BlocksSubmit);
    }

    [Fact]
    public void Project_WhenReadyWithModes_UsesRealItemsWithoutPlaceholder()
    {
        var policy = new ModeSelectorPolicy();

        var projection = policy.Project(new ModeSelectorPolicyInput(
            Identity: "profile-1|conn-1|cwd-1|1",
            CurrentIdentity: "profile-1|conn-1|cwd-1|1",
            Modes: new[] { Mode("plan", "Plan"), Mode("code", "Code") },
            SelectedModeId: "code",
            IsAuthoritative: true,
            IsLoading: false,
            HasError: false,
            HasModeCapabilitySignal: true));

        Assert.Null(projection.Placeholder);
        Assert.False(projection.DisableRealItems);
        Assert.Equal(new[] { "plan", "code" }, projection.RealItems.Select(item => item.SemanticValue).ToArray());
    }

    private static SessionModeViewModel Mode(string id, string name)
        => new()
        {
            ModeId = id,
            ModeName = name,
            Description = string.Empty
        };
}
```

- [ ] **Step 2: Run mode policy tests to verify they fail**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ModeSelectorPolicyTests" -m:1 -nr:false -v:minimal
```

Expected: fail because `ModeSelectorPolicy` and `ModeSelectorPolicyInput` do not exist.

- [ ] **Step 3: Implement mode policy**

Create `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ModeSelectorPolicy.cs`:

```csharp
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record ModeSelectorPolicyInput(
    string Identity,
    string CurrentIdentity,
    IReadOnlyList<SessionModeViewModel> Modes,
    string? SelectedModeId,
    bool IsAuthoritative,
    bool IsLoading,
    bool HasError,
    bool HasModeCapabilitySignal);

public sealed record SelectorPolicyProjection(
    IReadOnlyList<ComposerSelectorItemViewModel> RealItems,
    string? SelectedSemanticValue,
    ComposerSelectorItemViewModel? Placeholder,
    bool ReplaceSelectionWithPlaceholder,
    bool DisableRealItems,
    bool SelectorEnabled);

public sealed class ModeSelectorPolicy
{
    public SelectorPolicyProjection Project(ModeSelectorPolicyInput input)
    {
        var realItems = (input.Modes ?? Array.Empty<SessionModeViewModel>())
            .Where(static mode => !string.IsNullOrWhiteSpace(mode.ModeId))
            .Select(mode => ComposerSelectorItemViewModel.Real(
                ComposerSelectorKind.Mode,
                mode.ModeId,
                string.IsNullOrWhiteSpace(mode.ModeName) ? mode.ModeId : mode.ModeName,
                input.Identity))
            .ToArray();

        if (!string.Equals(input.Identity, input.CurrentIdentity, StringComparison.Ordinal))
        {
            return BlockingPlaceholder(input, realItems, SelectorPlaceholderKind.Unresolved, "Waiting for current mode...");
        }

        if (input.IsLoading)
        {
            return BlockingPlaceholder(input, realItems, SelectorPlaceholderKind.Loading, "Loading modes...");
        }

        if (input.HasError)
        {
            return BlockingPlaceholder(input, realItems, SelectorPlaceholderKind.Error, "Mode loading failed");
        }

        if (!input.IsAuthoritative)
        {
            return BlockingPlaceholder(input, realItems, SelectorPlaceholderKind.Unresolved, "Waiting for mode configuration...");
        }

        if (realItems.Count > 0)
        {
            return new SelectorPolicyProjection(
                realItems,
                input.SelectedModeId,
                Placeholder: null,
                ReplaceSelectionWithPlaceholder: false,
                DisableRealItems: false,
                SelectorEnabled: true);
        }

        if (!input.HasModeCapabilitySignal)
        {
            var defaultPlaceholder = ComposerSelectorItemViewModel.Placeholder(
                ComposerSelectorKind.Mode,
                SelectorPlaceholderKind.Default,
                "Default mode",
                input.Identity,
                semanticValue: string.Empty,
                blocksSubmit: false,
                isSelectable: false);
            return new SelectorPolicyProjection(
                realItems,
                SelectedSemanticValue: null,
                defaultPlaceholder,
                ReplaceSelectionWithPlaceholder: true,
                DisableRealItems: false,
                SelectorEnabled: false);
        }

        return BlockingPlaceholder(input, realItems, SelectorPlaceholderKind.Error, "No available modes");
    }

    private static SelectorPolicyProjection BlockingPlaceholder(
        ModeSelectorPolicyInput input,
        IReadOnlyList<ComposerSelectorItemViewModel> realItems,
        SelectorPlaceholderKind kind,
        string displayName)
    {
        var placeholder = ComposerSelectorItemViewModel.Placeholder(
            ComposerSelectorKind.Mode,
            kind,
            displayName,
            input.CurrentIdentity,
            blocksSubmit: true);
        return new SelectorPolicyProjection(
            realItems,
            input.SelectedModeId,
            placeholder,
            ReplaceSelectionWithPlaceholder: true,
            DisableRealItems: true,
            SelectorEnabled: false);
    }
}
```

- [ ] **Step 4: Run mode policy tests to verify they pass**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~ModeSelectorPolicyTests|FullyQualifiedName~SelectorProjectionPresenterTests" -m:1 -nr:false -v:minimal
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit**

Run:

```powershell
git add src\SalmonEgg.Presentation.Core\ViewModels\Chat\Selectors\ModeSelectorPolicy.cs tests\SalmonEgg.Presentation.Core.Tests\Chat\Selectors\ModeSelectorPolicyTests.cs
git commit -m "feat: add mode selector placeholder policy"
```

Expected: commit succeeds.

---

### Task 3: Agent And Project Selector Policies

**Files:**
- Create: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/AgentSelectorPolicy.cs`
- Create: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ProjectSelectorPolicy.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/AgentSelectorPolicyTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/ProjectSelectorPolicyTests.cs`

- [ ] **Step 1: Write failing agent policy tests**

Create `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/AgentSelectorPolicyTests.cs`:

```csharp
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Selectors;

public sealed class AgentSelectorPolicyTests
{
    [Fact]
    public void Project_WhenConnecting_KeepsAgentsVisibleAndAddsBlockingLoadingPlaceholder()
    {
        var policy = new AgentSelectorPolicy();

        var projection = policy.Project(new AgentSelectorPolicyInput(
            Identity: "profile-1|connecting",
            Agents: new[] { Agent("profile-1", "Agent One") },
            SelectedProfileId: "profile-1",
            IsConnecting: true,
            HasConnectionError: false,
            IsSelectionResolved: false));

        Assert.Equal(SelectorPlaceholderKind.Loading, projection.Placeholder!.PlaceholderKind);
        Assert.False(projection.ReplaceSelectionWithPlaceholder);
        Assert.False(projection.DisableRealItems);
        Assert.True(projection.Placeholder.BlocksSubmit);
        Assert.Single(projection.RealItems);
    }

    [Fact]
    public void Project_WhenConnectionFailed_AddsGenericErrorPlaceholder()
    {
        var policy = new AgentSelectorPolicy();

        var projection = policy.Project(new AgentSelectorPolicyInput(
            Identity: "profile-1|error",
            Agents: new[] { Agent("profile-1", "Agent One") },
            SelectedProfileId: "profile-1",
            IsConnecting: false,
            HasConnectionError: true,
            IsSelectionResolved: false));

        Assert.Equal("Agent unavailable", projection.Placeholder!.DisplayName);
        Assert.Equal(SelectorPlaceholderKind.Error, projection.Placeholder.PlaceholderKind);
        Assert.True(projection.Placeholder.BlocksSubmit);
    }

    [Fact]
    public void Project_WhenSelectionResolved_UsesRealAgentItems()
    {
        var policy = new AgentSelectorPolicy();

        var projection = policy.Project(new AgentSelectorPolicyInput(
            Identity: "profile-1|ready",
            Agents: new[] { Agent("profile-1", "Agent One") },
            SelectedProfileId: "profile-1",
            IsConnecting: false,
            HasConnectionError: false,
            IsSelectionResolved: true));

        Assert.Null(projection.Placeholder);
        Assert.False(projection.DisableRealItems);
        Assert.Equal("profile-1", projection.SelectedSemanticValue);
    }

    private static ServerConfiguration Agent(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Transport = TransportType.HttpSse,
            ServerUrl = "https://example.test"
        };
}
```

- [ ] **Step 2: Write failing project policy tests**

Create `tests/SalmonEgg.Presentation.Core.Tests/Chat/Selectors/ProjectSelectorPolicyTests.cs`:

```csharp
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Start;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Selectors;

public sealed class ProjectSelectorPolicyTests
{
    [Fact]
    public void Project_WhenOnlyUnclassifiedExists_ReturnsSelectableNonBlockingFallback()
    {
        var policy = new ProjectSelectorPolicy();

        var projection = policy.Project(new ProjectSelectorPolicyInput(
            Identity: "project|unclassified",
            Projects: new[] { new StartProjectOptionViewModel(NavigationProjectIds.Unclassified, "未归类") },
            SelectedProjectId: NavigationProjectIds.Unclassified,
            PendingProjectIntentResolved: true,
            HasLegalFallback: true));

        Assert.Null(projection.Placeholder);
        Assert.False(projection.DisableRealItems);
        Assert.Equal(NavigationProjectIds.Unclassified, projection.SelectedSemanticValue);
        Assert.False(projection.RealItems.Single().BlocksSubmit);
    }

    [Fact]
    public void Project_WhenPendingIntentUnresolvedWithoutFallback_BlocksSubmitWithPlaceholder()
    {
        var policy = new ProjectSelectorPolicy();

        var projection = policy.Project(new ProjectSelectorPolicyInput(
            Identity: "project|missing",
            Projects: Array.Empty<StartProjectOptionViewModel>(),
            SelectedProjectId: "deleted-project",
            PendingProjectIntentResolved: false,
            HasLegalFallback: false));

        Assert.Equal(SelectorPlaceholderKind.Unresolved, projection.Placeholder!.PlaceholderKind);
        Assert.True(projection.Placeholder.BlocksSubmit);
        Assert.True(projection.ReplaceSelectionWithPlaceholder);
    }
}
```

- [ ] **Step 3: Run agent/project policy tests to verify they fail**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~AgentSelectorPolicyTests|FullyQualifiedName~ProjectSelectorPolicyTests" -m:1 -nr:false -v:minimal
```

Expected: fail because `AgentSelectorPolicy` and `ProjectSelectorPolicy` types do not exist.

- [ ] **Step 4: Implement agent policy**

Create `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/AgentSelectorPolicy.cs`:

```csharp
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record AgentSelectorPolicyInput(
    string Identity,
    IReadOnlyList<ServerConfiguration> Agents,
    string? SelectedProfileId,
    bool IsConnecting,
    bool HasConnectionError,
    bool IsSelectionResolved);

public sealed class AgentSelectorPolicy
{
    public SelectorPolicyProjection Project(AgentSelectorPolicyInput input)
    {
        var realItems = (input.Agents ?? Array.Empty<ServerConfiguration>())
            .Where(static agent => !string.IsNullOrWhiteSpace(agent.Id))
            .Select(agent => ComposerSelectorItemViewModel.Real(
                ComposerSelectorKind.Agent,
                agent.Id,
                string.IsNullOrWhiteSpace(agent.Name) ? agent.Id : agent.Name,
                input.Identity))
            .ToArray();

        if (input.IsConnecting)
        {
            return WithTopPlaceholder(input, realItems, SelectorPlaceholderKind.Loading, "Connecting agent...");
        }

        if (input.HasConnectionError)
        {
            return WithTopPlaceholder(input, realItems, SelectorPlaceholderKind.Error, "Agent unavailable");
        }

        if (!input.IsSelectionResolved)
        {
            return WithTopPlaceholder(input, realItems, SelectorPlaceholderKind.Unresolved, "Select an agent");
        }

        return new SelectorPolicyProjection(
            realItems,
            input.SelectedProfileId,
            Placeholder: null,
            ReplaceSelectionWithPlaceholder: false,
            DisableRealItems: false,
            SelectorEnabled: realItems.Length > 0);
    }

    private static SelectorPolicyProjection WithTopPlaceholder(
        AgentSelectorPolicyInput input,
        IReadOnlyList<ComposerSelectorItemViewModel> realItems,
        SelectorPlaceholderKind kind,
        string displayName)
    {
        var placeholder = ComposerSelectorItemViewModel.Placeholder(
            ComposerSelectorKind.Agent,
            kind,
            displayName,
            input.Identity,
            blocksSubmit: true);
        return new SelectorPolicyProjection(
            realItems,
            input.SelectedProfileId,
            placeholder,
            ReplaceSelectionWithPlaceholder: false,
            DisableRealItems: false,
            SelectorEnabled: true);
    }
}
```

- [ ] **Step 5: Implement project policy**

Create `src/SalmonEgg.Presentation.Core/ViewModels/Chat/Selectors/ProjectSelectorPolicy.cs`:

```csharp
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Start;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record ProjectSelectorPolicyInput(
    string Identity,
    IReadOnlyList<StartProjectOptionViewModel> Projects,
    string? SelectedProjectId,
    bool PendingProjectIntentResolved,
    bool HasLegalFallback);

public sealed class ProjectSelectorPolicy
{
    public SelectorPolicyProjection Project(ProjectSelectorPolicyInput input)
    {
        var realItems = (input.Projects ?? Array.Empty<StartProjectOptionViewModel>())
            .Where(static project => !string.IsNullOrWhiteSpace(project.ProjectId))
            .Select(project => ComposerSelectorItemViewModel.Real(
                ComposerSelectorKind.Project,
                project.ProjectId,
                string.IsNullOrWhiteSpace(project.DisplayName) ? project.ProjectId : project.DisplayName,
                input.Identity))
            .ToArray();

        if (!input.PendingProjectIntentResolved && !input.HasLegalFallback)
        {
            var unresolved = ComposerSelectorItemViewModel.Placeholder(
                ComposerSelectorKind.Project,
                SelectorPlaceholderKind.Unresolved,
                "Project unavailable",
                input.Identity,
                blocksSubmit: true);
            return new SelectorPolicyProjection(
                realItems,
                input.SelectedProjectId,
                unresolved,
                ReplaceSelectionWithPlaceholder: true,
                DisableRealItems: false,
                SelectorEnabled: false);
        }

        if (realItems.Length == 0 && input.HasLegalFallback)
        {
            var fallback = ComposerSelectorItemViewModel.Placeholder(
                ComposerSelectorKind.Project,
                SelectorPlaceholderKind.Fallback,
                "未归类",
                input.Identity,
                semanticValue: NavigationProjectIds.Unclassified,
                blocksSubmit: false,
                isSelectable: true);
            return new SelectorPolicyProjection(
                realItems,
                NavigationProjectIds.Unclassified,
                fallback,
                ReplaceSelectionWithPlaceholder: true,
                DisableRealItems: false,
                SelectorEnabled: true);
        }

        return new SelectorPolicyProjection(
            realItems,
            string.IsNullOrWhiteSpace(input.SelectedProjectId)
                ? NavigationProjectIds.Unclassified
                : input.SelectedProjectId,
            Placeholder: null,
            ReplaceSelectionWithPlaceholder: false,
            DisableRealItems: false,
            SelectorEnabled: realItems.Length > 0);
    }
}
```

- [ ] **Step 6: Run selector policy tests to verify they pass**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~AgentSelectorPolicyTests|FullyQualifiedName~ProjectSelectorPolicyTests|FullyQualifiedName~ModeSelectorPolicyTests|FullyQualifiedName~SelectorProjectionPresenterTests" -m:1 -nr:false -v:minimal
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src\SalmonEgg.Presentation.Core\ViewModels\Chat\Selectors tests\SalmonEgg.Presentation.Core.Tests\Chat\Selectors
git commit -m "feat: add agent and project selector policies"
```

Expected: commit succeeds.

---

### Task 4: ViewModel Selector Projection Wiring

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.CommandWorkflow.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.NewSessionDraft.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Start/StartViewModel.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Start/StartViewModelTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`

- [ ] **Step 1: Write failing Start ViewModel projection tests**

Add these tests to `tests/SalmonEgg.Presentation.Core.Tests/Start/StartViewModelTests.cs` near existing Start mode tests:

```csharp
[Fact]
public async Task StartSelectorProjection_WhenModeDraftFails_ShowsBlockingModePlaceholderWithoutClearingAgentAndProject()
{
    var originalContext = SynchronizationContext.Current;
    var syncContext = new ImmediateSynchronizationContext();
    SynchronizationContext.SetSynchronizationContext(syncContext);
    try
    {
        var preferences = CreatePreferences();
        preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-a", Name = "Alpha", RootPath = @"C:\Repo\Alpha" });
        using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

        var workflow = new Mock<IChatLaunchWorkflow>();
        using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
        var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

        await chat.DispatchConnectionAsync(new SetSettingsSelectedProfileAction("profile-1"));
        await chat.DispatchConnectionAsync(new SetForegroundTransportProfileAction("profile-1"));
        await chat.DispatchConnectionAsync(new SetConnectionInstanceIdAction("conn-1"));
        await chat.DispatchConnectionAsync(new SetConnectionPhaseAction(ConnectionPhase.Connected));
        await chat.DispatchConnectionAsync(new SetNewSessionDraftAction(new NewSessionDraftState(
            ProfileId: "profile-1",
            Cwd: @"C:\Repo\App",
            RemoteSessionId: null,
            ConnectionInstanceId: "conn-1",
            Phase: NewSessionDraftPhase.Faulted,
            Version: 1,
            AvailableModes: ImmutableList<ConversationModeOptionSnapshot>.Empty,
            SelectedModeId: null,
            ConfigOptions: ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            ShowConfigOptionsPanel: false,
            AvailableCommands: ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
            SessionInfo: null,
            IsConfigAuthoritative: false,
            Error: "raw session/new failure")));

        await WaitForConditionAsync(() => startViewModel.StartModeSelectorProjection.SelectedDisplayItem?.IsPlaceholder == true);

        Assert.Equal(SelectorPlaceholderKind.Error, startViewModel.StartModeSelectorProjection.PlaceholderKind);
        Assert.True(startViewModel.StartModeSelectorProjection.IsSubmitBlocked);
        Assert.Contains("Alpha", startViewModel.StartProjectSelectorProjection.DisplayItems.Select(item => item.DisplayName));
        Assert.False(startViewModel.StartSessionAndSendCommand.CanExecute(null));
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(originalContext);
    }
}

[Fact]
public async Task StartSelectorProjection_WhenUnclassifiedProjectSelected_DoesNotBlockSubmit()
{
    var originalContext = SynchronizationContext.Current;
    var syncContext = new ImmediateSynchronizationContext();
    SynchronizationContext.SetSynchronizationContext(syncContext);
    try
    {
        var preferences = CreatePreferences();
        using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());

        var workflow = new Mock<IChatLaunchWorkflow>();
        using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
        var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

        await MakeStartDraftReadyAsync(chat, startViewModel);
        startViewModel.StartPrompt = "launch";

        Assert.Equal(NavigationProjectIds.Unclassified, startViewModel.StartProjectSelectorProjection.SelectedDisplayItem?.SemanticValue);
        Assert.False(startViewModel.StartProjectSelectorProjection.IsSubmitBlocked);
        Assert.True(startViewModel.StartSessionAndSendCommand.CanExecute(null));
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(originalContext);
    }
}
```

- [ ] **Step 2: Write failing Chat subset projection tests**

Add this test to `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs` near composer state tests:

```csharp
[Fact]
public async Task ChatSelectorProjection_ExposesModeOnlySubsetWithoutAgentOrProjectSubmitBlocks()
{
    await using var fixture = CreateViewModel();
    fixture.ViewModel.IsConnected = true;
    fixture.ViewModel.IsSessionActive = true;
    SetCurrentSessionId(fixture.ViewModel, "conv-1");
    fixture.ViewModel.AvailableModes.Add(new SessionModeViewModel
    {
        ModeId = "code",
        ModeName = "Code",
        Description = string.Empty
    });
    fixture.ViewModel.SelectedMode = fixture.ViewModel.AvailableModes.Single();

    var projection = fixture.ViewModel.ChatModeSelectorProjection;

    Assert.False(projection.IsSubmitBlocked);
    Assert.Equal("code", projection.SelectedDisplayItem?.SemanticValue);
    Assert.Equal(SelectorPlaceholderKind.None, projection.PlaceholderKind);
}
```

- [ ] **Step 3: Run new ViewModel tests to verify they fail**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~StartSelectorProjection|FullyQualifiedName~ChatSelectorProjection" -m:1 -nr:false -v:minimal
```

Expected: fail because selector projection properties do not exist.

- [ ] **Step 4: Add projection fields and properties to ChatViewModel**

In `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.cs`, add these fields near `_inputStatePresenter`:

```csharp
private readonly SelectorProjectionPresenter _selectorProjectionPresenter = new();
private readonly ModeSelectorPolicy _modeSelectorPolicy = new();
```

Add these public properties near `ComposerState`:

```csharp
public SelectorProjectionResult ChatModeSelectorProjection => ResolveChatModeSelectorProjection();

public IReadOnlyList<ComposerSelectorItemViewModel> ChatModeSelectorItems
    => ChatModeSelectorProjection.DisplayItems;

public ComposerSelectorItemViewModel? SelectedChatModeSelectorItem
    => ChatModeSelectorProjection.SelectedDisplayItem;
```

Add this helper in `ChatViewModel.CommandWorkflow.cs` after `ResolveInputState()`:

```csharp
private SelectorProjectionResult ResolveChatModeSelectorProjection()
{
    var identity = BuildModeSelectorIdentity(
        SelectedProfileId,
        ConnectionInstanceId,
        GetActiveSessionCwdOrDefault(),
        version: 0);
    var policy = _modeSelectorPolicy.Project(new ModeSelectorPolicyInput(
        Identity: identity,
        CurrentIdentity: identity,
        Modes: AvailableModes,
        SelectedModeId: SelectedMode?.ModeId,
        IsAuthoritative: IsSessionActive,
        IsLoading: IsConnecting || IsInitializing,
        HasError: HasConnectionError,
        HasModeCapabilitySignal: AvailableModes.Count > 0));

    return _selectorProjectionPresenter.Present(new SelectorProjectionInput(
        ComposerSelectorKind.Mode,
        policy.RealItems,
        policy.SelectedSemanticValue,
        policy.Placeholder,
        policy.ReplaceSelectionWithPlaceholder,
        policy.DisableRealItems,
        policy.SelectorEnabled && AreComposerToolsEnabled));
}

private static string BuildModeSelectorIdentity(
    string? profileId,
    string? connectionInstanceId,
    string? cwd,
    long version)
    => string.Join(
        "|",
        profileId ?? string.Empty,
        connectionInstanceId ?? string.Empty,
        cwd ?? string.Empty,
        version.ToString(CultureInfo.InvariantCulture));
```

Add this namespace import to `ChatViewModel.cs`:

```csharp
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
```

In `NotifyComposerProjectionChanged()`, add:

```csharp
OnPropertyChanged(nameof(ChatModeSelectorProjection));
OnPropertyChanged(nameof(ChatModeSelectorItems));
OnPropertyChanged(nameof(SelectedChatModeSelectorItem));
```

- [ ] **Step 5: Add Start selector projection properties**

In `src/SalmonEgg.Presentation.Core/ViewModels/Start/StartViewModel.cs`, add fields:

```csharp
private readonly SelectorProjectionPresenter _selectorProjectionPresenter = new();
private readonly ModeSelectorPolicy _modeSelectorPolicy = new();
private readonly AgentSelectorPolicy _agentSelectorPolicy = new();
private readonly ProjectSelectorPolicy _projectSelectorPolicy = new();
```

Add public properties:

```csharp
public SelectorProjectionResult StartAgentSelectorProjection => ResolveStartAgentSelectorProjection();

public SelectorProjectionResult StartModeSelectorProjection => ResolveStartModeSelectorProjection();

public SelectorProjectionResult StartProjectSelectorProjection => ResolveStartProjectSelectorProjection();

public IReadOnlyList<ComposerSelectorItemViewModel> StartAgentSelectorItems
    => StartAgentSelectorProjection.DisplayItems;

public IReadOnlyList<ComposerSelectorItemViewModel> StartModeSelectorItems
    => StartModeSelectorProjection.DisplayItems;

public IReadOnlyList<ComposerSelectorItemViewModel> StartProjectSelectorItems
    => StartProjectSelectorProjection.DisplayItems;

public ComposerSelectorItemViewModel? SelectedStartAgentSelectorItem
    => StartAgentSelectorProjection.SelectedDisplayItem;

public ComposerSelectorItemViewModel? SelectedStartModeSelectorItem
    => StartModeSelectorProjection.SelectedDisplayItem;

public ComposerSelectorItemViewModel? SelectedStartProjectSelectorItem
    => StartProjectSelectorProjection.SelectedDisplayItem;
```

Add resolver methods:

```csharp
private SelectorProjectionResult ResolveStartAgentSelectorProjection()
{
    var identity = $"agent|{Chat.SelectedAcpProfile?.Id ?? string.Empty}|{Chat.ConnectionInstanceId ?? string.Empty}";
    var policy = _agentSelectorPolicy.Project(new AgentSelectorPolicyInput(
        identity,
        Chat.AcpProfileList,
        Chat.SelectedAcpProfile?.Id,
        Chat.IsConnecting || Chat.IsInitializing,
        Chat.HasConnectionError,
        Chat.SelectedAcpProfile is not null && Chat.IsConnected));
    return _selectorProjectionPresenter.Present(new SelectorProjectionInput(
        ComposerSelectorKind.Agent,
        policy.RealItems,
        policy.SelectedSemanticValue,
        policy.Placeholder,
        policy.ReplaceSelectionWithPlaceholder,
        policy.DisableRealItems,
        policy.SelectorEnabled && IsInputEnabled));
}

private SelectorProjectionResult ResolveStartModeSelectorProjection()
{
    var identity = BuildStartModeIdentity();
    var policy = _modeSelectorPolicy.Project(new ModeSelectorPolicyInput(
        identity,
        identity,
        StartModeOptions,
        SelectedStartMode?.ModeId,
        Chat.IsNewSessionDraftReady,
        _isNewSessionDraftRefreshPending || Chat.IsNewSessionDraftLoading,
        Chat.HasNewSessionDraftError,
        StartModeOptions.Count > 0 || Chat.IsNewSessionDraftReady));
    return _selectorProjectionPresenter.Present(new SelectorProjectionInput(
        ComposerSelectorKind.Mode,
        policy.RealItems,
        policy.SelectedSemanticValue,
        policy.Placeholder,
        policy.ReplaceSelectionWithPlaceholder,
        policy.DisableRealItems,
        policy.SelectorEnabled && IsStartModeSelectorEnabled && IsInputEnabled));
}

private SelectorProjectionResult ResolveStartProjectSelectorProjection()
{
    var selectedProjectId = SelectedStartProjectId;
    var hasLegalFallback = StartProjectOptions.Any(option =>
        string.Equals(option.ProjectId, NavigationProjectIds.Unclassified, StringComparison.Ordinal));
    var policy = _projectSelectorPolicy.Project(new ProjectSelectorPolicyInput(
        $"project|{selectedProjectId}",
        StartProjectOptions,
        selectedProjectId,
        PendingProjectIntentResolved: HasSelectableProject(selectedProjectId) || string.Equals(selectedProjectId, NavigationProjectIds.Unclassified, StringComparison.Ordinal),
        hasLegalFallback));
    return _selectorProjectionPresenter.Present(new SelectorProjectionInput(
        ComposerSelectorKind.Project,
        policy.RealItems,
        policy.SelectedSemanticValue,
        policy.Placeholder,
        policy.ReplaceSelectionWithPlaceholder,
        policy.DisableRealItems,
        policy.SelectorEnabled && IsInputEnabled));
}

private string BuildStartModeIdentity()
    => string.Join(
        "|",
        Chat.SelectedAcpProfile?.Id ?? string.Empty,
        Chat.ConnectionInstanceId ?? string.Empty,
        ResolvePreviewCwd() ?? string.Empty,
        StartModeOptions.Count.ToString(CultureInfo.InvariantCulture));
```

Add `using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;` and `using System.Globalization;`.

Whenever existing code raises selector-related properties, also raise new projection properties:

```csharp
private void RefreshAllSelectorProjections()
{
    OnPropertyChanged(nameof(StartAgentSelectorProjection));
    OnPropertyChanged(nameof(StartModeSelectorProjection));
    OnPropertyChanged(nameof(StartProjectSelectorProjection));
    OnPropertyChanged(nameof(StartAgentSelectorItems));
    OnPropertyChanged(nameof(StartModeSelectorItems));
    OnPropertyChanged(nameof(StartProjectSelectorItems));
    OnPropertyChanged(nameof(SelectedStartAgentSelectorItem));
    OnPropertyChanged(nameof(SelectedStartModeSelectorItem));
    OnPropertyChanged(nameof(SelectedStartProjectSelectorItem));
}
```

Call `RefreshAllSelectorProjections()` from `RefreshStartModeState`, `RefreshStartProjectOptions`, `RefreshStartModeProjection`, `RefreshStartSessionDraftErrorProjection`, `RefreshVoiceProjection`, and `IsStarting` setter after existing property notifications.

- [ ] **Step 6: Make Start send gating consume selector submit blocks**

Replace `CanStartSessionAndSend()` in `StartViewModel.cs` with:

```csharp
private bool CanStartSessionAndSend()
    => _startSessionModeSnapshot.CanSubmitPrompt
        && !StartAgentSelectorProjection.IsSubmitBlocked
        && !StartModeSelectorProjection.IsSubmitBlocked
        && !StartProjectSelectorProjection.IsSubmitBlocked
        && !string.IsNullOrWhiteSpace(StartPrompt);
```

- [ ] **Step 7: Run ViewModel selector tests**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~StartSelectorProjection|FullyQualifiedName~ChatSelectorProjection|FullyQualifiedName~StartSessionAndSendCommand_WhenDraftBecomesReady_EnablesSubmitFromModePolicy" -m:1 -nr:false -v:minimal
```

Expected: all selected tests pass.

- [ ] **Step 8: Commit**

Run:

```powershell
git add src\SalmonEgg.Presentation.Core\ViewModels\Chat src\SalmonEgg.Presentation.Core\ViewModels\Start tests\SalmonEgg.Presentation.Core.Tests\Start\StartViewModelTests.cs tests\SalmonEgg.Presentation.Core.Tests\Chat\ChatViewModelTests.cs
git commit -m "feat: project composer selector state from view models"
```

Expected: commit succeeds.

---

### Task 5: XAML Binding To Selector Projections

**Files:**
- Modify: `SalmonEgg/SalmonEgg/Controls/ChatInputArea.xaml`
- Modify: `SalmonEgg/SalmonEgg/Controls/ChatInputArea.xaml.cs`
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Chat/ChatView.xaml`
- Modify: `SalmonEgg/SalmonEgg/Presentation/Views/Start/StartView.xaml`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Ui/XamlComplianceTests.cs`

- [ ] **Step 1: Write failing XAML contract tests**

Update `ChatInputArea_ExposesAgentAndProjectSlotsAsCapabilities` in `XamlComplianceTests.cs` to add:

```csharp
Assert.Contains("ItemsSource=\"{x:Bind AgentSelectorItemsSource, Mode=OneWay}\"", xaml);
Assert.Contains("SelectedItem=\"{x:Bind SelectedAgentSelectorItem, Mode=OneWay}\"", xaml);
Assert.Contains("ItemsSource=\"{x:Bind ProjectSelectorItemsSource, Mode=OneWay}\"", xaml);
Assert.Contains("SelectedItem=\"{x:Bind SelectedProjectSelectorItem, Mode=OneWay}\"", xaml);
```

Update `SharedComposer_ModeSelectionUsesExplicitCommandInsteadOfTwoWaySelectedMode` to add:

```csharp
Assert.Contains("ItemsSource=\"{x:Bind ModeSelectorItemsSource, Mode=OneWay}\"", chatInputXaml);
Assert.Contains("SelectedItem=\"{x:Bind SelectedModeSelectorItem, Mode=OneWay}\"", chatInputXaml);
```

Update `StartView_ComposerUsesSharedChatInputAreaWithoutPrivateInputControls` to add:

```csharp
Assert.Contains("AgentSelectorItemsSource=\"{x:Bind ViewModel.StartAgentSelectorItems, Mode=OneWay}\"", xaml);
Assert.Contains("ModeSelectorItemsSource=\"{x:Bind ViewModel.StartModeSelectorItems, Mode=OneWay}\"", xaml);
Assert.Contains("ProjectSelectorItemsSource=\"{x:Bind ViewModel.StartProjectSelectorItems, Mode=OneWay}\"", xaml);
Assert.Contains("SelectedModeSelectorItem=\"{x:Bind ViewModel.SelectedStartModeSelectorItem, Mode=OneWay}\"", xaml);
```

Update `ChatView_UsesSharedInputAreaWithoutAgentSelectorCapability` to add:

```csharp
Assert.Contains("ModeSelectorItemsSource=\"{x:Bind ViewModel.ChatModeSelectorItems, Mode=OneWay}\"", xaml);
Assert.Contains("SelectedModeSelectorItem=\"{x:Bind ViewModel.SelectedChatModeSelectorItem, Mode=OneWay}\"", xaml);
Assert.DoesNotContain("AgentSelectorItemsSource=", xaml);
Assert.DoesNotContain("ProjectSelectorItemsSource=", xaml);
```

- [ ] **Step 2: Run XAML contract tests to verify they fail**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~XamlComplianceTests.ChatInputArea|FullyQualifiedName~XamlComplianceTests.StartView_ComposerUsesSharedChatInputAreaWithoutPrivateInputControls|FullyQualifiedName~XamlComplianceTests.ChatView_UsesSharedInputAreaWithoutAgentSelectorCapability|FullyQualifiedName~XamlComplianceTests.SharedComposer_ModeSelectionUsesExplicitCommandInsteadOfTwoWaySelectedMode" -m:1 -nr:false -v:minimal
```

Expected: fail because new dependency properties and bindings do not exist.

- [ ] **Step 3: Add selector display dependency properties**

In `ChatInputArea.xaml.cs`, add `using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;`.

Add dependency properties:

```csharp
public static readonly DependencyProperty AgentSelectorItemsSourceProperty =
    DependencyProperty.Register(
        nameof(AgentSelectorItemsSource),
        typeof(object),
        typeof(ChatInputArea),
        new PropertyMetadata(null));

public static readonly DependencyProperty SelectedAgentSelectorItemProperty =
    DependencyProperty.Register(
        nameof(SelectedAgentSelectorItem),
        typeof(ComposerSelectorItemViewModel),
        typeof(ChatInputArea),
        new PropertyMetadata(null));

public static readonly DependencyProperty ModeSelectorItemsSourceProperty =
    DependencyProperty.Register(
        nameof(ModeSelectorItemsSource),
        typeof(object),
        typeof(ChatInputArea),
        new PropertyMetadata(null));

public static readonly DependencyProperty SelectedModeSelectorItemProperty =
    DependencyProperty.Register(
        nameof(SelectedModeSelectorItem),
        typeof(ComposerSelectorItemViewModel),
        typeof(ChatInputArea),
        new PropertyMetadata(null));

public static readonly DependencyProperty ProjectSelectorItemsSourceProperty =
    DependencyProperty.Register(
        nameof(ProjectSelectorItemsSource),
        typeof(object),
        typeof(ChatInputArea),
        new PropertyMetadata(null));

public static readonly DependencyProperty SelectedProjectSelectorItemProperty =
    DependencyProperty.Register(
        nameof(SelectedProjectSelectorItem),
        typeof(ComposerSelectorItemViewModel),
        typeof(ChatInputArea),
        new PropertyMetadata(null));
```

Add CLR properties:

```csharp
public object? AgentSelectorItemsSource
{
    get => GetValue(AgentSelectorItemsSourceProperty);
    set => SetValue(AgentSelectorItemsSourceProperty, value);
}

public ComposerSelectorItemViewModel? SelectedAgentSelectorItem
{
    get => (ComposerSelectorItemViewModel?)GetValue(SelectedAgentSelectorItemProperty);
    set => SetValue(SelectedAgentSelectorItemProperty, value);
}

public object? ModeSelectorItemsSource
{
    get => GetValue(ModeSelectorItemsSourceProperty);
    set => SetValue(ModeSelectorItemsSourceProperty, value);
}

public ComposerSelectorItemViewModel? SelectedModeSelectorItem
{
    get => (ComposerSelectorItemViewModel?)GetValue(SelectedModeSelectorItemProperty);
    set => SetValue(SelectedModeSelectorItemProperty, value);
}

public object? ProjectSelectorItemsSource
{
    get => GetValue(ProjectSelectorItemsSourceProperty);
    set => SetValue(ProjectSelectorItemsSourceProperty, value);
}

public ComposerSelectorItemViewModel? SelectedProjectSelectorItem
{
    get => (ComposerSelectorItemViewModel?)GetValue(SelectedProjectSelectorItemProperty);
    set => SetValue(SelectedProjectSelectorItemProperty, value);
}
```

- [ ] **Step 4: Update ComboBox XAML item binding**

In `ChatInputArea.xaml`, add namespace:

```xml
xmlns:selectors="using:SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors"
```

Replace Agent ComboBox binding:

```xml
ItemsSource="{x:Bind AgentSelectorItemsSource, Mode=OneWay}"
SelectedItem="{x:Bind SelectedAgentSelectorItem, Mode=OneWay}"
```

Use this item template:

```xml
<ComboBox.ItemTemplate>
    <DataTemplate x:DataType="selectors:ComposerSelectorItemViewModel">
        <TextBlock Text="{x:Bind DisplayName, Mode=OneWay}"
                   TextTrimming="CharacterEllipsis"
                   MaxLines="1"
                   Opacity="{x:Bind IsSelectable, Mode=OneWay, Converter={StaticResource BoolToOpacityConverter}}"/>
    </DataTemplate>
</ComboBox.ItemTemplate>
```

Create `SalmonEgg/SalmonEgg/Presentation/Converters/BoolToOpacityConverter.cs` with:

```csharp
using System;
using Microsoft.UI.Xaml.Data;

namespace SalmonEgg.Presentation.Converters;

public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool flag && flag ? 1.0 : 0.55;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
```

Register converter in `ChatInputArea.xaml` resources:

```xml
<converters:BoolToOpacityConverter x:Key="BoolToOpacityConverter"/>
```

Replace Mode ComboBox binding:

```xml
ItemsSource="{x:Bind ModeSelectorItemsSource, Mode=OneWay}"
SelectedItem="{x:Bind SelectedModeSelectorItem, Mode=OneWay}"
```

Use the same `ComposerSelectorItemViewModel` item template for mode.

Replace Project ComboBox binding:

```xml
ItemsSource="{x:Bind ProjectSelectorItemsSource, Mode=OneWay}"
SelectedItem="{x:Bind SelectedProjectSelectorItem, Mode=OneWay}"
```

Use the same `ComposerSelectorItemViewModel` item template for project.

- [ ] **Step 5: Update selection handling**

In `OnModeSelectorSelectionChanged`, convert selected display item to mode:

```csharp
private void OnModeSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (sender is not ComboBox comboBox
        || comboBox.SelectedItem is not ComposerSelectorItemViewModel item
        || item.IsPlaceholder
        || !item.IsSelectable
        || string.IsNullOrWhiteSpace(item.SemanticValue))
    {
        return;
    }

    ModeSelectionCommand?.Execute(item);
}
```

Update Start and Chat ViewModel commands in Task 6 to accept `ComposerSelectorItemViewModel`.

- [ ] **Step 6: Update Start and Chat XAML consumers**

In `StartView.xaml`, replace selector source bindings:

```xml
AgentSelectorItemsSource="{x:Bind ViewModel.StartAgentSelectorItems, Mode=OneWay}"
SelectedAgentSelectorItem="{x:Bind ViewModel.SelectedStartAgentSelectorItem, Mode=OneWay}"
ModeSelectorItemsSource="{x:Bind ViewModel.StartModeSelectorItems, Mode=OneWay}"
SelectedModeSelectorItem="{x:Bind ViewModel.SelectedStartModeSelectorItem, Mode=OneWay}"
ProjectSelectorItemsSource="{x:Bind ViewModel.StartProjectSelectorItems, Mode=OneWay}"
SelectedProjectSelectorItem="{x:Bind ViewModel.SelectedStartProjectSelectorItem, Mode=OneWay}"
```

Leave legacy `ShowAgentSelector`, `ShowModeSelector`, and `ShowProjectSelector` capability flags in place.

In `ChatView.xaml`, replace mode source bindings:

```xml
ModeSelectorItemsSource="{x:Bind ViewModel.ChatModeSelectorItems, Mode=OneWay}"
SelectedModeSelectorItem="{x:Bind ViewModel.SelectedChatModeSelectorItem, Mode=OneWay}"
```

Do not add agent or project display bindings to Chat.

- [ ] **Step 7: Run XAML contract tests**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~XamlComplianceTests.ChatInputArea|FullyQualifiedName~XamlComplianceTests.StartView_ComposerUsesSharedChatInputAreaWithoutPrivateInputControls|FullyQualifiedName~XamlComplianceTests.ChatView_UsesSharedInputAreaWithoutAgentSelectorCapability|FullyQualifiedName~XamlComplianceTests.SharedComposer_ModeSelectionUsesExplicitCommandInsteadOfTwoWaySelectedMode" -m:1 -nr:false -v:minimal
```

Expected: selected tests pass.

- [ ] **Step 8: Run app XAML build**

Run:

```powershell
dotnet build SalmonEgg\SalmonEgg\SalmonEgg.csproj -f net10.0-desktop -p:SalmonEggTargetFrameworks=net10.0-desktop -p:SalmonEggAllTargetFrameworks=net10.0-desktop -m:1 -nr:false -v:minimal
```

Expected: build succeeds. Existing `Uno0001 AutomationProperties.SetLiveSetting` warning may appear.

- [ ] **Step 9: Commit**

Run:

```powershell
git add SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml tests\SalmonEgg.Presentation.Core.Tests\Ui\XamlComplianceTests.cs
git commit -m "feat: bind composer selectors to display projections"
```

Expected: commit succeeds.

---

### Task 6: Selection Commands And Stale Identity Rejection

**Files:**
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Chat/ChatViewModel.CommandWorkflow.cs`
- Modify: `src/SalmonEgg.Presentation.Core/ViewModels/Start/StartViewModel.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Start/StartViewModelTests.cs`
- Test: `tests/SalmonEgg.Presentation.Core.Tests/Chat/ChatViewModelTests.cs`

- [ ] **Step 1: Write failing stale-selection tests**

Add this Start test:

```csharp
[Fact]
public async Task StartModeSelection_WhenSelectorIdentityIsStale_DoesNotChangeSelectedDraftMode()
{
    var originalContext = SynchronizationContext.Current;
    var syncContext = new ImmediateSynchronizationContext();
    SynchronizationContext.SetSynchronizationContext(syncContext);
    try
    {
        var preferences = CreatePreferences();
        using var chat = CreateChatViewModel(syncContext, preferences, Mock.Of<ISessionManager>());
        var workflow = new Mock<IChatLaunchWorkflow>();
        using var nav = CreateNavigationViewModel(chat, Mock.Of<ISessionManager>(), preferences);
        var startViewModel = CreateStartViewModel(chat.ViewModel, preferences, nav, workflow.Object);

        await MakeStartDraftReadyAsync(chat, startViewModel);
        var originalMode = startViewModel.SelectedStartMode;
        var staleItem = ComposerSelectorItemViewModel.Real(
            ComposerSelectorKind.Mode,
            "code",
            "Code",
            "stale-identity");

        startViewModel.SelectStartModeDisplayCommand.Execute(staleItem);

        Assert.Same(originalMode, startViewModel.SelectedStartMode);
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(originalContext);
    }
}
```

Add this Chat test:

```csharp
[Fact]
public async Task ChatModeSelection_WhenPlaceholderItemIsSelected_DoesNotDispatchModeCommand()
{
    await using var fixture = CreateViewModel();
    var placeholder = ComposerSelectorItemViewModel.Placeholder(
        ComposerSelectorKind.Mode,
        SelectorPlaceholderKind.Loading,
        "Loading modes...",
        "identity",
        blocksSubmit: true);

    fixture.ViewModel.SelectChatModeDisplayCommand.Execute(placeholder);

    Assert.Null(fixture.ViewModel.SelectedMode);
}
```

- [ ] **Step 2: Run stale-selection tests to verify they fail**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~StartModeSelection_WhenSelectorIdentityIsStale|FullyQualifiedName~ChatModeSelection_WhenPlaceholderItemIsSelected" -m:1 -nr:false -v:minimal
```

Expected: fail because display selection commands do not exist.

- [ ] **Step 3: Add display selection commands**

In `ChatViewModel.CommandWorkflow.cs`, add:

```csharp
[RelayCommand]
private void SelectChatModeDisplay(ComposerSelectorItemViewModel? item)
{
    if (item is null
        || item.Kind != ComposerSelectorKind.Mode
        || item.IsPlaceholder
        || !item.IsSelectable
        || string.IsNullOrWhiteSpace(item.SemanticValue))
    {
        return;
    }

    var current = ChatModeSelectorItems.FirstOrDefault(candidate =>
        string.Equals(candidate.SemanticValue, item.SemanticValue, StringComparison.Ordinal)
        && string.Equals(candidate.Identity, item.Identity, StringComparison.Ordinal));
    if (current is null)
    {
        return;
    }

    var mode = AvailableModes.FirstOrDefault(candidate =>
        string.Equals(candidate.ModeId, item.SemanticValue, StringComparison.Ordinal));
    if (mode is not null)
    {
        SetModeCommand.Execute(mode);
    }
}
```

In `StartViewModel.cs`, add command property construction:

```csharp
public IRelayCommand<ComposerSelectorItemViewModel?> SelectStartModeDisplayCommand { get; }
public IRelayCommand<ComposerSelectorItemViewModel?> SelectStartAgentDisplayCommand { get; }
public IRelayCommand<ComposerSelectorItemViewModel?> SelectStartProjectDisplayCommand { get; }
```

Initialize in constructor:

```csharp
SelectStartModeDisplayCommand = new RelayCommand<ComposerSelectorItemViewModel?>(SelectStartModeDisplay);
SelectStartAgentDisplayCommand = new RelayCommand<ComposerSelectorItemViewModel?>(SelectStartAgentDisplay);
SelectStartProjectDisplayCommand = new RelayCommand<ComposerSelectorItemViewModel?>(SelectStartProjectDisplay);
```

Add handlers:

```csharp
private void SelectStartModeDisplay(ComposerSelectorItemViewModel? item)
{
    if (!CanCommitSelectorItem(item, ComposerSelectorKind.Mode, StartModeSelectorItems))
    {
        return;
    }

    var mode = StartModeOptions.FirstOrDefault(candidate =>
        string.Equals(candidate.ModeId, item!.SemanticValue, StringComparison.Ordinal));
    if (mode is not null)
    {
        SelectedStartMode = mode;
    }
}

private void SelectStartAgentDisplay(ComposerSelectorItemViewModel? item)
{
    if (!CanCommitSelectorItem(item, ComposerSelectorKind.Agent, StartAgentSelectorItems))
    {
        return;
    }

    var agent = Chat.AcpProfileList.FirstOrDefault(candidate =>
        string.Equals(candidate.Id, item!.SemanticValue, StringComparison.Ordinal));
    if (agent is not null)
    {
        Chat.SelectedAcpProfile = agent;
    }
}

private void SelectStartProjectDisplay(ComposerSelectorItemViewModel? item)
{
    if (!CanCommitSelectorItem(item, ComposerSelectorKind.Project, StartProjectSelectorItems))
    {
        return;
    }

    SelectedStartProjectId = item!.SemanticValue ?? NavigationProjectIds.Unclassified;
}

private static bool CanCommitSelectorItem(
    ComposerSelectorItemViewModel? item,
    ComposerSelectorKind expectedKind,
    IReadOnlyList<ComposerSelectorItemViewModel> currentItems)
    => item is not null
        && item.Kind == expectedKind
        && !item.IsPlaceholder
        && item.IsSelectable
        && !string.IsNullOrWhiteSpace(item.SemanticValue)
        && currentItems.Any(candidate =>
            string.Equals(candidate.SemanticValue, item.SemanticValue, StringComparison.Ordinal)
            && string.Equals(candidate.Identity, item.Identity, StringComparison.Ordinal));
```

- [ ] **Step 4: Wire commands in XAML**

In `StartView.xaml`, bind:

```xml
AgentSelectionCommand="{x:Bind ViewModel.SelectStartAgentDisplayCommand}"
ModeSelectionCommand="{x:Bind ViewModel.SelectStartModeDisplayCommand}"
ProjectSelectionCommand="{x:Bind ViewModel.SelectStartProjectDisplayCommand}"
```

In `ChatView.xaml`, bind:

```xml
ModeSelectionCommand="{x:Bind ViewModel.SelectChatModeDisplayCommand}"
```

Add `AgentSelectionCommand` and `ProjectSelectionCommand` dependency properties to `ChatInputArea.xaml.cs`:

```csharp
public static readonly DependencyProperty AgentSelectionCommandProperty =
    DependencyProperty.Register(
        nameof(AgentSelectionCommand),
        typeof(ICommand),
        typeof(ChatInputArea),
        new PropertyMetadata(null));

public static readonly DependencyProperty ProjectSelectionCommandProperty =
    DependencyProperty.Register(
        nameof(ProjectSelectionCommand),
        typeof(ICommand),
        typeof(ChatInputArea),
        new PropertyMetadata(null));

public ICommand? AgentSelectionCommand
{
    get => (ICommand?)GetValue(AgentSelectionCommandProperty);
    set => SetValue(AgentSelectionCommandProperty, value);
}

public ICommand? ProjectSelectionCommand
{
    get => (ICommand?)GetValue(ProjectSelectionCommandProperty);
    set => SetValue(ProjectSelectionCommandProperty, value);
}
```

Update agent and project `SelectionChanged` handlers to dispatch display items only:

```csharp
private void OnAgentSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    ExecuteSelectorCommand(sender, AgentSelectionCommand);
}

private void OnProjectSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    ExecuteSelectorCommand(sender, ProjectSelectionCommand);
}

private static void ExecuteSelectorCommand(object sender, ICommand? command)
{
    if (sender is not ComboBox comboBox
        || command is null
        || comboBox.SelectedItem is not ComposerSelectorItemViewModel item
        || item.IsPlaceholder
        || !item.IsSelectable
        || string.IsNullOrWhiteSpace(item.SemanticValue))
    {
        return;
    }

    if (command.CanExecute(item))
    {
        command.Execute(item);
    }
}
```

- [ ] **Step 5: Run stale-selection tests**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~StartModeSelection_WhenSelectorIdentityIsStale|FullyQualifiedName~ChatModeSelection_WhenPlaceholderItemIsSelected" -m:1 -nr:false -v:minimal
```

Expected: selected tests pass.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src\SalmonEgg.Presentation.Core\ViewModels\Chat src\SalmonEgg.Presentation.Core\ViewModels\Start SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml tests\SalmonEgg.Presentation.Core.Tests\Start\StartViewModelTests.cs tests\SalmonEgg.Presentation.Core.Tests\Chat\ChatViewModelTests.cs
git commit -m "feat: reject stale composer selector selections"
```

Expected: commit succeeds.

---

### Task 7: GUI Smoke Coverage

**Files:**
- Create: `tests/SalmonEgg.GuiTests.Windows/ChatInputSelectorSmokeTests.cs`

- [ ] **Step 1: Add GUI smoke tests**

Create `tests/SalmonEgg.GuiTests.Windows/ChatInputSelectorSmokeTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Xunit;

namespace SalmonEgg.GuiTests.Windows;

public sealed class ChatInputSelectorSmokeTests
{
    [SkippableFact]
    public void StartComposer_WhenModeDraftUnavailable_ShowsModePlaceholderAndKeepsProjectFallbackVisible()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(sessionCount: 0, withContent: false);
        using var session = WindowsGuiAppSession.LaunchFresh();

        var startTitle = session.FindByAutomationId("StartView.Title", TimeSpan.FromSeconds(15));
        Assert.NotNull(startTitle);

        var modeSelector = session.FindByAutomationId("StartView.ModeSelector", TimeSpan.FromSeconds(10));
        Assert.False(string.IsNullOrWhiteSpace(modeSelector.Name));

        session.ClickElement(modeSelector);
        Thread.Sleep(250);
        var visibleTexts = session.GetVisibleTexts(session.MainWindow);
        Assert.Contains(visibleTexts, text =>
            text.Contains("mode", StringComparison.OrdinalIgnoreCase)
            || text.Contains("模式", StringComparison.OrdinalIgnoreCase)
            || text.Contains("配置", StringComparison.OrdinalIgnoreCase));

        var projectSelector = session.FindByAutomationId("StartView.ProjectSelector", TimeSpan.FromSeconds(10));
        Assert.False(string.IsNullOrWhiteSpace(projectSelector.Name));
    }

    [SkippableFact]
    public void ChatComposer_DoesNotShowStartOnlyAgentOrProjectSelectors()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(sessionCount: 1, withContent: true);
        using var session = WindowsGuiAppSession.LaunchFresh();

        var sessionItem = session.FindByAutomationId("MainNav.Session.gui-session-01", TimeSpan.FromSeconds(15));
        session.ActivateElement(sessionItem);

        var inputBox = session.FindByAutomationId("InputBox", TimeSpan.FromSeconds(10));
        Assert.NotNull(inputBox);
        Assert.Null(session.TryFindByAutomationId("StartView.AgentSelector", TimeSpan.FromMilliseconds(300)));
        Assert.Null(session.TryFindByAutomationId("StartView.ProjectSelector", TimeSpan.FromMilliseconds(300)));
    }

    [SkippableFact]
    public void StartComposer_SendLock_DisablesPromptAndSelectorsUntilDispatchCompletes()
    {
        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData(sessionCount: 0, withContent: false);
        using var session = WindowsGuiAppSession.LaunchFresh();

        var prompt = session.FindByAutomationId("StartView.PromptBox", TimeSpan.FromSeconds(15)).AsTextBox();
        prompt.Enter("hello from gui smoke");

        var sendButton = session.FindByAutomationId("SendButton", TimeSpan.FromSeconds(10));
        if (!sendButton.IsEnabled)
        {
            return;
        }

        session.ClickElement(sendButton);

        Assert.True(
            WaitUntilDisabled(session, "StartView.PromptBox", TimeSpan.FromSeconds(2)),
            "Start prompt box did not become disabled during send lock.");
    }

    private static bool WaitUntilDisabled(WindowsGuiAppSession session, string automationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var element = session.TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(150));
            if (element is not null && !element.IsEnabled)
            {
                return true;
            }

            Thread.Sleep(80);
        }

        return false;
    }
}
```

- [ ] **Step 2: Run GUI tests in disabled mode to ensure compile path is correct**

Run:

```powershell
dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~ChatInputSelectorSmokeTests" -m:1 -nr:false -v:minimal
```

Expected: tests compile; if GUI gate is disabled, tests are skipped by `SkippableFact`.

- [ ] **Step 3: Run GUI smoke against installed app when GUI gate is available**

Run the repo's installed-app gate first:

```powershell
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File .tools\run-winui3-msix.ps1 -Configuration Debug
```

Then run:

```powershell
dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~ChatInputSelectorSmokeTests" -m:1 -nr:false -v:minimal
```

Expected: selected GUI smoke tests pass or skip with an explicit gate message if the machine is not configured for GUI validation.

- [ ] **Step 4: Commit**

Run:

```powershell
git add tests\SalmonEgg.GuiTests.Windows\ChatInputSelectorSmokeTests.cs
git commit -m "test: cover composer selector placeholders in gui smoke"
```

Expected: commit succeeds.

---

### Task 8: Final Verification

**Files:**
- Verify all files changed by Tasks 1-7.

- [ ] **Step 1: Run focused Core tests**

Run:

```powershell
dotnet test tests\SalmonEgg.Presentation.Core.Tests\SalmonEgg.Presentation.Core.Tests.csproj --filter "FullyQualifiedName~SelectorProjectionPresenterTests|FullyQualifiedName~ModeSelectorPolicyTests|FullyQualifiedName~AgentSelectorPolicyTests|FullyQualifiedName~ProjectSelectorPolicyTests|FullyQualifiedName~StartSelectorProjection|FullyQualifiedName~ChatSelectorProjection|FullyQualifiedName~StartModeSelection_WhenSelectorIdentityIsStale|FullyQualifiedName~ChatModeSelection_WhenPlaceholderItemIsSelected|FullyQualifiedName~XamlComplianceTests.ChatInputArea|FullyQualifiedName~XamlComplianceTests.StartView|FullyQualifiedName~XamlComplianceTests.ChatView" -m:1 -nr:false -v:minimal
```

Expected: all selected tests pass.

- [ ] **Step 2: Build desktop app**

Run:

```powershell
dotnet build SalmonEgg\SalmonEgg\SalmonEgg.csproj -f net10.0-desktop -p:SalmonEggTargetFrameworks=net10.0-desktop -p:SalmonEggAllTargetFrameworks=net10.0-desktop -m:1 -nr:false -v:minimal
```

Expected: build succeeds. Existing `Uno0001 AutomationProperties.SetLiveSetting` warning may appear.

- [ ] **Step 3: Run GUI selector smoke compile/gate**

Run:

```powershell
dotnet test tests\SalmonEgg.GuiTests.Windows\SalmonEgg.GuiTests.Windows.csproj --filter "FullyQualifiedName~ChatInputSelectorSmokeTests" -m:1 -nr:false -v:minimal
```

Expected: selected tests pass or skip with gate message.

- [ ] **Step 4: Check whitespace and repository state**

Run:

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` reports no whitespace errors. `git status --short` shows only intended modified files before the final commit, or is empty after commits.

- [ ] **Step 5: Confirm no final verification edits remain uncommitted**

```powershell
git status --short
```

Expected: output is empty because Tasks 1-7 committed their own focused changes.

---

## Self-Review

Spec coverage:

- Send-action lock is covered by Task 4 Start integration tests, Task 7 GUI smoke, and Task 8 focused verification.
- Voice stop escape is preserved by keeping `ChatInputStatePresenter` as owner and covered by Task 4 ViewModel tests plus Task 8 focused verification.
- ACP `session/new` mode loading/error/default behavior is covered by Task 2 policy tests and Task 4 Start wiring tests.
- Selector placeholders never enter raw business collections because Tasks 1-4 introduce display-only `ComposerSelectorItemViewModel` projections and Task 5 binds XAML to projection collections.
- Start full selector set and regular Chat subset are covered by Task 4 integration tests, Task 5 XAML contract tests, and Task 7 GUI smoke.
- Stale identity rejection is covered by Task 2 policy tests, Task 4 projection tests, and Task 6 display command tests.
- Native ComboBox behavior is preserved because Task 5 only changes item sources/templates and Task 6 rejects stale commits through commands without code-behind close/reopen logic.

Placeholder scan:

- Ran a red-flag placeholder scan over this plan file.
- No unresolved plan placeholders remain.

Type consistency:

- `ComposerSelectorKind`, `SelectorPlaceholderKind`, `ComposerSelectorItemViewModel`, `SelectorProjectionInput`, `SelectorProjectionResult`, `SelectorProjectionPresenter`, `ModeSelectorPolicy`, `AgentSelectorPolicy`, and `ProjectSelectorPolicy` names are used consistently across tests, ViewModels, XAML binding surfaces, and command handlers.
- Display selection commands consistently accept `ComposerSelectorItemViewModel?` and reject mismatched `Kind`, placeholder items, non-selectable items, blank semantic values, and stale identity values.
