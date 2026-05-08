namespace SalmonEgg.Presentation.ViewModels.Chat.Panels;

public sealed record ChatConversationPanelSelection(
    System.Collections.ObjectModel.ObservableCollection<TerminalPanelSessionViewModel> TerminalSessions,
    TerminalPanelSessionViewModel? SelectedTerminal,
    AskUserRequestViewModel? PendingAskUserRequest);
