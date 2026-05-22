namespace SalmonEgg.Presentation.Core.ViewModels.Chat.TaskOverview;

public sealed record TaskOverviewPanelState(
    int PlanCount,
    int ChangeCount,
    bool ShouldShowEmpty,
    bool ShouldShowPlanList,
    bool ShouldShowChangesList);
