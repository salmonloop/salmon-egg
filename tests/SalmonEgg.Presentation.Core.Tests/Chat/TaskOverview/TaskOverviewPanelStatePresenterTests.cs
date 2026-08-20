using SalmonEgg.Presentation.Core.ViewModels.Chat.TaskOverview;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.TaskOverview;

public sealed class TaskOverviewPanelStatePresenterTests
{
    [Theory]
    [InlineData(0, 0, false, false)]
    [InlineData(2, 0, true, false)]
    [InlineData(0, 1, false, true)]
    [InlineData(2, 1, true, true)]
    public void Present_ComputesContentDrivenVisibility(int planCount, int changeCount, bool planList, bool changesList)
    {
        var presenter = new TaskOverviewPanelStatePresenter();

        var state = presenter.Present(
            planCount,
            changeCount,
            activePlanCount: 1,
            pendingPlanCount: 3,
            completedPlanCount: 2);

        Assert.Equal(planCount, state.PlanCount);
        Assert.Equal(changeCount, state.ChangeCount);
        Assert.Equal(1, state.ActivePlanCount);
        Assert.Equal(3, state.PendingPlanCount);
        Assert.Equal(2, state.CompletedPlanCount);
        Assert.Equal(planList, state.ShouldShowPlanList);
        Assert.Equal(changesList, state.ShouldShowChangesList);
        Assert.Equal(planList || changesList, state.HasContent);
    }
}
