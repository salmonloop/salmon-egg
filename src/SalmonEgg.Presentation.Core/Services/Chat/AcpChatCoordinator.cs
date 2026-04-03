using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Minimal ACP service lifecycle coordinator.
/// This slice extracts transport/profile/service seams so ChatViewModel can delegate incrementally.
/// </summary>
public sealed class AcpChatCoordinator : IAcpConnectionCommands
{
    private const int DefaultSessionUpdateBufferLimit = 256;

    private readonly IAcpChatServiceFactory _chatServiceFactory;
    private readonly IAcpConnectionCoordinator _connectionCoordinator;
    private readonly IAcpConnectionSessionRegistry _sessionRegistry;
    private readonly IAcpConnectionPoolManager _connectionPoolManager;
    private readonly IAcpSessionCommandOrchestrator _sessionCommandOrchestrator;
    private readonly ILogger<AcpChatCoordinator> _logger;
    private readonly int _sessionUpdateBufferLimit;
    private AcpChatServiceAdapter? _activeChatServiceAdapter;
    private readonly object _applyScopeLock = new();
    private CancellationTokenSource? _activeApplyScopeCts;

    public AcpChatCoordinator(
        IAcpChatServiceFactory chatServiceFactory,
        ILogger<AcpChatCoordinator> logger,
        IAcpConnectionCoordinator? connectionCoordinator = null,
        IAcpConnectionSessionRegistry? sessionRegistry = null,
        IAcpConnectionSessionCleaner? sessionCleaner = null,
        IAcpConnectionPoolManager? connectionPoolManager = null,
        IAcpSessionCommandOrchestrator? sessionCommandOrchestrator = null,
        int sessionUpdateBufferLimit = DefaultSessionUpdateBufferLimit)
    {
        _chatServiceFactory = chatServiceFactory ?? throw new ArgumentNullException(nameof(chatServiceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (sessionUpdateBufferLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sessionUpdateBufferLimit),
                "Session update buffer limit must be positive.");
        }

        _connectionCoordinator = connectionCoordinator ?? NoopAcpConnectionCoordinator.Instance;
        _sessionRegistry = sessionRegistry ?? new InMemoryAcpConnectionSessionRegistry();
        var cleaner = sessionCleaner ?? new AcpConnectionSessionCleaner(
            _sessionRegistry,
            new ConservativeAcpConnectionEvictionPolicy(new AcpConnectionEvictionOptions()),
            new AcpConnectionEvictionOptions(),
            NullLogger<AcpConnectionSessionCleaner>.Instance);
        _connectionPoolManager = connectionPoolManager ?? new AcpConnectionPoolManager(
            _sessionRegistry,
            cleaner,
            NullLogger<AcpConnectionPoolManager>.Instance);
        _sessionCommandOrchestrator = sessionCommandOrchestrator ?? new AcpSessionCommandOrchestrator(
            NullLogger<AcpSessionCommandOrchestrator>.Instance);
        _sessionUpdateBufferLimit = sessionUpdateBufferLimit;
    }

    public async Task<AcpTransportApplyResult> ConnectToProfileAsync(
        ServerConfiguration profile,
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transportConfiguration);
        ArgumentNullException.ThrowIfNull(sink);

        var preserveConversation = sink.IsSessionActive && !string.IsNullOrWhiteSpace(sink.CurrentSessionId);
        return await ConnectToProfileAsync(
            profile,
            transportConfiguration,
            sink,
            new AcpConnectionContext(sink.CurrentSessionId, preserveConversation),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AcpTransportApplyResult> ConnectToProfileAsync(
        ServerConfiguration profile,
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        AcpConnectionContext connectionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transportConfiguration);
        ArgumentNullException.ThrowIfNull(sink);

        await sink.SelectProfileAsync(profile, cancellationToken).ConfigureAwait(false);
        ApplyProfileToTransportConfiguration(profile, transportConfiguration);

        return await ApplyTransportConfigurationAsync(
            transportConfiguration,
            sink,
            connectionContext,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AcpTransportApplyResult> ApplyTransportConfigurationAsync(
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        bool preserveConversation,
        CancellationToken cancellationToken = default)
        => await ApplyTransportConfigurationAsync(
            transportConfiguration,
            sink,
            new AcpConnectionContext(sink.CurrentSessionId, preserveConversation),
            cancellationToken).ConfigureAwait(false);

    public async Task<AcpTransportApplyResult> ApplyTransportConfigurationAsync(
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        AcpConnectionContext connectionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transportConfiguration);
        ArgumentNullException.ThrowIfNull(sink);
        cancellationToken.ThrowIfCancellationRequested();

        var (isValid, errorMessage) = transportConfiguration.Validate();
        if (!isValid)
        {
            await _connectionCoordinator.SetDisconnectedAsync(errorMessage, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(errorMessage ?? "Invalid ACP transport configuration.");
        }

        var selectedProfileId = sink.SelectedProfileId;
        var cleanupResult = await _connectionPoolManager
            .CleanupBeforeApplyAsync(
                sink.CurrentChatService,
                selectedProfileId,
                cancellationToken)
            .ConfigureAwait(false);
        if (cleanupResult.RemovedCount > 0 || cleanupResult.DisposeFailureCount > 0)
        {
            _logger.LogDebug(
                "Pruned stale cached ACP sessions before apply. removedCount={RemovedCount} disposeFailureCount={DisposeFailureCount}",
                cleanupResult.RemovedCount,
                cleanupResult.DisposeFailureCount);
        }

        using var applyScope = EnterApplyScope(cancellationToken);
        var applyToken = applyScope.Token;
        var currentConnectionReuseKey = BuildConnectionReuseKey(transportConfiguration);

        var previousConnectionState = CaptureConnectionState(sink);
        await _connectionCoordinator.SetConnectingAsync(sink.SelectedProfileId, applyToken).ConfigureAwait(false);

        if (_connectionPoolManager.TryGetReusableSession(
                selectedProfileId,
                currentConnectionReuseKey,
                out var cachedSession))
        {
            applyToken.ThrowIfCancellationRequested();

            var currentService = sink.CurrentChatService;
            await sink.ReplaceChatServiceAsync(cachedSession.Service, applyToken).ConfigureAwait(false);
            _activeChatServiceAdapter = cachedSession.Service;
            sink.UpdateAgentIdentity(
                cachedSession.InitializeResponse.AgentInfo?.Name,
                cachedSession.InitializeResponse.AgentInfo?.Version);
            await _connectionCoordinator.SetConnectedAsync(selectedProfileId, applyToken).ConfigureAwait(false);
            await _connectionCoordinator.ClearAuthenticationRequiredAsync(applyToken).ConfigureAwait(false);

            if (currentService != null
                && !ReferenceEquals(currentService, cachedSession.Service)
                && !ShouldKeepServiceAlive(currentService, selectedProfileId))
            {
                await DisconnectServiceQuietlyAsync(currentService).ConfigureAwait(false);
                if (currentService != null)
                {
                    _connectionPoolManager.RemoveByService(currentService, out _);
                }
            }

            var hasExistingRemoteBinding = await HasExistingRemoteBindingAsync(
                sink,
                connectionContext,
                applyToken).ConfigureAwait(false);
            if (connectionContext.PreserveConversation
                && hasExistingRemoteBinding
                && cachedSession.Service.AgentCapabilities?.LoadSession == true)
            {
                await _connectionCoordinator.ResyncAsync(sink, applyToken).ConfigureAwait(false);
            }
            else
            {
                await TryMarkHydratedForConnectionContextAsync(
                    sink,
                    cachedSession.Service,
                    connectionContext,
                    applyToken).ConfigureAwait(false);
            }

            return new AcpTransportApplyResult(cachedSession.Service, cachedSession.InitializeResponse);
        }

        var previousService = sink.CurrentChatService;
        IChatService? candidateService = null;
        AcpChatServiceAdapter? wrappedService = null;
        var committed = false;
        try
        {
            candidateService = _chatServiceFactory.CreateChatService(
                transportConfiguration.SelectedTransportType,
                transportConfiguration.SelectedTransportType == TransportType.Stdio ? transportConfiguration.StdioCommand : null,
                transportConfiguration.SelectedTransportType == TransportType.Stdio ? transportConfiguration.StdioArgs : null,
                transportConfiguration.SelectedTransportType == TransportType.Stdio ? null : transportConfiguration.RemoteUrl);
            _logger.LogInformation(
                "ACP candidate created. transport={TransportType} conversationId={ConversationId} preserveConversation={PreserveConversation}",
                transportConfiguration.SelectedTransportType,
                connectionContext.ConversationId,
                connectionContext.PreserveConversation);
            applyToken.ThrowIfCancellationRequested();

            wrappedService = WrapChatService(candidateService, sink, applyToken);
            await _connectionCoordinator.SetInitializingAsync(sink.SelectedProfileId, applyToken).ConfigureAwait(false);

            var initializeResponse = await wrappedService
                .InitializeAsync(CreateDefaultInitializeParams())
                .ConfigureAwait(false);
            _logger.LogInformation(
                "ACP candidate initialized. transport={TransportType} conversationId={ConversationId}",
                transportConfiguration.SelectedTransportType,
                connectionContext.ConversationId);
            applyToken.ThrowIfCancellationRequested();

            await sink.ReplaceChatServiceAsync(wrappedService, applyToken).ConfigureAwait(false);
            _activeChatServiceAdapter = wrappedService;
            committed = true;
            if (!ShouldKeepServiceAlive(previousService, selectedProfileId))
            {
                await DisconnectServiceQuietlyAsync(previousService).ConfigureAwait(false);
                if (previousService != null)
                {
                    _connectionPoolManager.RemoveByService(previousService, out _);
                }
            }

            sink.UpdateAgentIdentity(initializeResponse.AgentInfo?.Name, initializeResponse.AgentInfo?.Version);
            if (!string.IsNullOrWhiteSpace(selectedProfileId))
            {
                _connectionPoolManager.RecordSession(
                    selectedProfileId!,
                    wrappedService,
                    initializeResponse,
                    currentConnectionReuseKey);
            }
            await _connectionCoordinator.SetConnectedAsync(sink.SelectedProfileId, applyToken).ConfigureAwait(false);
            await _connectionCoordinator.ClearAuthenticationRequiredAsync(applyToken).ConfigureAwait(false);

            var hasExistingRemoteBinding = await HasExistingRemoteBindingAsync(
                sink,
                connectionContext,
                applyToken).ConfigureAwait(false);
            if (connectionContext.PreserveConversation
                && hasExistingRemoteBinding
                && wrappedService.AgentCapabilities?.LoadSession == true)
            {
                await _connectionCoordinator.ResyncAsync(sink, applyToken).ConfigureAwait(false);
            }
            else
            {
                await TryMarkHydratedForConnectionContextAsync(
                    sink,
                    wrappedService,
                    connectionContext,
                    applyToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "ACP candidate committed. transport={TransportType} conversationId={ConversationId} preserveConversation={PreserveConversation}",
                transportConfiguration.SelectedTransportType,
                connectionContext.ConversationId,
                connectionContext.PreserveConversation);

            return new AcpTransportApplyResult(wrappedService, initializeResponse);
        }
        catch (OperationCanceledException)
        {
            if (!committed)
            {
                await DisposeServiceAsync(candidateService).ConfigureAwait(false);
                wrappedService?.SuppressBufferedUpdates("ApplySupersededBeforeCommit");

                if (applyScope.IsSuperseded(cancellationToken))
                {
                    _logger.LogInformation(
                        "ACP candidate superseded before commit. transport={TransportType} conversationId={ConversationId}",
                        transportConfiguration.SelectedTransportType,
                        connectionContext.ConversationId);
                }
                else
                {
                    await RestoreConnectionStateAfterDiscardAsync(previousConnectionState).ConfigureAwait(false);
                    _logger.LogInformation(
                        "ACP candidate discarded before commit. transport={TransportType} conversationId={ConversationId} restoredPhase={RestoredPhase} restoredProfileId={RestoredProfileId}",
                        transportConfiguration.SelectedTransportType,
                        connectionContext.ConversationId,
                        previousConnectionState.PhaseName,
                        previousConnectionState.SelectedProfileId);
                }
            }

            throw;
        }
        catch (Exception ex)
        {
            if (!committed)
            {
                await DisposeServiceAsync(candidateService).ConfigureAwait(false);
                wrappedService?.SuppressBufferedUpdates("ApplySupersededBeforeCommitError");
                if (applyScope.IsSuperseded(cancellationToken))
                {
                    _logger.LogInformation(
                        ex,
                        "ACP candidate superseded before commit after fault. transport={TransportType} conversationId={ConversationId}",
                        transportConfiguration.SelectedTransportType,
                        connectionContext.ConversationId);
                }
                else
                {
                    await _connectionCoordinator.SetDisconnectedAsync(ex.Message, cancellationToken).ConfigureAwait(false);
                    _logger.LogError(ex, "Failed to initialize ACP candidate before commit");
                }
                throw;
            }

            try
            {
                await DisposeServiceAsync(sink.CurrentChatService).ConfigureAwait(false);
                if (sink.CurrentChatService != null)
                {
                    _connectionPoolManager.RemoveByService(sink.CurrentChatService, out _);
                }
            }
            catch (Exception disconnectEx)
            {
                _logger.LogDebug(disconnectEx, "Failed to tear down ACP service after initialization error");
            }

            await sink.ReplaceChatServiceAsync(null, cancellationToken).ConfigureAwait(false);
            _activeChatServiceAdapter = null;
            sink.UpdateAgentIdentity(null, null);
            await _connectionCoordinator.SetDisconnectedAsync(ex.Message, cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to apply ACP transport configuration");
            throw;
        }
    }

    public async Task<AcpRemoteSessionResult> EnsureRemoteSessionAsync(
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default)
    {
        return await _sessionCommandOrchestrator.EnsureRemoteSessionAsync(
                sink,
                authenticateAsync,
                () => _activeChatServiceAdapter?.MarkHydrated(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AcpPromptDispatchResult> SendPromptAsync(
        string promptText,
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default)
    {
        return await _sessionCommandOrchestrator.SendPromptAsync(
                promptText,
                sink,
                authenticateAsync,
                (targetSink, auth, markHydrated, token) => _sessionCommandOrchestrator.EnsureRemoteSessionAsync(
                    targetSink,
                    auth,
                    markHydrated,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AcpPromptDispatchResult> DispatchPromptToRemoteSessionAsync(
        string remoteSessionId,
        string promptText,
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default)
    {
        return await _sessionCommandOrchestrator.DispatchPromptToRemoteSessionAsync(
                remoteSessionId,
                promptText,
                sink,
                authenticateAsync,
                (targetSink, auth, markHydrated, token) => _sessionCommandOrchestrator.EnsureRemoteSessionAsync(
                    targetSink,
                    auth,
                    markHydrated,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CancelPromptAsync(
        IAcpChatCoordinatorSink sink,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await _sessionCommandOrchestrator.CancelPromptAsync(sink, reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        cancellationToken.ThrowIfCancellationRequested();

        var chatService = sink.CurrentChatService;
        if (chatService != null)
        {
            await chatService.DisconnectAsync().ConfigureAwait(false);
            if (chatService is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _connectionPoolManager.RemoveByService(chatService, out _);
        }

        await sink.ReplaceChatServiceAsync(null, cancellationToken).ConfigureAwait(false);
        _activeChatServiceAdapter = null;
        await ClearBindingForCurrentConversationAsync(sink).ConfigureAwait(false);
        sink.UpdateAgentIdentity(null, null);
        await _connectionCoordinator.ResetAsync(cancellationToken).ConfigureAwait(false);
    }

    private AcpChatServiceAdapter WrapChatService(
        IChatService chatService,
        IAcpChatCoordinatorSink sink,
        CancellationToken applyScopeToken)
    {
        ArgumentNullException.ThrowIfNull(chatService);
        ArgumentNullException.ThrowIfNull(sink);

        AcpChatServiceAdapter? wrappedService = null;
        var eventAdapter = new AcpEventAdapter(
            update => wrappedService!.PublishBufferedUpdate(update),
            sink.SessionUpdateSynchronizationContext,
            _sessionUpdateBufferLimit,
            resyncRequired: sourceSessionId => _ = HandleResyncRequiredAsync(
                sink,
                wrappedService!,
                sourceSessionId,
                applyScopeToken));
        wrappedService = new AcpChatServiceAdapter(chatService, eventAdapter);
        return wrappedService;
    }

    private async Task HandleResyncRequiredAsync(
        IAcpChatCoordinatorSink sink,
        AcpChatServiceAdapter sourceService,
        string? sourceSessionId,
        CancellationToken applyScopeToken)
    {
        if (applyScopeToken.IsCancellationRequested)
        {
            sourceService.SuppressBufferedUpdates("StaleApplyScope");
            _logger.LogDebug("Ignoring ACP resync request from stale apply scope.");
            return;
        }

        if (!ReferenceEquals(sink.CurrentChatService, sourceService))
        {
            _logger.LogDebug("Ignoring ACP resync request from stale chat service instance.");
            return;
        }

        var currentBinding = await sink.GetCurrentRemoteBindingAsync(applyScopeToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sourceSessionId)
            || !string.Equals(currentBinding?.RemoteSessionId, sourceSessionId, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Ignoring ACP resync request because the active binding no longer targets the source session. sourceSessionId={SourceSessionId} activeRemoteSessionId={ActiveRemoteSessionId}",
                sourceSessionId,
                currentBinding?.RemoteSessionId);
            return;
        }

        _logger.LogWarning(
            "ACP update stream requested resync. remoteSessionId={RemoteSessionId}",
            currentBinding?.RemoteSessionId);

        await _connectionCoordinator.ResyncAsync(sink, applyScopeToken).ConfigureAwait(false);
    }

    private static async Task<bool> HasExistingRemoteBindingAsync(
        IAcpChatCoordinatorSink sink,
        AcpConnectionContext connectionContext,
        CancellationToken cancellationToken)
    {
        if (!connectionContext.HasConversationTarget)
        {
            return false;
        }

        var binding = await sink
            .GetConversationRemoteBindingAsync(connectionContext.ConversationId!, cancellationToken)
            .ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(binding?.RemoteSessionId);
    }

    private static async Task UpdateBindingForCurrentConversationAsync(
        IAcpChatCoordinatorSink sink,
        string remoteSessionId,
        string? profileId)
    {
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            throw new ArgumentException("Remote session id must not be empty.", nameof(remoteSessionId));
        }

        if (string.IsNullOrWhiteSpace(sink.CurrentSessionId))
        {
            throw new InvalidOperationException("Cannot update remote binding without an active local conversation.");
        }

        var result = await sink.ConversationBindingCommands
            .UpdateBindingAsync(
                sink.CurrentSessionId!,
                remoteSessionId,
                profileId)
            .ConfigureAwait(false);

        if (result.Status is not BindingUpdateStatus.Success)
        {
            throw new InvalidOperationException(
                $"Failed to update conversation binding ({result.Status}): {result.ErrorMessage ?? "UnknownError"}");
        }
    }

    private static async Task ClearBindingForCurrentConversationAsync(IAcpChatCoordinatorSink sink)
    {
        if (string.IsNullOrWhiteSpace(sink.CurrentSessionId))
        {
            return;
        }

        await sink.ConversationBindingCommands
            .UpdateBindingAsync(
                sink.CurrentSessionId!,
                remoteSessionId: null,
                sink.SelectedProfileId)
            .ConfigureAwait(false);
    }

    private static async Task TryMarkHydratedForConnectionContextAsync(
        IAcpChatCoordinatorSink sink,
        AcpChatServiceAdapter wrappedService,
        AcpConnectionContext connectionContext,
        CancellationToken cancellationToken)
    {
        if (connectionContext.HasConversationTarget)
        {
            var binding = await sink
                .GetConversationRemoteBindingAsync(connectionContext.ConversationId!, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(binding?.RemoteSessionId)
                && sink.IsSessionActive
                && string.Equals(binding.ConversationId, sink.CurrentSessionId, StringComparison.Ordinal))
            {
                wrappedService.MarkHydrated();
                return;
            }
        }

        if (sink.ConnectionGeneration > 0)
        {
            wrappedService.MarkHydrated(lowTrust: true, reason: "ConnectionGenerationAdvanced");
        }
    }

    private async Task DisconnectServiceQuietlyAsync(IChatService? service)
    {
        if (service == null)
        {
            return;
        }

        try
        {
            await DisposeServiceAsync(service).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to disconnect previous ACP service during transport replacement");
        }
    }

    private async Task RestoreConnectionStateAfterDiscardAsync(AcpConnectionStateSnapshot snapshot)
    {
        try
        {
            if (snapshot.IsConnected)
            {
                await _connectionCoordinator.SetConnectedAsync(snapshot.SelectedProfileId, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }

            if (snapshot.IsInitializing)
            {
                await _connectionCoordinator.SetInitializingAsync(snapshot.SelectedProfileId, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }

            if (snapshot.IsConnecting)
            {
                await _connectionCoordinator.SetConnectingAsync(snapshot.SelectedProfileId, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }

            await _connectionCoordinator.SetDisconnectedAsync(snapshot.ErrorMessage, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to restore ACP connection state after candidate discard. restoredPhase={RestoredPhase} restoredProfileId={RestoredProfileId}",
                snapshot.PhaseName,
                snapshot.SelectedProfileId);
        }
    }

    private static AcpConnectionStateSnapshot CaptureConnectionState(IAcpChatCoordinatorSink sink)
        => new(
            sink.SelectedProfileId,
            sink.IsConnecting,
            sink.IsInitializing,
            sink.IsConnected,
            sink.ConnectionErrorMessage);

    private static async Task DisposeServiceAsync(IChatService? service)
    {
        if (service == null)
        {
            return;
        }

        await service.DisconnectAsync().ConfigureAwait(false);
        if (service is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private ApplyScope EnterApplyScope(CancellationToken callerToken)
    {
        var scopeCts = new CancellationTokenSource();
        CancellationTokenSource? previousScope;
        lock (_applyScopeLock)
        {
            previousScope = _activeApplyScopeCts;
            _activeApplyScopeCts = scopeCts;
        }

        previousScope?.Cancel();
        previousScope?.Dispose();

        return new ApplyScope(this, scopeCts, callerToken);
    }

    private bool ShouldKeepServiceAlive(IChatService? service, string? targetProfileId)
    {
        if (service == null
            || string.IsNullOrWhiteSpace(targetProfileId))
        {
            return false;
        }

        return _sessionRegistry.TryGetProfileId(service, out var currentProfileId)
            && !string.Equals(currentProfileId, targetProfileId, StringComparison.Ordinal);
    }

    private static AcpConnectionReuseKey BuildConnectionReuseKey(IAcpTransportConfiguration transportConfiguration)
        => AcpConnectionReuseKey.FromTransportConfiguration(transportConfiguration);

    private sealed class ApplyScope : IDisposable
    {
        private readonly AcpChatCoordinator _owner;
        private readonly CancellationTokenSource _scopeCts;
        private readonly CancellationTokenSource _linkedCts;

        public ApplyScope(AcpChatCoordinator owner, CancellationTokenSource scopeCts, CancellationToken callerToken)
        {
            _owner = owner;
            _scopeCts = scopeCts;
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, scopeCts.Token);
        }

        public CancellationToken Token => _linkedCts.Token;

        public bool IsSuperseded(CancellationToken callerToken)
            => _scopeCts.IsCancellationRequested && !callerToken.IsCancellationRequested;

        public void Dispose()
        {
            lock (_owner._applyScopeLock)
            {
                if (ReferenceEquals(_owner._activeApplyScopeCts, _scopeCts))
                {
                    _owner._activeApplyScopeCts = null;
                }
            }

            _linkedCts.Dispose();
            _scopeCts.Dispose();
        }
    }

    private static void ApplyProfileToTransportConfiguration(
        ServerConfiguration profile,
        IAcpTransportConfiguration transportConfiguration)
    {
        transportConfiguration.SelectedTransportType = profile.Transport;

        if (profile.Transport == TransportType.Stdio)
        {
            transportConfiguration.StdioCommand = profile.StdioCommand ?? string.Empty;
            transportConfiguration.StdioArgs = profile.StdioArgs ?? string.Empty;
            transportConfiguration.RemoteUrl = string.Empty;
            return;
        }

        transportConfiguration.RemoteUrl = profile.ServerUrl ?? string.Empty;
        transportConfiguration.StdioCommand = string.Empty;
        transportConfiguration.StdioArgs = string.Empty;
    }

    private static IChatService RequireReadyChatService(IAcpChatCoordinatorSink sink)
    {
        if (sink.CurrentChatService is not { IsConnected: true, IsInitialized: true } chatService)
        {
            throw new InvalidOperationException("ACP chat service is not connected and initialized.");
        }

        return chatService;
    }

    private readonly record struct AcpConnectionStateSnapshot(
        string? SelectedProfileId,
        bool IsConnecting,
        bool IsInitializing,
        bool IsConnected,
        string? ErrorMessage)
    {
        public string PhaseName =>
            IsConnected ? "Connected" :
            IsInitializing ? "Initializing" :
            IsConnecting ? "Connecting" :
            "Disconnected";
    }

    private sealed class NoopAcpConnectionCoordinator : IAcpConnectionCoordinator
    {
        public static NoopAcpConnectionCoordinator Instance { get; } = new();

        public Task SetConnectingAsync(string? profileId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetInitializingAsync(string? profileId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetConnectedAsync(string? profileId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetDisconnectedAsync(string? errorMessage = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetAuthenticationRequiredAsync(string? hintMessage, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearAuthenticationRequiredAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ResetAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ResyncAsync(IAcpChatCoordinatorSink sink, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static InitializeParams CreateDefaultInitializeParams()
        => AcpInitializeRequestFactory.CreateDefault();
}
