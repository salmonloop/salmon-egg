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
            SelectedSemanticValue: "code",
            Placeholder: placeholder,
            ReplaceSelectionWithPlaceholder: true,
            DisableRealItems: true,
            SelectorEnabled: false));

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
            SelectedSemanticValue: "unclassified",
            Placeholder: fallback,
            ReplaceSelectionWithPlaceholder: true,
            DisableRealItems: false,
            SelectorEnabled: true));

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
            SelectedSemanticValue: "code",
            Placeholder: null,
            ReplaceSelectionWithPlaceholder: false,
            DisableRealItems: false,
            SelectorEnabled: true));

        Assert.Same(code, result.SelectedDisplayItem);
        Assert.False(result.IsSubmitBlocked);
        Assert.True(result.IsEnabled);
        Assert.Equal(new[] { "plan", "code" }, result.DisplayItems.Select(item => item.SemanticValue).ToArray());
    }

    [Fact]
    public void ComposerSelectorItemViewModel_UsesSemanticValueForStableAutomationId()
    {
        var item = ComposerSelectorItemViewModel.Real(
            ComposerSelectorKind.Agent,
            "5813c231-cba2-40de-8299-c1ce799ad7ef",
            "Claude Code",
            "agent|profile|connection");

        Assert.Equal(
            "ComposerSelectorItem.Agent.5813c231_cba2_40de_8299_c1ce799ad7ef",
            item.AutomationId);
    }

    [Fact]
    public void ComposerSelectorItemViewModel_UsesPlaceholderKindWhenSemanticValueMissing()
    {
        var item = ComposerSelectorItemViewModel.Placeholder(
            ComposerSelectorKind.Mode,
            SelectorPlaceholderKind.Loading,
            "Loading modes...",
            identity: "profile-1|conn-1|cwd-1|0",
            blocksSubmit: true);

        Assert.Equal("ComposerSelectorItem.Mode.Loading", item.AutomationId);
    }
}
