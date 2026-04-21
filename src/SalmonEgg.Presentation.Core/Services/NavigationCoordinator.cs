using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class NavigationCoordinator : INavigationCoordinator
{
    private readonly IShellSelectionMutationSink _selectionSink;
    private readonly IShellNavigationRuntimeState _runtimeState;
    private readonly IConversationSessionSwitcher _conversationSessionSwitcher;
    private readonly IConversationActivationPreview? _conversationActivationPreview;
    private readonly INavigationProjectSelectionStore _projectSelectionStore;
    private readonly IShellNavigationService _shellNavigationService;
    private readonly ILogger<NavigationCoordinator> _logger;
    private readonly SemaphoreSlim _sessionActivationGate = new(1, 1);
    private readonly object _sessionActivationSync = new();
    private CancellationTokenSource? _sessionActivationCts;

    public NavigationCoordinator(
        IShellSelectionMutationSink selectionSink,
        IShellNavigationRuntimeState runtimeState,
        IConversationSessionSwitcher conversationSessionSwitcher,
        INavigationProjectSelectionStore projectSelectionStore,
        IShellNavigationService shellNavigationService,
        ILogger<NavigationCoordinator>? logger = null)
    {
        _selectionSink = selectionSink ?? throw new ArgumentNullException(nameof(selectionSink));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _conversationSessionSwitcher = conversationSessionSwitcher ?? throw new ArgumentNullException(nameof(conversationSessionSwitcher));
        _conversationActivationPreview = conversationSessionSwitcher as IConversationActivationPreview;
        _projectSelectionStore = projectSelectionStore ?? throw new ArgumentNullException(nameof(projectSelectionStore));
        _shellNavigationService = shellNavigationService ?? throw new ArgumentNullException(nameof(shellNavigationService));
        _logger = logger ?? NullLogger<NavigationCoordinator>.Instance;
    }

    public async Task<bool> ActivateStartAsync(string? projectIdForNewSession = null)
    {
        try
        {
            var activationToken = BeginActivation();
            CancelInFlightSessionActivation();
            var navigationResult = await NavigateToStartAsync(activationToken).ConfigureAwait(true);
            if (navigationResult.Succeeded && IsLatestActivationToken(activationToken))
            {
                ClearPendingSessionPreviewState(activationToken);
                _projectSelectionStore.RememberSelectedProject(projectIdForNewSession);
                _runtimeState.CurrentShellContent = ShellNavigationContent.Start;
                _selectionSink.SetSelection(NavigationSelectionState.StartSelection);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public async Task ActivateDiscoverSessionsAsync()
    {
        try
        {
            var activationToken = BeginActivation();
            CancelInFlightSessionActivation();
            var navigationResult = await NavigateToDiscoverSessionsAsync(activationToken).ConfigureAwait(true);
            if (navigationResult.Succeeded && IsLatestActivationToken(activationToken))
            {
                ClearPendingSessionPreviewState(activationToken);
                _runtimeState.CurrentShellContent = ShellNavigationContent.DiscoverSessions;
                _selectionSink.SetSelection(NavigationSelectionState.DiscoverSessionsSelection);
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
            CancelInFlightSessionActivation();
            var navigationResult = await NavigateToSettingsAsync(
                    string.IsNullOrWhiteSpace(settingsKey) ? "General" : settingsKey,
                    activationToken)
                .ConfigureAwait(true);
            if (navigationResult.Succeeded && IsLatestActivationToken(activationToken))
            {
                ClearPendingSessionPreviewState(activationToken);
                _runtimeState.CurrentShellContent = ShellNavigationContent.Settings;
                _selectionSink.SetSelection(NavigationSelectionState.SettingsSelection);
            }
        }
        catch
        {
        }
    }

    public Task<bool> ActivateSessionAsync(string sessionId, string? projectId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.FromResult(false);
        }

        CancellationTokenSource? previousActivation;
        SessionActivationRequest request;
        lock (_sessionActivationSync)
        {
            var activeActivation = _runtimeState.ActiveSessionActivation;
            var duplicateLatestIntent = activeActivation is not null
                && activeActivation.Matches(sessionId)
                && activeActivation.Version == _runtimeState.LatestActivationToken
                && !IsTerminalPhase(activeActivation.Phase);
            if (duplicateLatestIntent)
            {
                _logger.LogInformation(
                    "Navigation activation ignored duplicate latest-intent. sessionId={SessionId} phase={Phase}",
                    sessionId,
                    activeActivation!.Phase);
                return Task.FromResult(true);
            }

            previousActivation = _sessionActivationCts;
            request = new SessionActivationRequest(
                sessionId,
                projectId,
                BeginActivation(),
                new CancellationTokenSource());
            _sessionActivationCts = request.CancellationTokenSource;
            _runtimeState.DesiredSessionId = sessionId;
            _runtimeState.ActiveSessionActivationVersion = request.Version;
            _runtimeState.IsSessionActivationInProgress = true;
            _runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                request.SessionId,
                request.ProjectId,
                request.Version,
                SessionActivationPhase.NavigatingToChatShell);
        }

        try
        {
            if (TryCancelActivation(previousActivation))
            {
                _logger.LogInformation(
                    "Navigation activation canceled previous request. sessionId={SessionId} version={Version}",
                    sessionId,
                    request.Version);
            }
        }
        finally
        {
            previousActivation?.Dispose();
        }

        _logger.LogInformation(
            "Navigation activation started. sessionId={SessionId} version={Version}",
            sessionId,
            request.Version);
        return ActivateSessionCoreAsync(request);
    }

    private async Task<bool> ActivateSessionCoreAsync(SessionActivationRequest request)
    {
        var activationGateEntered = false;
        var committed = false;
        var shouldPrimeSessionSwitchPreview = _runtimeState.CurrentShellContent != ShellNavigationContent.Chat;

        try
        {
            if (shouldPrimeSessionSwitchPreview)
            {
                _conversationActivationPreview?.PrimeSessionSwitchPreview(request.SessionId);
            }
            var navigationResult = await NavigateToChatAsync(request.Version).ConfigureAwait(true);
            if (!navigationResult.Succeeded
                || !IsLatestActivationToken(request.Version)
                || request.CancellationToken.IsCancellationRequested)
            {
                MarkSessionActivationFaulted(request, navigationResult.Succeeded ? "SupersededBeforeChatShell" : "ChatShellNavigationFailed");
                return false;
            }

            PublishSessionActivationPhase(request, SessionActivationPhase.SelectingConversation);
            _runtimeState.CurrentShellContent = ShellNavigationContent.Chat;

            await _sessionActivationGate
                .WaitAsync(request.CancellationToken)
                .ConfigureAwait(true);
            activationGateEntered = true;
            if (!IsLatestActivationToken(request.Version) || request.CancellationToken.IsCancellationRequested)
            {
                MarkSessionActivationFaulted(request, "SupersededBeforeConversationSelection");
                return false;
            }

            var activated = await _conversationSessionSwitcher
                .SwitchConversationAsync(request.SessionId, request.CancellationToken)
                .ConfigureAwait(true);
            if (!activated || !IsLatestActivationToken(request.Version) || request.CancellationToken.IsCancellationRequested)
            {
                MarkSessionActivationFaulted(
                    request,
                    activated ? "SupersededAfterConversationSelection" : "ConversationSelectionFailed");
                return false;
            }

            _projectSelectionStore.RememberSelectedProject(request.ProjectId);
            _selectionSink.SetSelection(new NavigationSelectionState.Session(request.SessionId));
            PublishSessionActivationPhase(request, SessionActivationPhase.Selected);
            committed = true;
            _logger.LogInformation(
                "Navigation activation committed. sessionId={SessionId} version={Version}",
                request.SessionId,
                request.Version);
            return true;
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            MarkSessionActivationFaulted(request, "Canceled");
            _logger.LogInformation(
                "Navigation activation canceled. sessionId={SessionId} version={Version}",
                request.SessionId,
                request.Version);
            return false;
        }
        catch (Exception ex)
        {
            MarkSessionActivationFaulted(request, ex.GetType().Name);
            _logger.LogError(
                ex,
                "Navigation activation failed. sessionId={SessionId} version={Version}",
                request.SessionId,
                request.Version);
        }
        finally
        {
            if (shouldPrimeSessionSwitchPreview)
            {
                _conversationActivationPreview?.ClearSessionSwitchPreview(request.SessionId);
            }
            if (activationGateEntered)
            {
                _sessionActivationGate.Release();
            }

            EndSessionActivationRequest(request, committed);
        }

        return false;
    }

    public void SyncSelectionFromShellContent(ShellNavigationContent content)
    {
        _runtimeState.CurrentShellContent = content;
        if (_runtimeState.ActiveSessionActivation is { } activeActivation
            && !IsTerminalPhase(activeActivation.Phase))
        {
            return;
        }

        switch (content)
        {
            case ShellNavigationContent.Start:
                ClearPendingSessionPreviewState(_runtimeState.LatestActivationToken);
                _selectionSink.SetSelection(NavigationSelectionState.StartSelection);
                return;

            case ShellNavigationContent.DiscoverSessions:
                ClearPendingSessionPreviewState(_runtimeState.LatestActivationToken);
                _selectionSink.SetSelection(NavigationSelectionState.DiscoverSessionsSelection);
                return;

            case ShellNavigationContent.Settings:
                ClearPendingSessionPreviewState(_runtimeState.LatestActivationToken);
                _selectionSink.SetSelection(NavigationSelectionState.SettingsSelection);
                return;

            default:
                return;
        }
    }

    private long BeginActivation()
    {
        lock (_sessionActivationSync)
        {
            var activationToken = _runtimeState.LatestActivationToken + 1;
            _runtimeState.LatestActivationToken = activationToken;
            return activationToken;
        }
    }

    private void EndSessionActivationRequest(SessionActivationRequest request, bool committed)
    {
        var shouldDisposeRequestCts = !committed;
        lock (_sessionActivationSync)
        {
            if (ReferenceEquals(_sessionActivationCts, request.CancellationTokenSource)
                && IsLatestActivationToken(request.Version))
            {
                if (committed)
                {
                    _runtimeState.DesiredSessionId = null;
                    _runtimeState.CommittedSessionId = request.SessionId;
                    _runtimeState.CommittedSessionActivationVersion = request.Version;
                }
                else
                {
                    _sessionActivationCts = null;
                    _runtimeState.IsSessionActivationInProgress = false;
                    _runtimeState.ActiveSessionActivationVersion = 0;
                }
            }
        }

        if (shouldDisposeRequestCts)
        {
            request.CancellationTokenSource.Dispose();
        }
    }

    private void ClearPendingSessionPreviewState(long activationToken)
    {
        CancellationTokenSource? pendingActivation = null;
        var shouldDisposePendingActivation = false;
        lock (_sessionActivationSync)
        {
            if (!IsLatestActivationToken(activationToken))
            {
                return;
            }

            pendingActivation = _sessionActivationCts;
            _sessionActivationCts = null;
            shouldDisposePendingActivation = pendingActivation is not null;
            _runtimeState.DesiredSessionId = null;
            _runtimeState.IsSessionActivationInProgress = false;
            _runtimeState.ActiveSessionActivationVersion = 0;
            _runtimeState.ActiveSessionActivation = null;
        }

        if (shouldDisposePendingActivation)
        {
            pendingActivation!.Dispose();
        }
    }

    private void PublishSessionActivationPhase(SessionActivationRequest request, SessionActivationPhase phase, string? reason = null)
    {
        lock (_sessionActivationSync)
        {
            if (!IsLatestActivationToken(request.Version))
            {
                return;
            }

            _runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                request.SessionId,
                request.ProjectId,
                request.Version,
                phase,
                reason);
        }
    }

    private void MarkSessionActivationFaulted(SessionActivationRequest request, string reason)
    {
        lock (_sessionActivationSync)
        {
            if (!IsLatestActivationToken(request.Version))
            {
                return;
            }

            _runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                request.SessionId,
                request.ProjectId,
                request.Version,
                SessionActivationPhase.Faulted,
                reason);
        }
    }

    private bool IsLatestActivationToken(long activationToken)
    {
        lock (_sessionActivationSync)
        {
            return _runtimeState.LatestActivationToken == activationToken;
        }
    }

    private static bool IsTerminalPhase(SessionActivationPhase phase)
        => phase == SessionActivationPhase.None || phase == SessionActivationPhase.Faulted;

    private void CancelInFlightSessionActivation()
    {
        CancellationTokenSource? pendingActivation = null;
        lock (_sessionActivationSync)
        {
            pendingActivation = _sessionActivationCts;
        }

        TryCancelActivation(pendingActivation);
    }

    private static bool TryCancelActivation(CancellationTokenSource? activation)
    {
        if (activation is null)
        {
            return false;
        }

        try
        {
            activation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

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

    private ValueTask<ShellNavigationResult> NavigateToDiscoverSessionsAsync(long activationToken)
    {
        return _shellNavigationService is IActivationTokenShellNavigationService tokenAware
            ? tokenAware.NavigateToDiscoverSessions(activationToken)
            : _shellNavigationService.NavigateToDiscoverSessions();
    }

    private sealed record SessionActivationRequest(
        string SessionId,
        string? ProjectId,
        long Version,
        CancellationTokenSource CancellationTokenSource)
    {
        public CancellationToken CancellationToken => CancellationTokenSource.Token;
    }
}

public interface IActivationTokenShellNavigationService
{
    ValueTask<ShellNavigationResult> NavigateToSettings(string key, long activationToken);
    ValueTask<ShellNavigationResult> NavigateToChat(long activationToken);
    ValueTask<ShellNavigationResult> NavigateToStart(long activationToken);
    ValueTask<ShellNavigationResult> NavigateToDiscoverSessions(long activationToken);
}
