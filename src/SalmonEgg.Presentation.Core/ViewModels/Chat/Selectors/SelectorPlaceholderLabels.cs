namespace SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;

public sealed record ModeSelectorPlaceholderLabels(
    string Unresolved,
    string Loading,
    string Error,
    string Default);

public sealed record AgentSelectorPlaceholderLabels(
    string Loading,
    string Error,
    string Unresolved,
    string Empty);

public sealed record ProjectSelectorPlaceholderLabels(
    string Unresolved,
    string Fallback,
    string RemoteSelectionRequired);

public sealed record ModelSelectorPlaceholderLabels(
    string Unresolved,
    string Loading,
    string Error);
