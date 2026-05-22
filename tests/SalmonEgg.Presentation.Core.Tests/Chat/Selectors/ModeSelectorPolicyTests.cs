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
        Assert.False(projection.ReplaceSelectionWithPlaceholder);
        Assert.Equal("code", projection.SelectedSemanticValue);
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
