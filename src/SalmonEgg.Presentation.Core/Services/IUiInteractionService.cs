using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Services;

public interface IUiInteractionService
{
    Task ShowInfoAsync(string message);

    Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText);

    Task<string?> PromptTextAsync(
        string title,
        string primaryButtonText,
        string closeButtonText,
        string initialText);

    Task<string?> PickFolderAsync();

    Task ShowSessionsListDialogAsync(
        string title,
        IReadOnlyList<SessionNavItemViewModel> sessions,
        Action<string> onPickSession);

    Task<string?> PickConversationProjectAsync(
        string title,
        string sessionTitle,
        IReadOnlyList<ConversationProjectTargetOption> options,
        string? selectedProjectId);
}
