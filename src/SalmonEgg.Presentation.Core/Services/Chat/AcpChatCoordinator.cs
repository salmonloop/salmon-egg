using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;

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
    private readonly IAcpConnectionDependencySnapshotProvider _connectionDependencySnapshotProvider;
    private readonly IAcpSessionCommandOrchestrator _sessionCommandOrchestrator;
    private readonly IAcpMcpServerProvider _mcpServerProvider;
    private readonly ITransportSupportPolicy _transportSupportPolicy;
    private readonly ILogger<AcpChatCoordinator> _logger;
    private readonly int _sessionUpdateBufferLimit;
    private AcpChatServiceAdapter? _activeChatServiceAdapter;
    private readonly object _applyScopeLock = new();
    private readonly object _poolConnectionGateSync = new();
    private readonly Dictionary<PoolConnectionRequestKey, PoolConnectionRequestGate> _poolConnectionGates = new();
    private readonly Dictionary<string, long> _poolProfileDisconnectGenerations = new(StringComparer.Ordinal);
    private CancellationTokenSource? _activeApplyScopeCts;

    public AcpChatCoordinator(
        IAcpChatServiceFactory chatServiceFactory,
        ILogger<AcpChatCoordinator> logger,
        ITransportSupportPolicy transportSupportPolicy,
        IAcpMcpServerProvider mcpServerProvider,
        IAcpSessionCommandOrchestrator sessionCommandOrchestrator,
        IAcpConnectionCoordinator? connectionCoordinator = null,
        IAcpConnectionSessionRegistry? sessionRegistry = null,
        IAcpConnectionSessionCleaner? sessionCleaner = null,
        IAcpConnectionPoolManager? connectionPoolManager = null,
        IAcpConnectionDependencySnapshotProvider? connectionDependencySnapshotProvider = null,
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
        _connectionDependencySnapshotProvider = connectionDependencySnapshotProvider
            ?? NoopAcpConnectionDependencySnapshotProvider.Instance;
        _mcpServerProvider = mcpServerProvider ?? throw new ArgumentNullException(nameof(mcpServerProvider));
        _sessionCommandOrchestrator = sessionCommandOrchestrator
            ?? throw new ArgumentNullException(nameof(sessionCommandOrchestrator));
        _transportSupportPolicy = transportSupportPolicy ?? throw new ArgumentNullException(nameof(transportSupportPolicy));
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

        EnsureTransportSupported(profile.Transport);
        sink.SetCurrentMcpServers(
            await _mcpServerProvider.GetMcpServersAsync(cancellationToken).ConfigureAwait(false));
        await sink.SelectProfileAsync(profile, cancellationToken).ConfigureAwait(false);
        ApplyProfileToTransportConfiguration(profile, transportConfiguration);

        return await ApplyTransportConfigurationCoreAsync(
            transportConfiguration,
            sink,
            connectionContext,
            profileForServiceCreation: profile,
            profile.Id,
            ResolveInitializeTimeout(profile),
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
        => await ApplyTransportConfigurationCoreAsync(
            transportConfiguration,
            sink,
            connectionContext,
            profileForServiceCreation: null,
            selectedProfileIdOverride: null,
            ResolveInitializeTimeout(profile: null),
            cancellationToken).ConfigureAwait(false);

    private async Task<AcpTransportApplyResult> ApplyTransportConfigurationCoreAsync(
        IAcpTransportConfiguration transportConfiguration,
        IAcpChatCoordinatorSink sink,
        AcpConnectionContext connectionContext,
        ServerConfiguration? profileForServiceCreation,
        string? selectedProfileIdOverride,
        TimeSpan initializeTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transportConfiguration);
        ArgumentNullException.ThrowIfNull(sink);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureTransportSupported(transportConfiguration.SelectedTransportType);

        var (isValid, errorMessage) = transportConfiguration.Validate();
        if (!isValid)
        {
            await _connectionCoordinator.SetConnectionInstanceIdAsync(null, cancellationToken).ConfigureAwait(false);
            await _connectionCoordinator.SetDisconnectedAsync(errorMessage, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(errorMessage ?? "Invalid ACP transport configuration.");
        }

        var selectedProfileId = string.IsNullOrWhiteSpace(selectedProfileIdOverride)
            ? sink.SelectedProfileId
            : selectedProfileIdOverride;
        var dependencySnapshot = await _connectionDependencySnapshotProvider
            .GetSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        var cleanupResult = await _connectionPoolManager
            .CleanupBeforeApplyAsync(
                sink.CurrentChatService,
                dependencySnapshot,
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

        var previousConnectionState = await CaptureConnectionStateAsync(sink, applyToken).ConfigureAwait(false);
        await _connectionCoordinator.SetConnectingAsync(selectedProfileId, applyToken).ConfigureAwait(false);
        var replaceIntent = connectionContext.PreserveConversation
            ? ServiceReplaceIntent.PoolOnly
            : ServiceReplaceIntent.ForegroundOwner;

        if (_connectionPoolManager.TryGetReusableSession(
                selectedProfileId,
                currentConnectionReuseKey,
                out var cachedSession))
        {
            applyToken.ThrowIfCancellationRequested();

            var currentService = sink.CurrentChatService;
            await sink.ReplaceChatServiceAsync(cachedSession.Service, replaceIntent, applyToken).ConfigureAwait(false);
            _activeChatServiceAdapter = cachedSession.Service;
            sink.UpdateAgentIdentity(
                ResolveDisplayAgentName(cachedSession.InitializeResponse.AgentInfo),
                cachedSession.InitializeResponse.AgentInfo?.Version);
            await _connectionCoordinator.SetConnectionInstanceIdAsync(
                cachedSession.ConnectionInstanceId,
                applyToken).ConfigureAwait(false);
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

            await TryMarkHydratedForConnectionContextAsync(
                sink,
                cachedSession.Service,
                connectionContext,
                applyToken).ConfigureAwait(false);

            return new AcpTransportApplyResult(cachedSession.Service, cachedSession.InitializeResponse);
        }

        var previousService = sink.CurrentChatService;
        IChatService? candidateService = null;
        AcpChatServiceAdapter? wrappedService = null;
        var committed = false;
        var connectionInstanceId = CreateConnectionInstanceId();
        try
        {
            candidateService = CreateCandidateChatService(transportConfiguration, profileForServiceCreation);
            _logger.LogInformation(
                "ACP candidate created. transport={TransportType} conversationId={ConversationId} preserveConversation={PreserveConversation}",
                transportConfiguration.SelectedTransportType,
                connectionContext.ConversationId,
                connectionContext.PreserveConversation);
            applyToken.ThrowIfCancellationRequested();

            wrappedService = WrapChatService(candidateService, sink, applyToken);
            await _connectionCoordinator.SetInitializingAsync(selectedProfileId, applyToken).ConfigureAwait(false);

            var initializeResponse = await InitializeCandidateAsync(
                    wrappedService,
                    transportConfiguration.SelectedTransportType,
                    selectedProfileId,
                    connectionContext.ConversationId,
                    initializeTimeout,
                    applyToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "ACP candidate initialized. transport={TransportType} conversationId={ConversationId}",
                transportConfiguration.SelectedTransportType,
                connectionContext.ConversationId);
            applyToken.ThrowIfCancellationRequested();

            await sink.ReplaceChatServiceAsync(wrappedService, replaceIntent, applyToken).ConfigureAwait(false);
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

            sink.UpdateAgentIdentity(ResolveDisplayAgentName(initializeResponse.AgentInfo), initializeResponse.AgentInfo?.Version);
            await _connectionCoordinator.SetConnectionInstanceIdAsync(connectionInstanceId, applyToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(selectedProfileId))
            {
                var replacedSession = _connectionPoolManager.RecordSession(
                    selectedProfileId!,
                    wrappedService,
                    initializeResponse,
                    currentConnectionReuseKey,
                    connectionInstanceId);
                await DisposeReplacedSessionAsync(replacedSession, wrappedService).ConfigureAwait(false);
            }
            await _connectionCoordinator.SetConnectedAsync(selectedProfileId, applyToken).ConfigureAwait(false);
            await _connectionCoordinator.ClearAuthenticationRequiredAsync(applyToken).ConfigureAwait(false);

            await TryMarkHydratedForConnectionContextAsync(
                sink,
                wrappedService,
                connectionContext,
                applyToken).ConfigureAwait(false);

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
                wrappedService?.SuppressAllBufferedUpdates("ApplySupersededBeforeCommit");

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
                wrappedService?.SuppressAllBufferedUpdates("ApplySupersededBeforeCommitError");
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
                    await _connectionCoordinator.SetConnectionInstanceIdAsync(null, cancellationToken).ConfigureAwait(false);
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
            await _connectionCoordinator.SetConnectionInstanceIdAsync(null, cancellationToken).ConfigureAwait(false);
            await _connectionCoordinator.SetDisconnectedAsync(ex.Message, cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to apply ACP transport configuration");
            throw;
        }
    }

    private IChatService CreateCandidateChatService(
        IAcpTransportConfiguration transportConfiguration,
        ServerConfiguration? profileForServiceCreation)
    {
        if (profileForServiceCreation is not null)
        {
            return _chatServiceFactory.CreateChatService(profileForServiceCreation);
        }

        return _chatServiceFactory.CreateChatService(
            transportConfiguration.SelectedTransportType,
            transportConfiguration.SelectedTransportType == TransportType.Stdio ? transportConfiguration.StdioCommand : null,
            transportConfiguration.SelectedTransportType == TransportType.Stdio ? transportConfiguration.StdioArguments : null,
            transportConfiguration.SelectedTransportType == TransportType.Stdio ? null : transportConfiguration.RemoteUrl);
    }

    public async Task<AcpRemoteSessionResult> EnsureRemoteSessionAsync(
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default)
    {
        var adapter = sink.CurrentChatService as AcpChatServiceAdapter;
        return await _sessionCommandOrchestrator.EnsureRemoteSessionAsync(
                sink,
                authenticateAsync,
                () => adapter?.ReleaseUnscopedBufferedUpdates(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AcpPromptDispatchResult> SendPromptAsync(
        string promptText,
        string? promptMessageId,
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default)
    {
        return await _sessionCommandOrchestrator.SendPromptAsync(
                promptText,
                promptMessageId,
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
        string? promptMessageId,
        IAcpChatCoordinatorSink sink,
        Func<CancellationToken, Task<bool>> authenticateAsync,
        CancellationToken cancellationToken = default)
    {
        return await _sessionCommandOrchestrator.DispatchPromptToRemoteSessionAsync(
                remoteSessionId,
                promptText,
                promptMessageId,
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
        CancellationToken cancellationToken = default)
    {
        await _sessionCommandOrchestrator.CancelPromptAsync(sink, cancellationToken).ConfigureAwait(false);
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
            try
            {
                chatService.Dispose();
            }
            catch (Exception ex)
            {
                // 释放失败不得中断后续 pool/sink/连接态的收尾,否则留下半拆的协调器状态。
                _logger.LogDebug(ex, "Failed to dispose ACP chat service cleanly during disconnect.");
            }

            _connectionPoolManager.RemoveByService(chatService, out _);
        }

        await sink.ReplaceChatServiceAsync(null, cancellationToken).ConfigureAwait(false);
        _activeChatServiceAdapter = null;
        await ClearBindingForCurrentConversationAsync(sink).ConfigureAwait(false);
        sink.UpdateAgentIdentity(null, null);
        await _connectionCoordinator.SetConnectionInstanceIdAsync(null, cancellationToken).ConfigureAwait(false);
        await _connectionCoordinator.ResetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AcpTransportApplyResult> ConnectProfileInPoolAsync(
        ServerConfiguration profile,
        IAcpTransportConfiguration transportConfiguration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transportConfiguration);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureTransportSupported(profile.Transport);
        ApplyProfileToTransportConfiguration(profile, transportConfiguration);
        var reuseKey = BuildConnectionReuseKey(transportConfiguration);
        var requestGeneration = GetPoolProfileDisconnectGeneration(profile.Id);

        if (_connectionPoolManager.TryGetReusableSession(profile.Id, reuseKey, out var cachedSession))
        {
            ThrowIfPoolProfileRequestSuperseded(profile.Id, requestGeneration, cancellationToken);
            _sessionRegistry.Touch(profile.Id);
            return new AcpTransportApplyResult(cachedSession.Service, cachedSession.InitializeResponse);
        }

        var requestKey = new PoolConnectionRequestKey(profile.Id, reuseKey);
        await using var poolGate = await AcquirePoolConnectionGateAsync(requestKey, cancellationToken)
            .ConfigureAwait(false);
        ThrowIfPoolProfileRequestSuperseded(profile.Id, requestGeneration, cancellationToken);

        if (_connectionPoolManager.TryGetReusableSession(profile.Id, reuseKey, out cachedSession))
        {
            ThrowIfPoolProfileRequestSuperseded(profile.Id, requestGeneration, cancellationToken);
            _sessionRegistry.Touch(profile.Id);
            return new AcpTransportApplyResult(cachedSession.Service, cachedSession.InitializeResponse);
        }

        using var attempt = BeginPoolConnectionAttempt(
            requestKey,
            poolGate.Gate,
            requestGeneration,
            cancellationToken);
        var service = _chatServiceFactory.CreateChatService(profile);
        var wrapped = WrapChatService(service, sink: null, attempt.Token);
        attempt.AttachService(wrapped);
        try
        {
            var initializeResponse = await InitializeCandidateAsync(
                    wrapped,
                    transportConfiguration.SelectedTransportType,
                    profile.Id,
                    conversationId: null,
                    ResolveInitializeTimeout(profile),
                    attempt.Token)
                .ConfigureAwait(false);
            ThrowIfPoolProfileRequestSuperseded(profile.Id, requestGeneration, attempt.Token);
            var connectionInstanceId = CreateConnectionInstanceId();
            var replacedSession = _connectionPoolManager.RecordSession(
                profile.Id,
                wrapped,
                initializeResponse,
                reuseKey,
                connectionInstanceId);
            await DisposeReplacedSessionAsync(replacedSession, wrapped).ConfigureAwait(false);
            return new AcpTransportApplyResult(wrapped, initializeResponse);
        }
        catch
        {
            await DisposeServiceAsync(wrapped).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectProfileInPoolAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var inFlightAttempts = CancelPoolConnectionAttempts(profileId);
        if (_sessionRegistry.TryGetByProfile(profileId, out var session))
        {
            _connectionPoolManager.RemoveByService(session.Service, out _);
            await DisposeServiceAsync(session.Service).ConfigureAwait(false);
        }

        if (inFlightAttempts.Count > 0)
        {
            await Task.WhenAll(inFlightAttempts.Select(static attempt => attempt.Completion))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var attempt in inFlightAttempts)
            {
                if (attempt.Service is not { } service)
                {
                    continue;
                }

                if (_connectionPoolManager.RemoveByService(service, out _))
                {
                    await DisposeServiceAsync(service).ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask<PoolConnectionGateLease> AcquirePoolConnectionGateAsync(
        PoolConnectionRequestKey requestKey,
        CancellationToken cancellationToken)
    {
        PoolConnectionRequestGate gate;
        lock (_poolConnectionGateSync)
        {
            if (!_poolConnectionGates.TryGetValue(requestKey, out gate!))
            {
                gate = new PoolConnectionRequestGate();
                _poolConnectionGates[requestKey] = gate;
            }

            gate.RefCount++;
        }

        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new PoolConnectionGateLease(this, requestKey, gate);
        }
        catch
        {
            ReleasePoolConnectionGateReference(requestKey, gate, hasSemaphoreLease: false);
            throw;
        }
    }

    private void ReleasePoolConnectionGateReference(
        PoolConnectionRequestKey requestKey,
        PoolConnectionRequestGate gate,
        bool hasSemaphoreLease)
    {
        if (hasSemaphoreLease)
        {
            gate.Semaphore.Release();
        }

        lock (_poolConnectionGateSync)
        {
            gate.RefCount--;
            if (gate.RefCount == 0)
            {
                _poolConnectionGates.Remove(requestKey);
                gate.Semaphore.Dispose();
            }
        }
    }

    private long GetPoolProfileDisconnectGeneration(string profileId)
    {
        lock (_poolConnectionGateSync)
        {
            return GetPoolProfileDisconnectGenerationLocked(profileId);
        }
    }

    private long GetPoolProfileDisconnectGenerationLocked(string profileId)
        => _poolProfileDisconnectGenerations.TryGetValue(profileId, out var generation)
            ? generation
            : 0;

    private void ThrowIfPoolProfileRequestSuperseded(
        string profileId,
        long expectedGeneration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetPoolProfileDisconnectGeneration(profileId) != expectedGeneration)
        {
            throw new OperationCanceledException("The pooled profile connection request was superseded by a disconnect intent.");
        }
    }

    private PoolConnectionAttempt BeginPoolConnectionAttempt(
        PoolConnectionRequestKey requestKey,
        PoolConnectionRequestGate gate,
        long expectedGeneration,
        CancellationToken cancellationToken)
    {
        lock (_poolConnectionGateSync)
        {
            if (GetPoolProfileDisconnectGenerationLocked(requestKey.ProfileId) != expectedGeneration)
            {
                throw new OperationCanceledException("The pooled profile connection request was superseded by a disconnect intent.");
            }

            var attempt = new PoolConnectionAttempt(
                this,
                requestKey,
                gate,
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            gate.ActiveAttempt = attempt;
            return attempt;
        }
    }

    private void CompletePoolConnectionAttempt(
        PoolConnectionRequestKey requestKey,
        PoolConnectionRequestGate gate,
        PoolConnectionAttempt attempt)
    {
        lock (_poolConnectionGateSync)
        {
            if (ReferenceEquals(gate.ActiveAttempt, attempt))
            {
                gate.ActiveAttempt = null;
            }
        }

        attempt.Complete();
    }

    private IReadOnlyList<PoolConnectionAttempt> CancelPoolConnectionAttempts(string profileId)
    {
        var attempts = new List<PoolConnectionAttempt>();
        lock (_poolConnectionGateSync)
        {
            var nextGeneration = GetPoolProfileDisconnectGenerationLocked(profileId) + 1;
            _poolProfileDisconnectGenerations[profileId] = nextGeneration;
            foreach (var pair in _poolConnectionGates)
            {
                if (!string.Equals(pair.Key.ProfileId, profileId, StringComparison.Ordinal)
                    || pair.Value.ActiveAttempt is not { } attempt)
                {
                    continue;
                }

                attempt.Cancel();
                attempts.Add(attempt);
            }
        }

        return attempts;
    }

    private AcpChatServiceAdapter WrapChatService(
        IChatService chatService,
        IAcpChatCoordinatorSink? sink,
        CancellationToken applyScopeToken)
    {
        ArgumentNullException.ThrowIfNull(chatService);

        AcpChatServiceAdapter? wrappedService = null;
        var dispatcher = sink?.Dispatcher ?? InlineDispatcher.Instance;
        Func<string?, Task>? resyncCallback = sink != null
            ? sourceSessionId => HandleResyncRequiredAsync(
                sink,
                wrappedService!,
                sourceSessionId,
                applyScopeToken)
            : null;
        var eventAdapter = new AcpEventAdapter(
            update => wrappedService!.PublishBufferedUpdate(update),
            dispatcher,
            _sessionUpdateBufferLimit,
            AcpEventAdapter.DefaultHydrationReplayBufferLimit,
            logger: null,
            resyncRequiredAsync: resyncCallback);
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
            sourceService.SuppressAllBufferedUpdates("StaleApplyScope");
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
                wrappedService.ReleaseUnscopedBufferedUpdates();
                return;
            }
        }

        if (sink.ConnectionGeneration > 0)
        {
            wrappedService.ReleaseUnscopedBufferedUpdates(lowTrust: true, reason: "ConnectionGenerationAdvanced");
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

    private async Task DisposeReplacedSessionAsync(
        AcpConnectionSession? replacedSession,
        AcpChatServiceAdapter replacementService)
    {
        if (replacedSession is null
            || ReferenceEquals(replacedSession.Service, replacementService))
        {
            return;
        }

        try
        {
            await DisposeServiceAsync(replacedSession.Service).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to dispose replaced ACP connection pool session. profileId={ProfileId}",
                replacedSession.ProfileId);
        }
    }

    private async Task RestoreConnectionStateAfterDiscardAsync(AcpConnectionStateSnapshot snapshot)
    {
        // Connecting/Initializing are non-authoritative intermediate states owned exclusively by the
        // apply path that drives them forward. Once that apply is discarded, no live actor remains to
        // progress them, so reflecting them back into the store would publish a phantom "still connecting"
        // state with no work behind it. Only Connected reflects a committed pre-existing service worth
        // preserving; everything else collapses to Disconnected.
        try
        {
            await _connectionCoordinator.SetConnectionInstanceIdAsync(snapshot.ConnectionInstanceId, CancellationToken.None)
                .ConfigureAwait(false);
            if (snapshot.IsConnected)
            {
                await _connectionCoordinator.SetConnectedAsync(snapshot.SelectedProfileId, CancellationToken.None)
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

    private async Task<AcpConnectionStateSnapshot> CaptureConnectionStateAsync(
        IAcpChatCoordinatorSink sink,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_connectionCoordinator is IAcpConnectionStateReader stateReader)
        {
            var state = await stateReader.GetCurrentStateAsync(cancellationToken).ConfigureAwait(false)
                ?? ChatConnectionState.Empty;
            return new(
                state.ForegroundTransportProfileId,
                state.ConnectionInstanceId,
                state.Phase == ConnectionPhase.Connected,
                state.Error);
        }

        return new(
            sink.SelectedProfileId,
            sink.ConnectionInstanceId,
            sink.IsConnected,
            sink.ConnectionErrorMessage);
    }

    private static string? ResolveDisplayAgentName(AgentInfo? agentInfo)
    {
        if (agentInfo is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(agentInfo.Title)
            ? agentInfo.Name
            : agentInfo.Title;
    }

    private async Task DisposeServiceAsync(IChatService? service)
    {
        if (service == null)
        {
            return;
        }

        // 该 helper 多数从 catch 清理路径调用(superseded/faulted candidate),
        // 释放失败若上抛会顶替真正的取消/连接异常,必须 best-effort 落日志。
        try
        {
            await service.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to disconnect ACP chat service cleanly during cleanup.");
        }

        try
        {
            service.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to dispose ACP chat service cleanly during cleanup.");
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

    private static async Task<InitializeResponse> InitializeCandidateAsync(
        IChatService chatService,
        TransportType transportType,
        string? profileId,
        string? conversationId,
        TimeSpan initializeTimeout,
        CancellationToken cancellationToken)
    {
        return await AcpInitializeTimeout.WaitForInitializeAsync(
            chatService,
            transportType,
            profileId,
            conversationId,
            initializeTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    private static TimeSpan ResolveInitializeTimeout(ServerConfiguration? profile)
        => AcpInitializeTimeout.Resolve(profile);

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

    private void EnsureTransportSupported(TransportType transport)
    {
        if (_transportSupportPolicy.IsSupported(transport))
        {
            return;
        }

        throw new NotSupportedException(
            _transportSupportPolicy.GetUnsupportedReason(transport)
            ?? $"Unsupported transport type: {transport}.");
    }

    private static string CreateConnectionInstanceId()
        => Guid.NewGuid().ToString("N");

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
            transportConfiguration.StdioArguments = profile.StdioArguments;
            transportConfiguration.RemoteUrl = string.Empty;
            return;
        }

        transportConfiguration.RemoteUrl = profile.ServerUrl ?? string.Empty;
        transportConfiguration.StdioCommand = string.Empty;
        transportConfiguration.StdioArguments = Array.Empty<string>();
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
        string? ConnectionInstanceId,
        bool IsConnected,
        string? ErrorMessage)
    {
        public string PhaseName => IsConnected ? "Connected" : "Disconnected";
    }

    private readonly record struct PoolConnectionRequestKey(
        string ProfileId,
        AcpConnectionReuseKey ReuseKey);

    private sealed class PoolConnectionRequestGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int RefCount { get; set; }

        public PoolConnectionAttempt? ActiveAttempt { get; set; }
    }

    private readonly struct PoolConnectionGateLease : IAsyncDisposable
    {
        private readonly AcpChatCoordinator _owner;
        private readonly PoolConnectionRequestKey _requestKey;
        private readonly PoolConnectionRequestGate _gate;

        public PoolConnectionGateLease(
            AcpChatCoordinator owner,
            PoolConnectionRequestKey requestKey,
            PoolConnectionRequestGate gate)
        {
            _owner = owner;
            _requestKey = requestKey;
            _gate = gate;
        }

        public PoolConnectionRequestGate Gate => _gate;

        public ValueTask DisposeAsync()
        {
            _owner.ReleasePoolConnectionGateReference(
                _requestKey,
                _gate,
                hasSemaphoreLease: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PoolConnectionAttempt : IDisposable
    {
        private readonly AcpChatCoordinator _owner;
        private readonly PoolConnectionRequestKey _requestKey;
        private readonly PoolConnectionRequestGate _gate;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;

        public PoolConnectionAttempt(
            AcpChatCoordinator owner,
            PoolConnectionRequestKey requestKey,
            PoolConnectionRequestGate gate,
            CancellationTokenSource cancellationTokenSource)
        {
            _owner = owner;
            _requestKey = requestKey;
            _gate = gate;
            _cancellationTokenSource = cancellationTokenSource;
        }

        public CancellationToken Token => _cancellationTokenSource.Token;

        public Task Completion => _completion.Task;

        public AcpChatServiceAdapter? Service { get; private set; }

        public void AttachService(AcpChatServiceAdapter service)
            => Service = service ?? throw new ArgumentNullException(nameof(service));

        public void Cancel()
        {
            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Complete()
            => _completion.TrySetResult();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.CompletePoolConnectionAttempt(_requestKey, _gate, this);
            _cancellationTokenSource.Dispose();
        }
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

        public Task SetConnectionInstanceIdAsync(string? connectionInstanceId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetDisconnectedAsync(string? errorMessage = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetAuthenticationRequiredAsync(
            string? hintMessage,
            string? hintResourceKey = null,
            string? hintFallback = null,
            object[]? hintFormatArgs = null,
            CancellationToken cancellationToken = default)
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

    /// <summary>
    /// Inline dispatcher used for pool-only connections that have no chat-page consumer.
    /// Events are drained synchronously on the calling thread. This is safe because pool
    /// connections never touch UI-bound objects; if a future pool path requires UI-thread
    /// affinity, this dispatcher must be replaced with a real one.
    /// </summary>
    private sealed class InlineDispatcher : IUiDispatcher
    {
        public static InlineDispatcher Instance { get; } = new();

        /// <summary>
        /// Pool connections have no UI thread; return true to allow direct enqueue
        /// semantics. Callers that need real UI-thread affinity should not use this.
        /// </summary>
        public bool HasThreadAccess => true;

        public void Enqueue(Action action) => action();

        public Task EnqueueAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(Func<Task> function) => function();
    }
}
