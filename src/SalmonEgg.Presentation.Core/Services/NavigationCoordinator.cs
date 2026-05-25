using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class NavigationCoordinator : INavigationCoordinator
{
    private readonly IShellSelectionMutationSink _selectionSink;
    private readonly IShellNavigationRuntimeState _runtimeState;
    private readonly IConversationSessionSwitcher _conversationSessionSwitcher;
    private readonly IDiscoverSessionsConnectionFacade _discoverConnectionFacade;
    private readonly INavigationProjectSelectionStore _projectSelectionStore;
    private readonly IShellNavigationService _shellNavigationService;
    private readonly ILogger<NavigationCoordinator> _logger;
    private readonly SemaphoreSlim _sessionActivationGate = new(1, 1);
    private readonly object _sessionActivationSync = new();
    private CancellationTokenSource? _sessionActivationCts;
    private bool _sessionActivationCtsCanBeDisposedByCanceler;

    public NavigationCoordinator(
        IShellSelectionMutationSink selectionSink,
        IShellNavigationRuntimeState runtimeState,
        IConversationSessionSwitcher conversationSessionSwitcher,
        IDiscoverSessionsConnectionFacade discoverConnectionFacade,
        INavigationProjectSelectionStore projectSelectionStore,
        IShellNavigationService shellNavigationService,
        ILogger<NavigationCoordinator>? logger = null)
    {
        _selectionSink = selectionSink ?? throw new ArgumentNullException(nameof(selectionSink));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _conversationSessionSwitcher = conversationSessionSwitcher ?? throw new ArgumentNullException(nameof(conversationSessionSwitcher));
        _discoverConnectionFacade = discoverConnectionFacade ?? throw new ArgumentNullException(nameof(discoverConnectionFacade));
        _projectSelectionStore = projectSelectionStore ?? throw new ArgumentNullException(nameof(projectSelectionStore));
        _shellNavigationService = shellNavigationService ?? throw new ArgumentNullException(nameof(shellNavigationService));
        _logger = logger ?? NullLogger<NavigationCoordinator>.Instance;
    }

    public async Task<bool> ActivateStartAsync(string? projectIdForNewSession = null)
    {
        var activationToken = BeginActivation();
        try
        {
            CancelInFlightSessionActivation();
            var navigationResult = await NavigateToStartAsync(activationToken).ConfigureAwait(true);
            if (!navigationResult.Succeeded)
            {
                _logger.LogWarning(
                    "Navigation activation failed. content={Content} activationVersion={ActivationVersion} reason={Reason}",
                    ShellNavigationContent.Start,
                    activationToken,
                    ActivationFaultReasons.StartNavigationFailed);
                return false;
            }

            if (IsLatestActivationToken(activationToken))
            {
                ClearPendingSessionPreviewState(activationToken);
                _projectSelectionStore.RememberSelectedProject(projectIdForNewSession);
                _runtimeState.CurrentShellContent = ShellNavigationContent.Start;
                _selectionSink.SetSelection(NavigationSelectionState.StartSelection);
                return true;
            }

            _logger.LogInformation(
                "Navigation activation ignored stale completion. content={Content} activationVersion={ActivationVersion} reason={Reason}",
                ShellNavigationContent.Start,
                activationToken,
                ActivationFaultReasons.SupersededBeforeCommit);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Navigation activation threw. content={Content} activationVersion={ActivationVersion} reason={Reason}",
                ShellNavigationContent.Start,
                activationToken,
                ActivationFaultReasons.StartNavigationException);
        }

        return false;
    }

    public async Task ActivateDiscoverSessionsAsync()
    {
        var activationToken = BeginActivation();
        try
        {
            CancelInFlightSessionActivation();
            var navigationResult = await NavigateToDiscoverSessionsAsync(activationToken).ConfigureAwait(true);
            if (!navigationResult.Succeeded)
            {
                _logger.LogWarning(
                    "Navigation activation failed. content={Content} activationVersion={ActivationVersion} reason={Reason}",
                    ShellNavigationContent.DiscoverSessions,
                    activationToken,
                    ActivationFaultReasons.DiscoverSessionsNavigationFailed);
                return;
            }

            if (IsLatestActivationToken(activationToken))
            {
                ClearPendingSessionPreviewState(activationToken);
                _runtimeState.CurrentShellContent = ShellNavigationContent.DiscoverSessions;
                _selectionSink.SetSelection(NavigationSelectionState.DiscoverSessionsSelection);
                return;
            }

            _logger.LogInformation(
                "Navigation activation ignored stale completion. content={Content} activationVersion={ActivationVersion} reason={Reason}",
                ShellNavigationContent.DiscoverSessions,
                activationToken,
                ActivationFaultReasons.SupersededBeforeCommit);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Navigation activation threw. content={Content} activationVersion={ActivationVersion} reason={Reason}",
                ShellNavigationContent.DiscoverSessions,
                activationToken,
                ActivationFaultReasons.DiscoverSessionsNavigationException);
        }
    }

    public async Task ActivateSettingsAsync(string settingsKey)
    {
        var activationToken = BeginActivation();
        var normalizedSettingsKey = string.IsNullOrWhiteSpace(settingsKey)
            ? SettingsSectionCatalog.GeneralKey
            : settingsKey;
        try
        {
            CancelInFlightSessionActivation();
            var navigationResult = await NavigateToSettingsAsync(
                    normalizedSettingsKey,
                    activationToken)
                .ConfigureAwait(true);
            if (!navigationResult.Succeeded)
            {
                _logger.LogWarning(
                    "Navigation activation failed. content={Content} settingsKey={SettingsKey} activationVersion={ActivationVersion} reason={Reason}",
                    ShellNavigationContent.Settings,
                    normalizedSettingsKey,
                    activationToken,
                    ActivationFaultReasons.SettingsNavigationFailed);
                return;
            }

            if (IsLatestActivationToken(activationToken))
            {
                ClearPendingSessionPreviewState(activationToken);
                _runtimeState.CurrentShellContent = ShellNavigationContent.Settings;
                _selectionSink.SetSelection(NavigationSelectionState.SettingsSelection);
                return;
            }

            _logger.LogInformation(
                "Navigation activation ignored stale completion. content={Content} settingsKey={SettingsKey} activationVersion={ActivationVersion} reason={Reason}",
                ShellNavigationContent.Settings,
                normalizedSettingsKey,
                activationToken,
                ActivationFaultReasons.SupersededBeforeCommit);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Navigation activation threw. content={Content} settingsKey={SettingsKey} activationVersion={ActivationVersion} reason={Reason}",
                ShellNavigationContent.Settings,
                normalizedSettingsKey,
                activationToken,
                ActivationFaultReasons.SettingsNavigationException);
        }
    }

    public Task<bool> ActivateSessionAsync(string sessionId, string? projectId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.FromResult(false);
        }

        CancellationTokenSource? previousActivation;
        bool previousActivationCanBeDisposed;
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
            previousActivationCanBeDisposed = _sessionActivationCtsCanBeDisposedByCanceler;
            var cancellationTokenSource = new CancellationTokenSource();
            request = new SessionActivationRequest(
                sessionId,
                projectId,
                BeginActivation(),
                cancellationTokenSource,
                cancellationTokenSource.Token);
            _sessionActivationCts = request.CancellationTokenSource;
            _sessionActivationCtsCanBeDisposedByCanceler = false;
            _runtimeState.DesiredSessionId = sessionId;
            _runtimeState.ActiveSessionActivationVersion = request.Version;
            _runtimeState.IsSessionActivationInProgress = true;
            _runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                request.SessionId,
                request.ProjectId,
                request.Version,
                SessionActivationPhase.NavigatingToChatShell);
        }

        if (TryCancelActivation(previousActivation))
        {
            _logger.LogInformation(
                "Navigation activation canceled previous request. sessionId={SessionId} version={Version}",
                sessionId,
                request.Version);
            if (previousActivationCanBeDisposed)
            {
                previousActivation?.Dispose();
            }
        }

        _logger.LogInformation(
            "Navigation activation started. sessionId={SessionId} version={Version}",
            sessionId,
            request.Version);
        return ActivateSessionCoreAsync(request);
    }

    public async Task<DiscoverRemoteSessionOpenResult> ActivateDiscoveredRemoteSessionAsync(
        DiscoverRemoteSessionOpenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RemoteSessionId))
        {
            return new DiscoverRemoteSessionOpenResult(false, null, "RemoteSessionIdMissing");
        }

        var chatService = _discoverConnectionFacade.CurrentChatService;
        if (chatService is not { IsConnected: true, IsInitialized: true })
        {
            return new DiscoverRemoteSessionOpenResult(false, null, "ACP 连接尚未完成初始化。");
        }

        if (chatService.AgentCapabilities?.SupportsSessionLoading != true)
        {
            return new DiscoverRemoteSessionOpenResult(false, null, "当前 Agent 未声明 ACP loadSession 能力，无法导入已发现的远程会话。");
        }

        var importRequest = BeginDiscoveredRemoteSessionImport(request.RemoteSessionId);
        var importActivationToken = importRequest.Version;
        DiscoverRemoteSessionOpenResult openResult;
        try
        {
            openResult = await _conversationSessionSwitcher
                .OpenDiscoveredRemoteSessionAsync(request, importRequest.CancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (
            importRequest.CancellationToken.IsCancellationRequested
            || !IsLatestActivationToken(importActivationToken))
        {
            EndDiscoveredRemoteSessionImport(importRequest);
            return new DiscoverRemoteSessionOpenResult(false, null, "DiscoverSessionOpenSuperseded");
        }
        catch (Exception ex)
        {
            EndDiscoveredRemoteSessionImport(importRequest);
            _logger.LogError(
                ex,
                "Discover remote session import failed. remoteSessionId={RemoteSessionId} activationVersion={ActivationVersion}",
                request.RemoteSessionId,
                importActivationToken);
            return new DiscoverRemoteSessionOpenResult(false, null, ex.Message);
        }

        if (!openResult.Succeeded || string.IsNullOrWhiteSpace(openResult.LocalConversationId))
        {
            EndDiscoveredRemoteSessionImport(importRequest);
            return openResult;
        }

        return await CompleteDiscoveredRemoteSessionImportAsync(
                request,
                openResult,
                importRequest)
            .ConfigureAwait(true);
    }

    private async Task<DiscoverRemoteSessionOpenResult> CompleteDiscoveredRemoteSessionImportAsync(
        DiscoverRemoteSessionOpenRequest request,
        DiscoverRemoteSessionOpenResult openResult,
        DiscoveredRemoteSessionImportRequest importRequest)
    {
        var importActivationToken = importRequest.Version;
        if (!IsLatestActivationToken(importActivationToken)
            || importRequest.CancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Discover remote session activation ignored stale completion. remoteSessionId={RemoteSessionId} activationVersion={ActivationVersion}",
                request.RemoteSessionId,
                importActivationToken);
            EndDiscoveredRemoteSessionImport(importRequest);
            await DiscardSupersededDiscoveredRemoteSessionAsync(
                    openResult.LocalConversationId!,
                    request.RemoteSessionId,
                    importActivationToken)
                .ConfigureAwait(true);
            return new DiscoverRemoteSessionOpenResult(false, openResult.LocalConversationId, "DiscoverSessionOpenSuperseded");
        }

        var activationRequest = PromoteDiscoveredRemoteSessionImport(
            importRequest,
            openResult.LocalConversationId!,
            projectId: null);
        var activated = await ActivateSessionCoreAsync(activationRequest).ConfigureAwait(true);
        if (!activated)
        {
            await DiscardSupersededDiscoveredRemoteSessionAsync(
                    openResult.LocalConversationId!,
                    request.RemoteSessionId,
                    importActivationToken)
                .ConfigureAwait(true);
            return new DiscoverRemoteSessionOpenResult(
                false,
                openResult.LocalConversationId,
                "加载会话并导入失败，请检查连接状态。");
        }

        return openResult;
    }

    private async Task<bool> ActivateSessionCoreAsync(SessionActivationRequest request)
    {
        var activationGateEntered = false;
        var committed = false;

        try
        {
            var navigationResult = await NavigateToChatAsync(request.Version).ConfigureAwait(true);
            if (!navigationResult.Succeeded
                || !IsLatestActivationToken(request.Version)
                || request.CancellationToken.IsCancellationRequested)
            {
                MarkSessionActivationFaulted(
                    request,
                    navigationResult.Succeeded
                        ? ActivationFaultReasons.SupersededBeforeChatShell
                        : ActivationFaultReasons.ChatShellNavigationFailed);
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
                MarkSessionActivationFaulted(request, ActivationFaultReasons.SupersededBeforeConversationSelection);
                return false;
            }

            var activated = await _conversationSessionSwitcher
                .SwitchConversationAsync(request.SessionId, request.CancellationToken)
                .ConfigureAwait(true);
            if (!activated || !IsLatestActivationToken(request.Version) || request.CancellationToken.IsCancellationRequested)
            {
                MarkSessionActivationFaulted(
                    request,
                    activated ? ActivationFaultReasons.SupersededAfterConversationSelection : ActivationFaultReasons.ConversationSelectionFailed);
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
            MarkSessionActivationFaulted(request, ActivationFaultReasons.Canceled);
            _logger.LogInformation(
                "Navigation activation canceled. sessionId={SessionId} version={Version} reason={Reason}",
                request.SessionId,
                request.Version,
                ActivationFaultReasons.Canceled);
            return false;
        }
        catch (Exception ex)
        {
            MarkSessionActivationFaulted(request, ex.GetType().Name);
            _logger.LogError(
                ex,
                "Navigation activation failed. sessionId={SessionId} version={Version} reason={Reason}",
                request.SessionId,
                request.Version,
                ex.GetType().Name);
        }
        finally
        {
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
        if (content == ShellNavigationContent.Chat
            && _runtimeState.ActiveSessionActivation is { } activeActivation
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

    private DiscoveredRemoteSessionImportRequest BeginDiscoveredRemoteSessionImport(string remoteSessionId)
    {
        CancellationTokenSource? previousActivation;
        bool previousActivationCanBeDisposed;
        var cancellationTokenSource = new CancellationTokenSource();
        long activationToken;
        lock (_sessionActivationSync)
        {
            previousActivation = _sessionActivationCts;
            previousActivationCanBeDisposed = _sessionActivationCtsCanBeDisposedByCanceler;
            activationToken = _runtimeState.LatestActivationToken + 1;
            _runtimeState.LatestActivationToken = activationToken;
            _sessionActivationCts = cancellationTokenSource;
            _sessionActivationCtsCanBeDisposedByCanceler = false;
            _runtimeState.DesiredSessionId = null;
            _runtimeState.IsSessionActivationInProgress = false;
            _runtimeState.ActiveSessionActivationVersion = 0;
            _runtimeState.ActiveSessionActivation = null;
        }

        if (TryCancelActivation(previousActivation))
        {
            _logger.LogInformation(
                "Navigation activation canceled previous request before discover import. remoteSessionId={RemoteSessionId} version={Version}",
                remoteSessionId,
                activationToken);
            if (previousActivationCanBeDisposed)
            {
                previousActivation?.Dispose();
            }
        }

        return new DiscoveredRemoteSessionImportRequest(
            activationToken,
            cancellationTokenSource,
            cancellationTokenSource.Token);
    }

    private SessionActivationRequest PromoteDiscoveredRemoteSessionImport(
        DiscoveredRemoteSessionImportRequest importRequest,
        string sessionId,
        string? projectId)
    {
        lock (_sessionActivationSync)
        {
            _runtimeState.DesiredSessionId = sessionId;
            _runtimeState.ActiveSessionActivationVersion = importRequest.Version;
            _runtimeState.IsSessionActivationInProgress = true;
            _runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                sessionId,
                projectId,
                importRequest.Version,
                SessionActivationPhase.NavigatingToChatShell);
        }

        _logger.LogInformation(
            "Navigation activation started. sessionId={SessionId} version={Version}",
            sessionId,
            importRequest.Version);
        return new SessionActivationRequest(
            sessionId,
            projectId,
            importRequest.Version,
            importRequest.CancellationTokenSource,
            importRequest.CancellationToken);
    }

    private void EndDiscoveredRemoteSessionImport(DiscoveredRemoteSessionImportRequest request)
    {
        lock (_sessionActivationSync)
        {
            if (ReferenceEquals(_sessionActivationCts, request.CancellationTokenSource))
            {
                _sessionActivationCts = null;
                _sessionActivationCtsCanBeDisposedByCanceler = false;
            }
        }

        request.CancellationTokenSource.Dispose();
    }

    private async Task DiscardSupersededDiscoveredRemoteSessionAsync(
        string localConversationId,
        string remoteSessionId,
        long activationToken)
    {
        try
        {
            await _conversationSessionSwitcher
                .DiscardDiscoveredRemoteSessionAsync(localConversationId, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to discard superseded discovered remote session import. localConversationId={LocalConversationId} remoteSessionId={RemoteSessionId} activationVersion={ActivationVersion}",
                localConversationId,
                remoteSessionId,
                activationToken);
        }
    }

    private void EndSessionActivationRequest(SessionActivationRequest request, bool committed)
    {
        var shouldDisposeRequestCts = !committed;
        lock (_sessionActivationSync)
        {
            if (!committed && ReferenceEquals(_sessionActivationCts, request.CancellationTokenSource))
            {
                _sessionActivationCts = null;
                _sessionActivationCtsCanBeDisposedByCanceler = false;
            }

            if (IsLatestActivationToken(request.Version))
            {
                if (committed)
                {
                    _runtimeState.DesiredSessionId = null;
                    _runtimeState.CommittedSessionId = request.SessionId;
                    _runtimeState.CommittedSessionActivationVersion = request.Version;
                    if (ReferenceEquals(_sessionActivationCts, request.CancellationTokenSource))
                    {
                        _sessionActivationCtsCanBeDisposedByCanceler = true;
                    }
                }
                else
                {
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
        var pendingActivationCanBeDisposed = false;
        lock (_sessionActivationSync)
        {
            if (!IsLatestActivationToken(activationToken))
            {
                return;
            }

            pendingActivation = _sessionActivationCts;
            pendingActivationCanBeDisposed = _sessionActivationCtsCanBeDisposedByCanceler;
            _sessionActivationCts = null;
            _sessionActivationCtsCanBeDisposedByCanceler = false;
            _runtimeState.DesiredSessionId = null;
            _runtimeState.IsSessionActivationInProgress = false;
            _runtimeState.ActiveSessionActivationVersion = 0;
            _runtimeState.ActiveSessionActivation = null;
        }

        if (TryCancelActivation(pendingActivation))
        {
            if (pendingActivationCanBeDisposed)
            {
                pendingActivation?.Dispose();
            }
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
        => phase is SessionActivationPhase.None or SessionActivationPhase.Hydrated or SessionActivationPhase.Faulted;

    private void CancelInFlightSessionActivation()
    {
        CancellationTokenSource? pendingActivation = null;
        var pendingActivationCanBeDisposed = false;
        lock (_sessionActivationSync)
        {
            pendingActivation = _sessionActivationCts;
            pendingActivationCanBeDisposed = _sessionActivationCtsCanBeDisposedByCanceler;
            _sessionActivationCts = null;
            _sessionActivationCtsCanBeDisposedByCanceler = false;
        }

        if (TryCancelActivation(pendingActivation))
        {
            if (pendingActivationCanBeDisposed)
            {
                pendingActivation?.Dispose();
            }
        }
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
        CancellationTokenSource CancellationTokenSource,
        CancellationToken CancellationToken);

    private sealed record DiscoveredRemoteSessionImportRequest(
        long Version,
        CancellationTokenSource CancellationTokenSource,
        CancellationToken CancellationToken);

    private static class ActivationFaultReasons
    {
        public const string Canceled = "Canceled";
        public const string ChatShellNavigationFailed = "ChatShellNavigationFailed";
        public const string ConversationSelectionFailed = "ConversationSelectionFailed";
        public const string DiscoverSessionsNavigationException = "DiscoverSessionsNavigationException";
        public const string DiscoverSessionsNavigationFailed = "DiscoverSessionsNavigationFailed";
        public const string SettingsNavigationException = "SettingsNavigationException";
        public const string SettingsNavigationFailed = "SettingsNavigationFailed";
        public const string StartNavigationException = "StartNavigationException";
        public const string StartNavigationFailed = "StartNavigationFailed";
        public const string SupersededAfterConversationSelection = "SupersededAfterConversationSelection";
        public const string SupersededBeforeChatShell = "SupersededBeforeChatShell";
        public const string SupersededBeforeCommit = "SupersededBeforeCommit";
        public const string SupersededBeforeConversationSelection = "SupersededBeforeConversationSelection";
    }

}

public interface IActivationTokenShellNavigationService
{
    ValueTask<ShellNavigationResult> NavigateToSettings(string key, long activationToken);
    ValueTask<ShellNavigationResult> NavigateToChat(long activationToken);
    ValueTask<ShellNavigationResult> NavigateToStart(long activationToken);
    ValueTask<ShellNavigationResult> NavigateToDiscoverSessions(long activationToken);
}
