using System.Collections.ObjectModel;

namespace SalmonEgg.Presentation.ViewModels.Chat.Panels;

public sealed record ChatConversationPanelSelection(
    ObservableCollection<BottomPanelTabViewModel> Tabs,
    BottomPanelTabViewModel? SelectedTab,
    ObservableCollection<TerminalPanelSessionViewModel> TerminalSessions,
    TerminalPanelSessionViewModel? SelectedTerminal,
    AskUserRequestViewModel? PendingAskUserRequest);
