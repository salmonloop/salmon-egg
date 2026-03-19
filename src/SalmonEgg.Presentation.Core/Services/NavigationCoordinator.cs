using System;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class NavigationCoordinator : INavigationCoordinator
{
    private readonly MainNavigationViewModel _navigationViewModel;
    private readonly ChatViewModel _chatViewModel;
    private readonly AppPreferencesViewModel _preferences;
    private readonly IShellNavigationService _shellNavigationService;

    public NavigationCoordinator(
        MainNavigationViewModel navigationViewModel,
        ChatViewModel chatViewModel,
        AppPreferencesViewModel preferences,
        IShellNavigationService shellNavigationService)
    {
        _navigationViewModel = navigationViewModel ?? throw new ArgumentNullException(nameof(navigationViewModel));
        _chatViewModel = chatViewModel ?? throw new ArgumentNullException(nameof(chatViewModel));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _shellNavigationService = shellNavigationService ?? throw new ArgumentNullException(nameof(shellNavigationService));
        _navigationViewModel.RegisterSessionActivationHandler(ActivateSessionAsync);
    }

    public Task ActivateStartAsync()
    {
        _navigationViewModel.SelectStart();
        _shellNavigationService.NavigateToStart();
        return Task.CompletedTask;
    }

    public Task ActivateSettingsAsync(string settingsKey)
    {
        _navigationViewModel.SelectSettings();
        _shellNavigationService.NavigateToSettings(string.IsNullOrWhiteSpace(settingsKey) ? "General" : settingsKey);
        return Task.CompletedTask;
    }

    public async Task ActivateSessionAsync(string sessionId, string? projectId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        _navigationViewModel.SelectSession(sessionId);
        _shellNavigationService.NavigateToChat();

        _preferences.LastSelectedProjectId = string.Equals(projectId, MainNavigationViewModel.UnclassifiedProjectId, StringComparison.Ordinal)
            ? null
            : projectId;

        await _chatViewModel.TrySwitchToSessionAsync(sessionId).ConfigureAwait(true);
    }

    public Task ToggleProjectAsync(string projectId)
    {
        _navigationViewModel.ToggleProjectExpanded(projectId);
        return Task.CompletedTask;
    }

    public void SyncSelectionFromShellContent(ShellNavigationContent content, string? currentSessionId)
    {
        switch (content)
        {
            case ShellNavigationContent.Start:
                _navigationViewModel.SelectStart();
                return;

            case ShellNavigationContent.Settings:
                _navigationViewModel.SelectSettings();
                return;

            case ShellNavigationContent.Chat:
                if (!string.IsNullOrWhiteSpace(currentSessionId))
                {
                    _navigationViewModel.SelectSession(currentSessionId);
                }

                return;

            default:
                return;
        }
    }
}
