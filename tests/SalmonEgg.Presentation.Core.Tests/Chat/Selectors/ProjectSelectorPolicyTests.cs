using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
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
            Projects: new[] { new StartProjectOptionViewModel(NavigationProjectIds.Unclassified, "Unclassified") },
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
