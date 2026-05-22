using SalmonEgg.Presentation.Core.ViewModels.Chat.TaskOverview;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.TaskOverview;

public sealed class TaskOverviewPanelStatePresenterTests
{
    [Theory]
    [InlineData(0, 0, true, false, false)]
    [InlineData(2, 0, false, true, false)]
    [InlineData(0, 1, false, false, true)]
    [InlineData(2, 1, false, true, true)]
    public void Present_ComputesVisibility(int planCount, int changeCount, bool empty, bool planList, bool changesList)
    {
        var presenter = new TaskOverviewPanelStatePresenter();

        var state = presenter.Present(planCount, changeCount, activePlanCount: 1, completedPlanCount: 2);

        Assert.Equal(planCount, state.PlanCount);
        Assert.Equal(changeCount, state.ChangeCount);
        Assert.Equal(1, state.ActivePlanCount);
        Assert.Equal(2, state.CompletedPlanCount);
        Assert.Equal(empty, state.ShouldShowEmpty);
        Assert.Equal(planList, state.ShouldShowPlanList);
        Assert.Equal(changesList, state.ShouldShowChangesList);
    }
}
