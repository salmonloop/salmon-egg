using SalmonEgg.Presentation.Core.ViewModels.Chat.PlanPanel;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.PlanPanel;

[Collection("NonParallel")]
public sealed class ChatPlanPanelStatePresenterTests
{
    private readonly ChatPlanPanelStatePresenter _sut = new();

    [Fact]
    public void Present_WithNoEntries_ShowsEmptyState()
    {
        var state = _sut.Present(showPlanPanel: true, planEntryCount: 0);

        Assert.False(state.HasPlanEntries);
        Assert.False(state.ShouldShowPlanList);
        Assert.True(state.ShouldShowPlanEmpty);
    }

    [Fact]
    public void Present_WithEntriesAndVisiblePanel_ShowsList()
    {
        var state = _sut.Present(showPlanPanel: true, planEntryCount: 2);

        Assert.True(state.HasPlanEntries);
        Assert.True(state.ShouldShowPlanList);
        Assert.False(state.ShouldShowPlanEmpty);
    }

    [Fact]
    public void Present_WithHiddenPanel_HidesListAndShowsEmpty()
    {
        var state = _sut.Present(showPlanPanel: false, planEntryCount: 2);

        Assert.True(state.HasPlanEntries);
        Assert.False(state.ShouldShowPlanList);
        Assert.True(state.ShouldShowPlanEmpty);
    }
}
