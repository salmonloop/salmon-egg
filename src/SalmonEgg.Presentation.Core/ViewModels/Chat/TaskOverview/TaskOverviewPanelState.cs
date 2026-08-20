namespace SalmonEgg.Presentation.Core.ViewModels.Chat.TaskOverview;

public sealed record TaskOverviewPanelState(
    int PlanCount,
    int ChangeCount,
    int ActivePlanCount,
    int PendingPlanCount,
    int CompletedPlanCount,
    bool ShouldShowPlanList,
    bool ShouldShowChangesList)
{
    public bool HasContent => ShouldShowPlanList || ShouldShowChangesList;
}
