namespace SalmonEgg.Presentation.Core.ViewModels.Chat.PlanPanel;

public sealed record ChatPlanPanelState(
    bool HasPlanEntries,
    bool ShouldShowPlanList,
    bool ShouldShowPlanEmpty);
