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
    private long _activationTokenCounter;
    private long _latestActivationToken;

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
            var activationToken = BeginActivation();
            var navigationResult = await NavigateToStartAsync(activationToken).ConfigureAwait(true);
            if (navigationResult.Succeeded && IsLatestActivationToken(activationToken))
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
            var activationToken = BeginActivation();
            var navigationResult = await NavigateToSettingsAsync(
                    string.IsNullOrWhiteSpace(settingsKey) ? "General" : settingsKey,
                    activationToken)
                .ConfigureAwait(true);
            if (navigationResult.Succeeded && IsLatestActivationToken(activationToken))
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

        var activationToken = BeginActivation();
        var activationResult = await _conversationActivationCoordinator
            .ActivateSessionAsync(sessionId)
            .ConfigureAwait(true);
        if (!activationResult.Succeeded)
        {
            return false;
        }

        try
        {
            var navigationResult = await NavigateToChatAsync(activationToken).ConfigureAwait(true);
            if (!navigationResult.Succeeded || !IsLatestActivationToken(activationToken))
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

    private long BeginActivation()
    {
        var activationToken = Interlocked.Increment(ref _activationTokenCounter);
        Interlocked.Exchange(ref _latestActivationToken, activationToken);
        return activationToken;
    }

    private bool IsLatestActivationToken(long activationToken)
        => Interlocked.Read(ref _latestActivationToken) == activationToken;

    private ValueTask<ShellNavigationResult> NavigateToStartAsync(long activationToken)
    {
        return _shellNavigationService is IActivationTokenShellNavigationService tokenAware
            ? tokenAware.NavigateToStart(activationToken)
            : _shellNavigationService.NavigateToStart();
    }

    private ValueTask<ShellNavigationResult> NavigateToChatAsync(long activationToken)
    {
        return _shellNavigationService is IActivationTokenShellNavigationService tokenAware
            ? tokenAware.NavigateToChat(activationToken)
            : _shellNavigationService.NavigateToChat();
    }

    private ValueTask<ShellNavigationResult> NavigateToSettingsAsync(string key, long activationToken)
    {
        return _shellNavigationService is IActivationTokenShellNavigationService tokenAware
            ? tokenAware.NavigateToSettings(key, activationToken)
            : _shellNavigationService.NavigateToSettings(key);
    }
}

public interface IActivationTokenShellNavigationService
{
    ValueTask<ShellNavigationResult> NavigateToSettings(string key, long activationToken);
    ValueTask<ShellNavigationResult> NavigateToChat(long activationToken);
    ValueTask<ShellNavigationResult> NavigateToStart(long activationToken);
}
