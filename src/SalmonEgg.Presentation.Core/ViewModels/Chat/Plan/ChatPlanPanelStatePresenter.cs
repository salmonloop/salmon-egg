namespace SalmonEgg.Presentation.Core.ViewModels.Chat.PlanPanel;

public sealed class ChatPlanPanelStatePresenter
{
    public ChatPlanPanelState Present(bool showPlanPanel, int planEntryCount)
    {
        var hasPlanEntries = planEntryCount > 0;
        return new ChatPlanPanelState(
            HasPlanEntries: hasPlanEntries,
            ShouldShowPlanList: showPlanPanel && hasPlanEntries,
            ShouldShowPlanEmpty: !showPlanPanel || !hasPlanEntries);
    }
}
