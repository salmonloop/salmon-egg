using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Services;

public interface IUiInteractionService
{
    Task ShowInfoAsync(string message);

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
}

