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
            HasLegalFallback: true,
            Labels: Labels()));

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
            HasLegalFallback: false,
            Labels: Labels()));

        Assert.Equal(SelectorPlaceholderKind.Unresolved, projection.Placeholder!.PlaceholderKind);
        Assert.Equal("project-unresolved", projection.Placeholder.DisplayName);
        Assert.True(projection.Placeholder.BlocksSubmit);
        Assert.True(projection.ReplaceSelectionWithPlaceholder);
    }

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

    private static ProjectSelectorPlaceholderLabels Labels()
        => new(
            Unresolved: "project-unresolved",
            Fallback: "project-fallback",
            RemoteSelectionRequired: "remote-selection-required");
}
