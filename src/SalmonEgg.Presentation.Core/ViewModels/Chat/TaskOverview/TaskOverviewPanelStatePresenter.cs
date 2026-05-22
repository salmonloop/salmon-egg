namespace SalmonEgg.Presentation.Core.ViewModels.Chat.TaskOverview;

public sealed class TaskOverviewPanelStatePresenter
{
    public TaskOverviewPanelState Present(
        int planCount,
        int changeCount,
        int activePlanCount = 0,
        int completedPlanCount = 0)
    {
        var hasPlan = planCount > 0;
        var hasChanges = changeCount > 0;
        return new TaskOverviewPanelState(
            planCount,
            changeCount,
            activePlanCount,
            completedPlanCount,
            ShouldShowEmpty: !hasPlan && !hasChanges,
            ShouldShowPlanList: hasPlan,
            ShouldShowChangesList: hasChanges);
    }
}
