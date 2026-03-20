using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class NavigationCoordinator : INavigationCoordinator
{
    private readonly INavigationSelectionHost _navigationHost;
    private readonly IConversationSessionSwitcher _conversationSessionSwitcher;
    private readonly INavigationProjectSelectionStore _projectSelectionStore;
    private readonly IShellNavigationService _shellNavigationService;

    public NavigationCoordinator(
        INavigationSelectionHost navigationHost,
        IConversationSessionSwitcher conversationSessionSwitcher,
        INavigationProjectSelectionStore projectSelectionStore,
        IShellNavigationService shellNavigationService)
    {
        _navigationHost = navigationHost ?? throw new ArgumentNullException(nameof(navigationHost));
        _conversationSessionSwitcher = conversationSessionSwitcher ?? throw new ArgumentNullException(nameof(conversationSessionSwitcher));
        _projectSelectionStore = projectSelectionStore ?? throw new ArgumentNullException(nameof(projectSelectionStore));
        _shellNavigationService = shellNavigationService ?? throw new ArgumentNullException(nameof(shellNavigationService));
        _navigationHost.RegisterSessionActivationHandler(ActivateSessionAsync);
    }

    public async Task ActivateStartAsync()
    {
        try
        {
            var navigationResult = await _shellNavigationService.NavigateToStart().ConfigureAwait(true);
            if (navigationResult.Succeeded)
            {
                _navigationHost.SelectStart();
            }
        }
        catch
        {
        }
    }

    public async Task ActivateSettingsAsync(string settingsKey)
    {
        try
        {
            var navigationResult = await _shellNavigationService
                .NavigateToSettings(string.IsNullOrWhiteSpace(settingsKey) ? "General" : settingsKey)
                .ConfigureAwait(true);
            if (navigationResult.Succeeded)
            {
                _navigationHost.SelectSettings();
            }
        }
        catch
        {
        }
    }

    public async Task<bool> ActivateSessionAsync(string sessionId, string? projectId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var switched = await _conversationSessionSwitcher.TrySwitchToSessionAsync(sessionId).ConfigureAwait(true);
        if (!switched)
        {
            return false;
        }

        try
        {
            var navigationResult = await _shellNavigationService.NavigateToChat().ConfigureAwait(true);
            if (!navigationResult.Succeeded)
            {
                return false;
            }

            _projectSelectionStore.RememberSelectedProject(projectId);
            _navigationHost.SelectSession(sessionId);
            return true;
        }
        catch
        {
        }

        return false;
    }

    public Task ToggleProjectAsync(string projectId)
    {
        _navigationHost.ToggleProjectExpanded(projectId);
        return Task.CompletedTask;
    }

    public void SyncSelectionFromShellContent(ShellNavigationContent content, string? currentSessionId)
    {
        switch (content)
        {
            case ShellNavigationContent.Start:
                _navigationHost.SelectStart();
                return;

            case ShellNavigationContent.Settings:
                _navigationHost.SelectSettings();
                return;

            case ShellNavigationContent.Chat:
                if (!string.IsNullOrWhiteSpace(currentSessionId))
                {
                    _navigationHost.SelectSession(currentSessionId);
                }

                return;

            default:
                return;
        }
    }
}
