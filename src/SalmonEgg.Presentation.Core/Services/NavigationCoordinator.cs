using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class NavigationCoordinator : INavigationCoordinator
{
    private readonly IShellSelectionMutationSink _selectionSink;
    private readonly IConversationActivationCoordinator _conversationActivationCoordinator;
    private readonly INavigationProjectSelectionStore _projectSelectionStore;
    private readonly IShellNavigationService _shellNavigationService;

    public NavigationCoordinator(
        IShellSelectionMutationSink selectionSink,
        IConversationActivationCoordinator conversationActivationCoordinator,
        INavigationProjectSelectionStore projectSelectionStore,
        IShellNavigationService shellNavigationService)
    {
        _selectionSink = selectionSink ?? throw new ArgumentNullException(nameof(selectionSink));
        _conversationActivationCoordinator = conversationActivationCoordinator ?? throw new ArgumentNullException(nameof(conversationActivationCoordinator));
        _projectSelectionStore = projectSelectionStore ?? throw new ArgumentNullException(nameof(projectSelectionStore));
        _shellNavigationService = shellNavigationService ?? throw new ArgumentNullException(nameof(shellNavigationService));
    }

    public async Task ActivateStartAsync()
    {
        try
        {
            var navigationResult = await _shellNavigationService.NavigateToStart().ConfigureAwait(true);
            if (navigationResult.Succeeded)
            {
                _selectionSink.SetSelection(NavigationSelectionState.StartSelection);
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
                _selectionSink.SetSelection(NavigationSelectionState.SettingsSelection);
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

        var activationResult = await _conversationActivationCoordinator
            .ActivateSessionAsync(sessionId)
            .ConfigureAwait(true);
        if (!activationResult.Succeeded)
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
            _selectionSink.SetSelection(new NavigationSelectionState.Session(sessionId));
            return true;
        }
        catch
        {
        }

        return false;
    }

    public void SyncSelectionFromShellContent(ShellNavigationContent content)
    {
        switch (content)
        {
            case ShellNavigationContent.Start:
                _selectionSink.SetSelection(NavigationSelectionState.StartSelection);
                return;

            case ShellNavigationContent.Settings:
                _selectionSink.SetSelection(NavigationSelectionState.SettingsSelection);
                return;

            default:
                return;
        }
    }
}
