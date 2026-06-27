using System;
using Uno.Extensions.Reactive;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Presentation.Core.Utilities;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Content;
using SalmonEgg.Domain.Models.JsonRpc;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.ViewModels.Chat.AskUser;
using SalmonEgg.Presentation.ViewModels.Chat.Hydration;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Input;
using SalmonEgg.Presentation.ViewModels.Chat.Interactions;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Overlay;
using SalmonEgg.Presentation.Core.ViewModels.Chat.PlanPanel;
using SalmonEgg.Presentation.Core.ViewModels.Chat.ProjectAffinity;
using SalmonEgg.Presentation.ViewModels.Chat.Activation;
using SalmonEgg.Presentation.ViewModels.Chat.Transcript;
using SalmonEgg.Presentation.ViewModels.Chat.Panels;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    public Task RestoreAsync(CancellationToken cancellationToken = default)
        => EnsureConversationWorkspaceRestoredAsync(cancellationToken);

    public IChatService? CurrentChatService => _chatService;

    public long ConnectionGeneration => Interlocked.Read(ref _connectionGeneration);

    public string? ConnectionInstanceId => _connectionInstanceId;

    public IUiDispatcher Dispatcher => _uiDispatcher;

    public IConversationBindingCommands ConversationBindingCommands => _bindingCommands;

    public ValueTask<ConversationRemoteBindingState?> GetCurrentRemoteBindingAsync(CancellationToken cancellationToken = default)
        => ResolveActiveConversationBindingAsync(cancellationToken);

    public async ValueTask<ConversationRemoteBindingState?> GetConversationRemoteBindingAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return ToBindingState(binding);
    }

    public bool IsInitialized => IsConnected && !IsInitializing;

    public string? CurrentRemoteSessionId => _currentRemoteSessionId;

    public string? SelectedProfileIntentId => _selectedProfileIntentIdFromStore;

    public string? SelectedProfileId => _selectedProfileIdFromStore;

    public string? ForegroundTransportProfileId => _foregroundTransportProfileIdFromStore;

    public async Task<string?> ResolvePreferredNewSessionDraftProfileIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(connectionState.SelectedProfileIntentId))
        {
            return connectionState.SelectedProfileIntentId;
        }

        if (!string.IsNullOrWhiteSpace(_selectedProfileIntentIdFromStore))
        {
            return _selectedProfileIntentIdFromStore;
        }

        if (!string.IsNullOrWhiteSpace(_preferences.LastSelectedServerId))
        {
            return _preferences.LastSelectedServerId;
        }

        return SelectedAcpProfile?.Id;
    }

    public IReadOnlyList<McpServer> CurrentMcpServers => _currentMcpServers;

    public void SetCurrentMcpServers(IReadOnlyList<McpServer> mcpServers)
    {
        _currentMcpServers = McpServerJsonConverter.CloneServers(mcpServers);
    }

    public async Task<IReadOnlyList<McpServer>> ResolveCurrentMcpServersAsync(
        CancellationToken cancellationToken = default)
        => await _mcpServerResolver.ResolveCurrentMcpServersAsync(this, cancellationToken)
            .ConfigureAwait(false);

    public Task SetIsHydratingAsync(bool isHydrating, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _chatStore.Dispatch(new SetIsHydratingAction(isHydrating)).AsTask();
    }

    public async Task SetConversationHydratingAsync(
        string conversationId,
        bool isHydrating,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        await PostToUiAsync(() =>
        {
            if (isHydrating)
            {
                _pendingHistoryOverlayDismissConversationId = null;
                SetConversationOverlayOwners(
                    sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                    connectionLifecycleConversationId: _connectionLifecycleOverlayConversationId,
                    historyConversationId: conversationId);
            }
            else if (string.Equals(_historyOverlayConversationId, conversationId, StringComparison.Ordinal))
            {
                _pendingHistoryOverlayDismissConversationId = conversationId;
            }
        }).ConfigureAwait(false);

        await _chatStore.Dispatch(new SetIsHydratingAction(isHydrating)).ConfigureAwait(false);
    }

    public async Task MarkActiveConversationRemoteHydratedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var conversationId = CurrentSessionId;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        await SetConversationRuntimeStateAsync(
                conversationId!,
                ConversationRuntimePhase.Warm,
                binding,
                reason: ConversationRuntimeReasons.MarkedHydrated,
                cancellationToken)
            .ConfigureAwait(false);
        await ClearConversationUnreadAttentionAsync(conversationId!).ConfigureAwait(false);
    }

    public async Task MarkConversationRemoteHydratedAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        await SetConversationRuntimeStateAsync(
                conversationId,
                ConversationRuntimePhase.Warm,
                binding,
                reason: ConversationRuntimeReasons.MarkedHydrated,
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            await ClearConversationUnreadAttentionAsync(conversationId).ConfigureAwait(false);
        }
    }

    public Task ApplyConversationSessionLoadResponseAsync(
        string conversationId,
        SessionLoadResponse response,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ApplySessionLoadResponseAsync(conversationId, response);
    }

    public async Task ResetConversationForResyncAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        await SetConversationRuntimeStateAsync(
                conversationId,
                ConversationRuntimePhase.RemoteHydrating,
                binding,
                reason: "ResyncResetStarted",
                cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);

        await ResetConversationProjectionForResyncAsync(conversationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HydrateActiveConversationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = await _chatStore.GetCurrentStateAsync();
        var conversationId = ResolveActiveConversationId(state);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            SetError("Failed to load session: no active conversation is selected.");
            return false;
        }

        var binding = await ResolveConversationBindingAsync(conversationId!, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            SetError("Failed to load session: no remote session binding is available for the active conversation.");
            return false;
        }

        var remoteConnectionReady = await EnsureActiveConversationRemoteConnectionReadyAsync(
                conversationId!,
                activationVersion: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!remoteConnectionReady)
        {
            return false;
        }

        return await HydrateConversationAsync(
                conversationId!,
                binding!,
                activationVersion: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> HydrateConversationAsync(
        string conversationId,
        ConversationBindingSlice binding,
        long? activationVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var authoritativeConnection = await ResolveAuthoritativeForegroundConnectionAsync(
                binding.ProfileId,
                cancellationToken)
            .ConfigureAwait(false);
        if (authoritativeConnection is not { } resolvedConnection)
        {
            await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                    conversationId,
                    activationVersion,
                    SessionActivationPhase.Faulted,
                    reason: "ChatServiceNotReady")
                .ConfigureAwait(false);
            await _conversationActivationOutcomePublisher.TrySetActivationErrorAsync(
                    conversationId,
                    activationVersion,
                    "Failed to load session: ACP chat service is not connected and initialized.")
                .ConfigureAwait(false);
            return false;
        }

        var chatService = resolvedConnection.ChatService;
        var recoveryMode = AcpSessionRecoveryPolicy.ResolveForHydration(chatService.AgentCapabilities);
        if (recoveryMode == AcpSessionRecoveryMode.None)
        {
            await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                    conversationId,
                    activationVersion,
                    SessionActivationPhase.Faulted,
                    reason: "RecoveryCapabilityMissing")
                .ConfigureAwait(false);
            await _conversationActivationOutcomePublisher.TrySetActivationErrorAsync(
                    conversationId,
                    activationVersion,
                    "Failed to load session: the connected ACP agent does not advertise remote session recovery capabilities.")
                .ConfigureAwait(false);
            return false;
        }

        if (string.IsNullOrWhiteSpace(binding.RemoteSessionId))
        {
            SetError("Failed to load session: no remote session binding is available for the active conversation.");
            return false;
        }

        var stateBeforeHydration = await _chatStore.GetCurrentStateAsync();
        var currentConnectionBeforeHydration = new ConversationWarmReuseConnectionIdentity(
            resolvedConnection.ProfileId,
            resolvedConnection.ConnectionInstanceId);
        var warmReuseBeforeHydration = ConversationWarmReusePolicy.EvaluateRemoteWarmConversation(
            stateBeforeHydration.ResolveRuntimeState(conversationId),
            binding,
            currentConnectionBeforeHydration,
            HasReusableWarmProjection(stateBeforeHydration, conversationId));
        if (warmReuseBeforeHydration.CanReuse)
        {
            Logger.LogInformation(
                "Skipping remote hydration because runtime became warm before session recovery started. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} ConnectionInstanceId={ConnectionInstanceId}",
                conversationId,
                binding.RemoteSessionId,
                resolvedConnection.ConnectionInstanceId);
            return true;
        }

        await SetConversationRuntimeStateAsync(
                conversationId,
                ConversationRuntimePhase.RemoteHydrating,
                binding,
                reason: recoveryMode == AcpSessionRecoveryMode.Load ? "SessionLoadStarted" : "SessionResumeStarted",
                cancellationToken,
                connectionInstanceId: resolvedConnection.ConnectionInstanceId)
            .ConfigureAwait(false);
        var hydrationStopwatch = Stopwatch.StartNew();
        var adapter = chatService as IAcpSessionUpdateBufferController;
        long? hydrationAttemptId = null;
        var ownsRecoveryLease = false;
        var recoveryCancellationToken = cancellationToken;
        RemoteSessionRecoveryRequest? ownedRecoveryRequest = null;
        RemoteSessionRecoveryLeaseKey? ownedRecoveryLeaseKey = null;
        Logger.LogInformation(
            "Starting conversation hydration. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} CompletionMode={CompletionMode}",
            conversationId,
            binding.RemoteSessionId,
            _hydrationCompletionMode);

        try
        {
            var ownsRemoteHydrationUi = ShouldOwnRemoteHydrationUi(conversationId, activationVersion);
            var transcriptBaselineCount = await GetProjectedTranscriptCountAsync(conversationId).ConfigureAwait(false);
            var knownTranscriptGrowthGraceDeadlineUtc = DateTime.UtcNow + RemoteReplayKnownTranscriptGrowthGracePeriod;
            var replayBaseline = GetSessionUpdateObservationCount(binding.RemoteSessionId);
            var transcriptProjectionBaseline = GetTranscriptProjectionObservationCount(binding.RemoteSessionId);
            var canObserveReplayProjection = recoveryMode == AcpSessionRecoveryMode.Load && adapter != null;
            var hasCachedTranscript = transcriptBaselineCount > 0;
            var shouldAwaitReplayProjection =
                canObserveReplayProjection &&
                _hydrationCompletionMode == AcpHydrationCompletionMode.StrictReplay &&
                !hasCachedTranscript;
            Logger.LogInformation(
                "Hydration replay wait policy resolved. conversationId={ConversationId} remoteSessionId={RemoteSessionId} shouldAwaitReplayProjection={ShouldAwaitReplayProjection} cachedTranscriptCount={CachedTranscriptCount}",
                conversationId,
                binding.RemoteSessionId,
                shouldAwaitReplayProjection,
                transcriptBaselineCount);
            if (ownsRemoteHydrationUi)
            {
                await PostToUiAsync(() =>
                {
                    IsRemoteHydrationPending = true;
                    _pendingHistoryOverlayDismissConversationId = null;
                    _remoteHydrationSessionUpdateBaselineCounts[conversationId] = replayBaseline;
                    if (shouldAwaitReplayProjection)
                    {
                        _remoteHydrationKnownTranscriptBaselineCounts[conversationId] = transcriptBaselineCount;
                        _remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc[conversationId] =
                            knownTranscriptGrowthGraceDeadlineUtc;
                    }
                    else
                    {
                        ClearKnownTranscriptGrowthRequirement(conversationId);
                    }
                    SetConversationOverlayOwners(
                        sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                        connectionLifecycleConversationId: _connectionLifecycleOverlayConversationId,
                        historyConversationId: conversationId);
                    SetHydrationOverlayPhase(conversationId, HydrationOverlayPhase.RequestingSessionLoad);
                }).ConfigureAwait(false);
#if DEBUG
                Logger.LogInformation(
                    "Remote hydration overlay acquired. conversationId={ConversationId} activationVersion={ActivationVersion} remoteSessionId={RemoteSessionId}",
                    conversationId,
                    activationVersion,
                    binding.RemoteSessionId);
#endif
                await _chatStore.Dispatch(new SetIsHydratingAction(true)).ConfigureAwait(false);
            }

            if (IsActivationContextStale(activationVersion, cancellationToken))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _chatStore.Dispatch(new SelectConversationAction(conversationId)).ConfigureAwait(false);
            if (IsActivationContextStale(activationVersion, cancellationToken))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            AcpSessionRecoveryProjection recoveryProjection;
            if (recoveryMode == AcpSessionRecoveryMode.Load)
            {
                var recoveryCwd = await ResolveRecoverySessionCwdOrSessionListAsync(
                        chatService,
                        binding,
                        conversationId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(recoveryCwd))
                {
                    throw new InvalidOperationException(
                        "Cannot recover remote session because working directory is missing.");
                }

                var recoveryStart = GetOrStartRemoteSessionRecoveryProjection(
                        chatService,
                        recoveryMode,
                        conversationId,
                        binding,
                        resolvedConnection.ConnectionInstanceId,
                        recoveryCwd,
                        adapter);
                hydrationAttemptId = recoveryStart.BufferScope.HydrationAttemptId;
                ownsRecoveryLease = recoveryStart.BufferScope.OwnsRecoveryLease;
                recoveryCancellationToken = recoveryStart.BufferScope.RecoveryCancellationToken;
                ownedRecoveryRequest = ownsRecoveryLease ? recoveryStart.RecoveryRequest : null;
                ownedRecoveryLeaseKey = ownsRecoveryLease ? recoveryStart.RecoveryLeaseKey : null;
                cancellationToken.ThrowIfCancellationRequested();
                if (IsActivationContextStale(activationVersion, cancellationToken))
                {
                    return false;
                }

                if (ownsRecoveryLease && recoveryStart.ConflictingRecoveryCompletion is not null)
                {
                    await AwaitConflictingRemoteSessionRecoveryCompletionAsync(
                            recoveryStart.ConflictingRecoveryCompletion,
                            recoveryMode,
                            binding.RemoteSessionId!)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsActivationContextStale(activationVersion, cancellationToken))
                    {
                        return false;
                    }

                    if (adapter != null && hydrationAttemptId.HasValue)
                    {
                        adapter.SuppressBufferedUpdates(
                            hydrationAttemptId.Value,
                            "ConflictingRemoteRecoveryCompleted");
                        if (recoveryStart.RecoveryRequest is { } restartRequest)
                        {
                            restartRequest.HydrationAttemptId = null;
                            restartRequest.ResetBufferingStarted();
                            EnsureRemoteSessionRecoveryBufferingScope(
                                restartRequest,
                                adapter,
                                binding.RemoteSessionId!,
                                recoveryMode);
                            hydrationAttemptId = restartRequest.HydrationAttemptId;
                        }
                    }
                }

                if (ownsRecoveryLease
                    && hydrationAttemptId is null
                    && recoveryStart.RecoveryRequest is { } recoveryRequest)
                {
                    EnsureRemoteSessionRecoveryBufferingScope(
                        recoveryRequest,
                        adapter,
                        binding.RemoteSessionId!,
                        recoveryMode);
                    hydrationAttemptId = recoveryRequest.HydrationAttemptId;
                }

                if (ownsRecoveryLease)
                {
                    await ResetConversationProjectionForResyncAsync(conversationId, cancellationToken).ConfigureAwait(false);
                }

                await SetHydrationOverlayPhaseAsync(
                        conversationId,
                        activationVersion,
                        HydrationOverlayPhase.RequestingSessionLoad)
                    .ConfigureAwait(false);
                recoveryStart.StartRecoveryTransport?.Invoke();
                recoveryProjection = await recoveryStart.RecoveryTask
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                var recoveryCwd = await ResolveRecoverySessionCwdOrSessionListAsync(
                        chatService,
                        binding,
                        conversationId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(recoveryCwd))
                {
                    throw new InvalidOperationException(
                        "Cannot recover remote session because working directory is missing.");
                }

                var recoveryStart = GetOrStartRemoteSessionRecoveryProjection(
                        chatService,
                        recoveryMode,
                        conversationId,
                        binding,
                        resolvedConnection.ConnectionInstanceId,
                        recoveryCwd,
                        adapter: null);
                ownsRecoveryLease = recoveryStart.BufferScope.OwnsRecoveryLease;
                recoveryCancellationToken = recoveryStart.BufferScope.RecoveryCancellationToken;
                ownedRecoveryRequest = ownsRecoveryLease ? recoveryStart.RecoveryRequest : null;
                ownedRecoveryLeaseKey = ownsRecoveryLease ? recoveryStart.RecoveryLeaseKey : null;
                if (recoveryStart.ConflictingRecoveryCompletion is not null)
                {
                    await AwaitConflictingRemoteSessionRecoveryCompletionAsync(
                            recoveryStart.ConflictingRecoveryCompletion,
                            recoveryMode,
                            binding.RemoteSessionId!)
                        .ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (IsActivationContextStale(activationVersion, cancellationToken))
                {
                    return false;
                }

                await SetHydrationOverlayPhaseAsync(
                        conversationId,
                        activationVersion,
                        HydrationOverlayPhase.RequestingSessionLoad)
                    .ConfigureAwait(false);
                recoveryStart.StartRecoveryTransport?.Invoke();
                recoveryProjection = await recoveryStart.RecoveryTask
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (!recoveryProjection.WasPublished)
            {
                await RecoverProjectionAfterStaleHydrationAttemptAsync(
                        conversationId,
                        activationVersion,
                        binding,
                        ConversationRuntimePhase.RemoteConnectionReady,
                        cancellationToken)
                    .ConfigureAwait(false);
                return false;
            }

            if (shouldAwaitReplayProjection)
            {
                await AwaitRemoteReplayProjectionAsync(
                        conversationId,
                        activationVersion,
                        binding.RemoteSessionId!,
                        replayBaseline,
                        transcriptProjectionBaseline,
                        hydrationAttemptId,
                        recoveryCancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await SetHydrationOverlayPhaseAsync(
                        conversationId,
                        activationVersion,
                        HydrationOverlayPhase.FinalizingProjection)
                    .ConfigureAwait(false);
            }

            if (shouldAwaitReplayProjection)
            {
                await _hydrationCoordinator.AwaitKnownTranscriptGrowthRequirementAsync(
                        _hydrationContext,
                        conversationId,
                        transcriptBaselineCount,
                        knownTranscriptGrowthGraceDeadlineUtc,
                        hydrationAttemptId,
                        recoveryCancellationToken)
                    .ConfigureAwait(false);
            }

            await RestoreCachedConversationProjectionIfReplayIsEmptyAsync(
                    conversationId)
                .ConfigureAwait(false);
            await ApplyCurrentStoreProjectionAsync(activationVersion).ConfigureAwait(false);
            var metadataRefreshToken = _disposeCts.Token;
            var metadataRefreshGeneration = _foregroundChatServiceGeneration;
            _ = ApplyRemoteSessionInfoSnapshotWhenReadyAsync(
                conversationId,
                binding,
                resolvedConnection.ConnectionInstanceId,
                metadataRefreshGeneration,
                LoadRemoteSessionInfoSnapshotFromSsotAsync(
                    conversationId,
                    binding,
                    chatService,
                    metadataRefreshToken),
                metadataRefreshToken);
            Logger.LogInformation(
                "Conversation hydration completed. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} ElapsedMs={ElapsedMs}",
                conversationId,
                binding.RemoteSessionId,
                hydrationStopwatch.ElapsedMilliseconds);

            return true;
        }
        catch (OperationCanceledException)
        {
            ReleaseBufferedUpdatesAfterInterruptedHydration(
                adapter,
                hydrationAttemptId,
                ownsRecoveryLease && recoveryCancellationToken.IsCancellationRequested,
                "RemoteHydrationCanceled");

            throw;
        }
        catch (Exception ex)
        {
            if (IsActivationContextStale(activationVersion, cancellationToken))
            {
                Logger.LogInformation(
                    ex,
                    "Discarding stale remote hydration failure because the activation intent no longer owns the chat shell. ConversationId={ConversationId}",
                    conversationId);
                ReleaseBufferedUpdatesAfterInterruptedHydration(
                    adapter,
                    hydrationAttemptId,
                    ownsRecoveryLease,
                    "StaleRemoteHydrationFailed");
                return false;
            }

            ReleaseBufferedUpdatesAfterInterruptedHydration(
                adapter,
                hydrationAttemptId,
                ownsRecoveryLease,
                "DiscoverImportLoadSessionFailed");

            if (AcpErrorClassifier.IsRemoteSessionNotFound(ex))
            {
                Logger.LogWarning(
                    ex,
                    "Remote session binding became stale during hydration. Preserving binding for explicit recovery. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                    conversationId,
                    binding.RemoteSessionId);
                await SetConversationRuntimeStateAsync(
                        conversationId,
                        ConversationRuntimePhase.Stale,
                        binding,
                        reason: "RemoteSessionNotFound",
                        cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
                await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                        conversationId,
                        activationVersion,
                        SessionActivationPhase.Faulted,
                        reason: ex.Message)
                    .ConfigureAwait(false);
                await _conversationActivationOutcomePublisher.TrySetActivationErrorAsync(
                        conversationId,
                        activationVersion,
                        $"Failed to load session: {ex.Message}")
                    .ConfigureAwait(false);
                return false;
            }
            else
            {
                await SetConversationRuntimeStateAsync(
                        conversationId,
                        ConversationRuntimePhase.Faulted,
                        binding,
                        reason: ex.Message,
                        cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
            }

            Logger.LogError(ex, "Failed to hydrate active conversation from remote session");
            Logger.LogInformation(
                "Conversation hydration failed. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} ElapsedMs={ElapsedMs}",
                conversationId,
                binding.RemoteSessionId,
                hydrationStopwatch.ElapsedMilliseconds);
            await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                    conversationId,
                    activationVersion,
                    SessionActivationPhase.Faulted,
                    reason: ex.Message)
                .ConfigureAwait(false);
            await _conversationActivationOutcomePublisher.TrySetActivationErrorAsync(
                    conversationId,
                    activationVersion,
                    $"Failed to load session: {ex.Message}")
                .ConfigureAwait(false);
            return false;
        }
        finally
        {
            var canceledUnstartedRecovery = TryCancelUnstartedOwnedRemoteSessionRecovery(
                ownedRecoveryRequest,
                ownedRecoveryLeaseKey,
                ownsRecoveryLease);
            if (canceledUnstartedRecovery)
            {
                ReleaseBufferedUpdatesAfterInterruptedHydration(
                    adapter,
                    hydrationAttemptId,
                    ownsRecoveryLease,
                    "RemoteHydrationCanceledBeforeTransportStart");
            }

            if (ShouldOwnRemoteHydrationUi(conversationId, activationVersion))
            {
                await _chatStore.Dispatch(new SetIsHydratingAction(false)).ConfigureAwait(false);
                await PostToUiAsync(() =>
                {
                    _pendingHistoryOverlayDismissConversationId = conversationId;
                    IsRemoteHydrationPending = false;
                }).ConfigureAwait(false);
#if DEBUG
                Logger.LogInformation(
                    "Remote hydration logical completion reached. conversationId={ConversationId} activationVersion={ActivationVersion} pendingHistoryDismiss={PendingHistoryDismissConversationId}",
                    conversationId,
                    activationVersion,
                    _pendingHistoryOverlayDismissConversationId);
#endif
                await AwaitBufferedSessionReplayProjectionAsync(cancellationToken, hydrationAttemptId).ConfigureAwait(false);
                await ApplyCurrentStoreProjectionAsync(activationVersion).ConfigureAwait(false);
            }
        }
    }

    private bool UpdateVisibleTranscriptConversationId(string? conversationId, bool hasVisibleTranscript)
    {
        var nextConversationId = hasVisibleTranscript
            ? conversationId
            : string.IsNullOrWhiteSpace(conversationId) ? null : conversationId;
        if (string.Equals(_visibleTranscriptConversationId, nextConversationId, StringComparison.Ordinal))
        {
            return false;
        }

        _visibleTranscriptConversationId = nextConversationId;
        return true;
    }

    private void ReplacePlanEntries(IReadOnlyList<ConversationPlanEntrySnapshot> planEntries)
        => PlanEntries = _planEntriesProjectionCoordinator.Replace(planEntries);

    private async Task<bool> CanReuseWarmCurrentConversationAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
        {
            return false;
        }

        var state = await _chatStore.GetCurrentStateAsync();
        var workspaceBinding = _conversationWorkspace.GetRemoteBinding(sessionId);
        var binding = state.ResolveBinding(sessionId)
            ?? (workspaceBinding is null
                ? null
                : new ConversationBindingSlice(
                    workspaceBinding.ConversationId,
                    workspaceBinding.RemoteSessionId,
                    workspaceBinding.BoundProfileId));
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            return string.IsNullOrWhiteSpace(binding?.ProfileId);
        }

        if (AcpSessionRecoveryPolicy.ResolveForHydration(_chatService?.AgentCapabilities) == AcpSessionRecoveryMode.None)
        {
            return true;
        }

        var hasReusableProjection = HasReusableWarmProjection(state, sessionId);
        var currentConnection = await ResolveWarmReuseConnectionIdentityAsync(binding, cancellationToken).ConfigureAwait(false);
        return ConversationWarmReusePolicy.EvaluateRemoteWarmConversation(
            state.ResolveRuntimeState(sessionId),
            binding,
            currentConnection,
            hasReusableProjection).CanReuse;
    }

    private async Task<bool> CanReusePendingRemoteHydrationCurrentConversationAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsPendingRemoteHydrationActivation(sessionId))
        {
            return false;
        }

        var state = await _chatStore.GetCurrentStateAsync();
        if (!string.Equals(state.HydratedConversationId, sessionId, StringComparison.Ordinal)
            || !string.Equals(ResolvePresentedSessionConversationId(), sessionId, StringComparison.Ordinal))
        {
            return false;
        }

        var binding = state.ResolveBinding(sessionId);
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            return false;
        }

        var runtimeState = state.ResolveRuntimeState(sessionId);
        if (!runtimeState.HasValue)
        {
            return false;
        }

        return runtimeState.Value.Phase is ConversationRuntimePhase.Selected
            or ConversationRuntimePhase.RemoteConnectionReady
            or ConversationRuntimePhase.RemoteHydrating;
    }

    private bool IsPendingRemoteHydrationActivation(string sessionId)
    {
        var activeActivation = _shellNavigationRuntimeState?.ActiveSessionActivation;
        return activeActivation is not null
            && activeActivation.Matches(sessionId)
            && activeActivation.Phase is SessionActivationPhase.Selected
                or SessionActivationPhase.RemoteConnectionReady
                or SessionActivationPhase.RemoteHydrationPending;
    }

    private bool HasCompetingInFlightConversationActivation(ChatState state, string targetConversationId)
    {
        if (string.IsNullOrWhiteSpace(targetConversationId))
        {
            return false;
        }

        var candidateConversationIds = new[] { state.HydratedConversationId, CurrentSessionId }
            .Where(static conversationId => !string.IsNullOrWhiteSpace(conversationId))
            .Distinct(StringComparer.Ordinal);

        var targetRuntime = state.ResolveRuntimeState(targetConversationId);

        foreach (var candidateConversationId in candidateConversationIds)
        {
            if (string.Equals(candidateConversationId, targetConversationId, StringComparison.Ordinal))
            {
                continue;
            }

            var runtimeState = state.ResolveRuntimeState(candidateConversationId!);
            if (runtimeState is not { } competingRuntime)
            {
                continue;
            }

            if (competingRuntime.Phase is ConversationRuntimePhase.Selecting
                or ConversationRuntimePhase.Selected
                or ConversationRuntimePhase.RemoteConnectionReady
                or ConversationRuntimePhase.RemoteHydrating)
            {
                // A competing conversation's in-flight activation should not block warm
                // reuse for the target conversation when the target is already warm.
                // The target's runtime state and projection are independent of other
                // conversations' activation lifecycle.
                if (targetRuntime is { Phase: ConversationRuntimePhase.Warm })
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private async Task CancelPendingPermissionRequestAsync(string? expectedRemoteSessionId = null)
    {
        var pending = PendingPermissionRequest;
        if (pending is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(expectedRemoteSessionId)
            && !string.Equals(pending.SessionId, expectedRemoteSessionId, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (_chatService != null)
            {
                await _chatService.RespondToPermissionRequestAsync(pending.MessageId, "cancelled", null).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(
                ex,
                "Failed to send permission cancellation response. SessionId={SessionId}",
                pending.SessionId);
        }
        finally
        {
            ShowPermissionDialog = false;
            ClearInlinePermissionRequest(pending);
            if (ReferenceEquals(PendingPermissionRequest, pending))
            {
                PendingPermissionRequest = null;
            }
        }
    }

    private void ApplySelectedProfile(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var selectedProfile = ResolveLoadedProfileSelection(profile);
        _selectedProfileIntentIdFromStore = profile.Id;
        _selectedProfileIdFromStore = profile.Id;
        ApplyResolvedProfileSelection(
            selectedProfile,
            suppressStoreProjection: true,
            suppressProfileSyncFromStore: false);
    }

    private bool IsCurrentConnectionContext(AcpConnectionContext connectionContext)
    {
        if (_disposed || !connectionContext.ActivationVersion.HasValue)
        {
            return !_disposed;
        }

        return _conversationActivationOrchestrator.IsLatestActivationVersion(connectionContext.ActivationVersion.Value);
    }

    private IAcpChatCoordinatorSink CreateScopedAcpCoordinatorSink(AcpConnectionContext connectionContext)
        => connectionContext.ActivationVersion.HasValue
            ? new ScopedAcpChatCoordinatorSink(this, connectionContext)
            : this;

    public void SelectProfile(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (_uiDispatcher.HasThreadAccess)
        {
            ApplySelectedProfile(profile);
            _ = _chatConnectionStore.Dispatch(new SetSelectedProfileIntentAction(profile.Id));
            return;
        }

        _ = SelectProfileAsync(profile);
    }

    public Task SelectProfileAsync(ServerConfiguration profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        return SelectProfileCoreAsync(profile, cancellationToken);
    }

    private async Task SelectProfileCoreAsync(ServerConfiguration profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await PostToUiAsync(() => ApplySelectedProfile(profile)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _chatConnectionStore.Dispatch(new SetSelectedProfileIntentAction(profile.Id)).ConfigureAwait(false);
    }

    private void ApplyChatServiceReplacement(
        IChatService? chatService,
        ServiceReplaceIntent intent = ServiceReplaceIntent.ForegroundOwner)
    {
        if (_chatService != null)
        {
            UnsubscribeFromChatService(_chatService);
        }

        if (intent == ServiceReplaceIntent.ForegroundOwner)
        {
            unchecked
            {
                _foregroundChatServiceGeneration++;
            }

            CancelAndClearRemoteSessionRecoveryRequests("ForegroundChatServiceReplacement");
            _ = _chatStore.Dispatch(new ResetConversationRuntimeStatesAction());
            _remoteHydrationSessionUpdateBaselineCounts.Clear();
            _remoteHydrationKnownTranscriptBaselineCounts.Clear();
            _remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc.Clear();
            _hydrationOverlayPhase = HydrationOverlayPhase.None;
            _hydrationOverlayPhaseConversationId = null;
            _panelStateCoordinator.ClearAskUserRequests();
            PendingAskUserRequest = null;
        }
        _chatService = chatService;
        if (chatService != null)
        {
            SubscribeToChatService(chatService);
        }

        OnPropertyChanged(nameof(OverlayStatusText));
        OnPropertyChanged(nameof(CurrentChatService));
        OnPropertyChanged(nameof(IsInitialized));
    }

    private async Task ApplyChatServiceReplacementAsync(
        IChatService? chatService,
        ServiceReplaceIntent intent = ServiceReplaceIntent.ForegroundOwner)
    {
        if (_chatService != null)
        {
            UnsubscribeFromChatService(_chatService);
        }

        if (intent == ServiceReplaceIntent.ForegroundOwner)
        {
            unchecked
            {
                _foregroundChatServiceGeneration++;
            }

            CancelAndClearRemoteSessionRecoveryRequests("ForegroundChatServiceReplacement");
            await _chatStore.Dispatch(new ResetConversationRuntimeStatesAction()).ConfigureAwait(false);
        }

        if (_uiDispatcher.HasThreadAccess)
        {
            FinalizeChatServiceReplacement(chatService, intent);
            return;
        }

        await PostToUiAsync(() => FinalizeChatServiceReplacement(chatService, intent)).ConfigureAwait(false);
    }

    private void FinalizeChatServiceReplacement(
        IChatService? chatService,
        ServiceReplaceIntent intent)
    {
        if (intent == ServiceReplaceIntent.ForegroundOwner)
        {
            _remoteHydrationSessionUpdateBaselineCounts.Clear();
            _remoteHydrationKnownTranscriptBaselineCounts.Clear();
            _remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc.Clear();
            _hydrationOverlayPhase = HydrationOverlayPhase.None;
            _hydrationOverlayPhaseConversationId = null;
            _panelStateCoordinator.ClearAskUserRequests();
            PendingAskUserRequest = null;
        }

        _chatService = chatService;
        if (chatService != null)
        {
            SubscribeToChatService(chatService);
        }

        OnPropertyChanged(nameof(OverlayStatusText));
        OnPropertyChanged(nameof(CurrentChatService));
        OnPropertyChanged(nameof(IsInitialized));
    }

    public void ReplaceChatService(IChatService? chatService)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            ApplyChatServiceReplacement(chatService);
            return;
        }

        _uiDispatcher.Enqueue(() => ApplyChatServiceReplacement(chatService));
    }

    public Task ReplaceChatServiceAsync(IChatService? chatService, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PostToUiAsync(() => ApplyChatServiceReplacementAsync(chatService));
    }

    internal Task ReplaceChatServiceWithIntentAsync(IChatService? chatService, ServiceReplaceIntent intent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PostToUiAsync(() => ApplyChatServiceReplacementAsync(chatService, intent));
    }

    public void UpdateConnectionState(bool isConnecting, bool isConnected, bool isInitialized, string? errorMessage)
    {
        var profileId = _selectedProfileIdFromStore;
        if (isConnecting)
        {
            _ = _acpConnectionCoordinator.SetConnectingAsync(profileId);
            return;
        }

        if (isConnected)
        {
            _ = _acpConnectionCoordinator.SetConnectedAsync(profileId);
            return;
        }

        _ = PublishDisconnectedConnectionStateAsync(errorMessage);
    }

    public void UpdateInitializationState(bool isInitializing)
    {
    }

    public void UpdateAuthenticationState(bool isRequired, string? hintMessage)
    {
        if (isRequired)
        {
            _ = _acpConnectionCoordinator.SetAuthenticationRequiredAsync(hintMessage);
            return;
        }

        _ = _acpConnectionCoordinator.ClearAuthenticationRequiredAsync();
    }

    public void UpdateAgentIdentity(string? agentName, string? agentVersion)
    {
        _ = _chatStore.Dispatch(new SetAgentIdentityAction(SelectedProfileId, agentName, agentVersion));
    }

    public async Task ResetHydratedConversationForResyncAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(CurrentSessionId))
        {
            return;
        }

        var conversationId = CurrentSessionId!;
        await ResetConversationProjectionForResyncAsync(conversationId, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResetConversationProjectionForResyncAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storeState = await _chatStore.GetCurrentStateAsync();
        var preservedSessionState = storeState.ResolveSessionStateSlice(conversationId);
        var preservedSessionInfo = ConversationSessionInfoSnapshots.Clone(
            preservedSessionState?.SessionInfo);
        await _chatStore.Dispatch(new ClearTerminalTurnAction(conversationId)).ConfigureAwait(false);
        await _chatStore.Dispatch(new HydrateConversationAction(
            conversationId,
            ImmutableList<ConversationMessageSnapshot>.Empty,
            ImmutableList<ConversationPlanEntrySnapshot>.Empty,
            ShowPlanPanel: false)).ConfigureAwait(false);
        await _chatStore.Dispatch(new SetConversationSessionStateAction(
            conversationId,
            ImmutableList<ConversationModeOptionSnapshot>.Empty,
            SelectedModeId: null,
            ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            ShowConfigOptionsPanel: false,
            preservedSessionState?.AvailableCommands ?? ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
            SessionInfo: preservedSessionInfo,
            Usage: preservedSessionState?.Usage)).ConfigureAwait(false);
    }

    private async Task<bool> TryRestoreConversationProjectionFromWorkspaceSnapshotAsync(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        var binding = await ResolveConversationBindingAsync(conversationId, CancellationToken.None).ConfigureAwait(false);
        var hydrationMode = RemoteConversationPersistencePolicy.IsRemoteBacked(
            binding?.RemoteSessionId,
            binding?.ProfileId)
            ? ConversationActivationHydrationMode.MetadataOnly
            : ConversationActivationHydrationMode.WorkspaceSnapshot;
        var fallback = await _conversationActivationCoordinator
            .ActivateSessionAsync(
                conversationId,
                hydrationMode,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (fallback.Succeeded)
        {
            return true;
        }

        Logger.LogWarning(
            "Failed to restore workspace snapshot after stale remote session fallback. ConversationId={ConversationId} Reason={Reason}",
            conversationId,
            fallback.FailureReason);
        return false;
    }

    private async Task RestoreCachedConversationProjectionIfReplayIsEmptyAsync(
        string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var snapshot = _conversationWorkspace.GetConversationSnapshot(conversationId);
        var binding = await ResolveConversationBindingAsync(conversationId, CancellationToken.None).ConfigureAwait(false);
        var snapshotOrigin = _conversationWorkspace.GetConversationSnapshotOrigin(conversationId);
        var currentConnectionInstanceId = await ResolveProjectionRestoreConnectionInstanceIdAsync().ConfigureAwait(false);
        if (!RemoteConversationWorkspaceSnapshotPolicy.CanRestoreCachedTranscriptAfterAuthoritativeHydration(
                binding,
                snapshot,
                snapshotOrigin,
                currentConnectionInstanceId))
        {
            return;
        }

        if (snapshot is null || snapshot.Transcript.Count == 0)
        {
            return;
        }

        var projectedTranscriptCount = await GetProjectedTranscriptCountAsync(conversationId).ConfigureAwait(false);
        if (projectedTranscriptCount >= snapshot.Transcript.Count)
        {
            return;
        }

        Logger.LogInformation(
            "Remote hydration projected partial transcript. Restoring cached workspace snapshot. ConversationId={ConversationId} ProjectedCount={ProjectedCount} CachedCount={CachedCount}",
            conversationId,
            projectedTranscriptCount,
            snapshot.Transcript.Count);
        await _chatStore.Dispatch(new HydrateConversationAction(
            conversationId,
            snapshot.Transcript.ToImmutableList(),
            snapshot.Plan.ToImmutableList(),
            snapshot.ShowPlanPanel)).ConfigureAwait(false);
    }

    private async Task RecoverProjectionAfterStaleHydrationAttemptAsync(
        string conversationId,
        long? activationVersion,
        ConversationBindingSlice binding,
        ConversationRuntimePhase fallbackPhase,
        CancellationToken cancellationToken)
    {
        await RestoreCachedConversationProjectionAfterInterruptedHydrationIfReplayIsEmptyAsync(
                conversationId,
                binding)
            .ConfigureAwait(false);
        await ApplyCurrentStoreProjectionAsync(activationVersion).ConfigureAwait(false);
        await SetConversationRuntimeStateAsync(
                conversationId,
                fallbackPhase,
                binding,
                reason: "HydrationAttemptSuperseded",
                cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task RestoreCachedConversationProjectionAfterInterruptedHydrationIfReplayIsEmptyAsync(
        string conversationId,
        ConversationBindingSlice binding)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var snapshot = _conversationWorkspace.GetConversationSnapshot(conversationId);
        var state = await _chatStore.GetCurrentStateAsync();
        var snapshotOrigin = _conversationWorkspace.GetConversationSnapshotOrigin(conversationId);
        var currentConnectionInstanceId = await ResolveProjectionRestoreConnectionInstanceIdAsync().ConfigureAwait(false);
        if (!RemoteConversationWorkspaceSnapshotPolicy.CanRestoreCachedTranscriptAfterInterruptedHydration(
                binding,
                snapshot,
                snapshotOrigin,
                currentConnectionInstanceId))
        {
            return;
        }

        if (snapshot is null || snapshot.Transcript.Count == 0)
        {
            return;
        }

        var projectedTranscriptCount = await GetProjectedTranscriptCountAsync(conversationId).ConfigureAwait(false);
        if (projectedTranscriptCount >= snapshot.Transcript.Count)
        {
            return;
        }

        Logger.LogInformation(
            "Interrupted hydration is restoring cached workspace snapshot. ConversationId={ConversationId} ProjectedCount={ProjectedCount} CachedCount={CachedCount}",
            conversationId,
            projectedTranscriptCount,
            snapshot.Transcript.Count);
        await _chatStore.Dispatch(new HydrateConversationAction(
            conversationId,
            snapshot.Transcript.ToImmutableList(),
            snapshot.Plan.ToImmutableList(),
            snapshot.ShowPlanPanel)).ConfigureAwait(false);
    }

    private async Task<bool> EnsureActiveConversationRemoteConnectionReadyAsync(
        string conversationId,
        long? activationVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        if (IsActivationContextStale(activationVersion, cancellationToken))
        {
            return false;
        }

        var ownsConnectionLifecycleOverlay = false;
        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (IsActivationContextStale(activationVersion, cancellationToken))
        {
            return false;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
            {
                return true;
            }

            if (await IsRemoteConnectionReadyAsync(binding.ProfileId, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            ownsConnectionLifecycleOverlay = await TryAcquireConnectionLifecycleOverlayAsync(
                    conversationId,
                    activationVersion)
                .ConfigureAwait(false);

            var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
            var hasPendingConnection = connectionState.Phase is ConnectionPhase.Connecting or ConnectionPhase.Initializing
                || IsConnecting
                || IsInitializing;
            var pendingProfileId = !string.IsNullOrWhiteSpace(connectionState.ForegroundTransportProfileId)
                ? connectionState.ForegroundTransportProfileId
                : !string.IsNullOrWhiteSpace(SelectedProfileId)
                    ? SelectedProfileId
                    : SelectedAcpProfile?.Id;
            if (!string.IsNullOrWhiteSpace(binding.ProfileId)
                && hasPendingConnection
                && (string.IsNullOrWhiteSpace(pendingProfileId)
                    || string.Equals(binding.ProfileId, pendingProfileId, StringComparison.Ordinal)))
            {
                var becameReady = await WaitForRemoteConnectionReadyAsync(binding.ProfileId, cancellationToken).ConfigureAwait(false);
                if (IsActivationContextStale(activationVersion, cancellationToken))
                {
                    return false;
                }

                if (becameReady)
                {
                    return true;
                }

                var finalConnectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(finalConnectionState.Error))
                {
                    await ApplyCurrentStoreProjectionAsync(activationVersion).ConfigureAwait(false);
                    await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                            conversationId,
                            activationVersion,
                            SessionActivationPhase.Faulted,
                            reason: finalConnectionState.Error)
                        .ConfigureAwait(false);
                    await _conversationActivationOutcomePublisher.TrySetActivationErrorAsync(
                            conversationId,
                            activationVersion,
                            $"Failed to load session: {finalConnectionState.Error}")
                        .ConfigureAwait(false);
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(binding.ProfileId))
            {
                await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                        conversationId,
                        activationVersion,
                        SessionActivationPhase.Faulted,
                        reason: "MissingBoundProfile")
                    .ConfigureAwait(false);
                await _conversationActivationOutcomePublisher.TrySetActivationErrorAsync(
                        conversationId,
                        activationVersion,
                        "Failed to load session: no ACP profile is bound to the remote conversation.")
                    .ConfigureAwait(false);
                return false;
            }

            var profile = await ResolveProfileConfigurationAsync(binding.ProfileId!, cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                Logger.LogWarning(
                    "Skipping remote conversation connection because the bound profile could not be resolved. ConversationId={ConversationId} ProfileId={ProfileId}",
                    conversationId,
                    binding.ProfileId);
                await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                        conversationId,
                        activationVersion,
                        SessionActivationPhase.Faulted,
                        reason: "ProfileNotResolved")
                    .ConfigureAwait(false);
                await _conversationActivationOutcomePublisher.TrySetActivationErrorAsync(
                        conversationId,
                        activationVersion,
                        "Failed to load session: the bound ACP profile could not be resolved.")
                    .ConfigureAwait(false);
                return false;
            }

            var connectionContext = _conversationProfileConnectionGateway.CreateConnectionContext(
                conversationId,
                binding,
                profile.Id,
                preserveConversation: true,
                activationVersion);
            await ConnectToAcpProfileCoreAsync(profile, connectionContext, cancellationToken).ConfigureAwait(false);
            if (IsActivationContextStale(activationVersion, cancellationToken))
            {
                return false;
            }

            var readyAfterConnect = await WaitForRemoteConnectionReadyAsync(binding.ProfileId, cancellationToken).ConfigureAwait(false);
            if (IsActivationContextStale(activationVersion, cancellationToken))
            {
                return false;
            }

            if (!readyAfterConnect)
            {
                await ApplyCurrentStoreProjectionAsync(activationVersion).ConfigureAwait(false);
                await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                        conversationId,
                        activationVersion,
                        SessionActivationPhase.Faulted,
                        reason: "RemoteConnectionNotReady")
                    .ConfigureAwait(false);
                await _conversationActivationOutcomePublisher.TrySetActivationErrorAsync(
                        conversationId,
                        activationVersion,
                        "Failed to load session: ACP profile connection did not become ready.")
                    .ConfigureAwait(false);
            }

            return readyAfterConnect;
        }
        finally
        {
            if (ownsConnectionLifecycleOverlay
                && string.Equals(_connectionLifecycleOverlayConversationId, conversationId, StringComparison.Ordinal))
            {
                await ReleaseConnectionLifecycleOverlayAsync(conversationId).ConfigureAwait(false);
            }
        }
    }

    private async Task EnsureSelectedProfileConnectionForConversationAsync(
        string conversationId,
        long? activationVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId)
            || string.IsNullOrWhiteSpace(binding.ProfileId))
        {
            return;
        }

        var ownsConnectionLifecycleOverlay = false;
        try
        {
            if (await IsRemoteConnectionReadyAsync(binding.ProfileId, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            ownsConnectionLifecycleOverlay = await TryAcquireConnectionLifecycleOverlayAsync(
                    conversationId,
                    activationVersion)
                .ConfigureAwait(false);

            var profile = await ResolveProfileConfigurationAsync(binding.ProfileId, cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                return;
            }

            var connectionContext = _conversationProfileConnectionGateway.CreateConnectionContext(
                conversationId,
                binding,
                profile.Id,
                preserveConversation: true,
                activationVersion);
            await ConnectToAcpProfileCoreAsync(profile, connectionContext, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (ownsConnectionLifecycleOverlay
                && string.Equals(_connectionLifecycleOverlayConversationId, conversationId, StringComparison.Ordinal))
            {
                await ReleaseConnectionLifecycleOverlayAsync(conversationId).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> EnsureActiveConversationRemoteHydratedAsync(
        string conversationId,
        long? activationVersion,
        CancellationToken cancellationToken,
        bool allowWarmReuseShortCircuit = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            return true;
        }

        var authoritativeConnection = await ResolveAuthoritativeForegroundConnectionAsync(
                binding.ProfileId,
                cancellationToken)
            .ConfigureAwait(false);
        if (authoritativeConnection is not { } resolvedConnection)
        {
            SetError("Failed to load session: ACP chat service is not connected and initialized.");
            return false;
        }

        var chatService = resolvedConnection.ChatService;
        if (AcpSessionRecoveryPolicy.ResolveForHydration(chatService.AgentCapabilities) == AcpSessionRecoveryMode.None)
        {
            return true;
        }

        var state = await _chatStore.GetCurrentStateAsync();
        var currentConnection = new ConversationWarmReuseConnectionIdentity(
            resolvedConnection.ProfileId,
            resolvedConnection.ConnectionInstanceId);
        var hasReusableProjection = HasReusableWarmProjection(state, conversationId);
        var warmReuseDecision = ConversationWarmReusePolicy.EvaluateRemoteWarmConversation(
            state.ResolveRuntimeState(conversationId),
            binding,
            currentConnection,
            hasReusableProjection);
        if (allowWarmReuseShortCircuit && warmReuseDecision.CanReuse)
        {
            Logger.LogInformation(
                "Skipping remote hydration for conversation because runtime state is warm. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} ConnectionInstanceId={ConnectionInstanceId}",
                conversationId,
                binding.RemoteSessionId,
                currentConnection.ConnectionInstanceId);
            return true;
        }

        {
            var runtimeState = state.ResolveRuntimeState(conversationId);
            var denialReason = allowWarmReuseShortCircuit
                ? warmReuseDecision.DenialReason
                : "SupersededInFlightActivationRequiresAuthoritativeHydration";
            Logger.LogInformation(
                "Warm reuse denied in HydrateConversationAsync, falling back to slow hydration. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} ExpectedConnectionInstanceId={ExpectedConnectionInstanceId} ActualConnectionInstanceId={ActualConnectionInstanceId} Reason={Reason}",
                conversationId,
                binding.RemoteSessionId,
                runtimeState?.ConnectionInstanceId,
                currentConnection.ConnectionInstanceId,
                denialReason);
        }

        await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                conversationId,
                activationVersion,
                SessionActivationPhase.RemoteHydrationPending,
                reason: "RemoteHydrationPending")
            .ConfigureAwait(false);
        return await HydrateConversationAsync(conversationId, binding!, activationVersion, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConversationActivationHydrationMode> ResolveConversationActivationHydrationModeAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var binding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            return ConversationActivationHydrationMode.WorkspaceSnapshot;
        }

        if (AcpSessionRecoveryPolicy.ResolveForHydration(_chatService?.AgentCapabilities) == AcpSessionRecoveryMode.None)
        {
            return ConversationActivationHydrationMode.WorkspaceSnapshot;
        }

        return ConversationActivationHydrationMode.SelectionOnly;
    }

    private async Task<ConversationBindingSlice?> ResolveConversationBindingAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        var state = await _chatStore.GetCurrentStateAsync();
        var binding = state.ResolveBinding(conversationId);
        if (binding != null)
        {
            return binding;
        }

        var workspaceBinding = _conversationWorkspace.GetRemoteBinding(conversationId);
        return workspaceBinding is null
            ? null
            : new ConversationBindingSlice(
                workspaceBinding.ConversationId,
                workspaceBinding.RemoteSessionId,
                workspaceBinding.BoundProfileId);
    }

    private bool HasReusableWarmProjection(ChatState state, string conversationId)
    {
        var binding = ResolveReusableWarmProjectionBinding(state, conversationId);
        var snapshot = ResolveReusableWarmProjectionSnapshot(state, conversationId);
        var snapshotOrigin = _conversationWorkspace.GetConversationSnapshotOrigin(conversationId);
        if (RemoteConversationWorkspaceSnapshotPolicy.HasAuthoritativeRemoteRuntimeProjection(
                binding,
                snapshot,
                snapshotOrigin))
        {
            return true;
        }

        return ConversationProjectionReadinessPolicy.HasReusableWarmProjection(
            state,
            conversationId,
            snapshot);
    }

    private bool HasReusableWarmSelectionProjection(ChatState state, string conversationId)
        => HasReusableWarmProjection(state, conversationId);

    private AcpSessionRecoveryStartResult GetOrStartRemoteSessionRecoveryProjection(
        IChatService chatService,
        AcpSessionRecoveryMode recoveryMode,
        string conversationId,
        ConversationBindingSlice binding,
        string? connectionInstanceId,
        string? cwd,
        IAcpSessionUpdateBufferController? adapter)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return new AcpSessionRecoveryStartResult(
                Task.FromException<AcpSessionRecoveryProjection>(
                    new InvalidOperationException("Cannot recover remote session without a working directory.")),
                new( null, OwnsRecoveryLease: false, CancellationToken.None),
                null,
                null,
                null,
                null);
        }

        if (string.IsNullOrWhiteSpace(binding.RemoteSessionId))
        {
            return new AcpSessionRecoveryStartResult(
                Task.FromException<AcpSessionRecoveryProjection>(
                    new InvalidOperationException("Cannot recover a remote session without a remote session id.")),
                default,
                null,
                null,
                null,
                null);
        }

        var key = new RemoteSessionRecoveryLeaseKey(
            recoveryMode,
            conversationId,
            binding.ProfileId,
            connectionInstanceId,
            binding.RemoteSessionId!,
            cwd);

        List<RemoteSessionRecoveryRequest> requestsToCancel;
        RemoteSessionRecoveryRequest requestToStart;
        AcpSessionRecoveryBufferScope resolvedBufferScope;
        Task? conflictingRecoveryCompletion;
        lock (_remoteSessionRecoveryRequestsSync)
        {
            var decision = RemoteSessionRecoveryLeasePolicy.Decide(
                key,
                _remoteSessionRecoveryRequests.Keys.ToArray());
            if (TryReuseRemoteSessionRecoveryRequest(
                    decision,
                    chatService,
                    recoveryMode,
                    conversationId,
                    binding,
                    connectionInstanceId,
                    cwd,
                    adapter,
                    out var reuseResult))
            {
                return reuseResult;
            }

            requestsToCancel = RemoveConflictingRemoteSessionRecoveryRequestsLocked(decision);
            requestToStart = new RemoteSessionRecoveryRequest(
                CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token));
            resolvedBufferScope = RegisterRemoteSessionRecoveryRequestLocked(
                key,
                requestToStart,
                binding.RemoteSessionId!,
                recoveryMode,
                adapter);
            conflictingRecoveryCompletion = requestsToCancel.Count == 0
                ? null
                : System.Threading.Tasks.Task.WhenAll(
                    requestsToCancel.Select(static request => request.CompletionOrExecutionTask));
        }

        foreach (var request in requestsToCancel)
        {
            request.Cancel();
        }

        _ = RemoveRemoteSessionRecoveryRequestWhenCompleteAsync(key, requestToStart);
        return CreateRemoteSessionRecoveryStartResult(
            requestToStart,
            resolvedBufferScope,
            conflictingRecoveryCompletion,
            key,
            chatService,
            recoveryMode,
            conversationId,
            binding,
            connectionInstanceId,
            cwd,
            adapter);
    }

    private bool TryReuseRemoteSessionRecoveryRequest(
        RemoteSessionRecoveryLeaseDecision decision,
        IChatService chatService,
        AcpSessionRecoveryMode recoveryMode,
        string conversationId,
        ConversationBindingSlice binding,
        string? connectionInstanceId,
        string cwd,
        IAcpSessionUpdateBufferController? adapter,
        out AcpSessionRecoveryStartResult startResult)
    {
        startResult = default;
        if (decision.Kind != RemoteSessionRecoveryLeaseDecisionKind.ReuseExisting
            || decision.ExistingLeaseToReuse is not { } existingLease
            || !_remoteSessionRecoveryRequests.TryGetValue(existingLease, out var existing))
        {
            return false;
        }

        Logger.LogInformation(
            "Reusing in-flight remote session recovery request. RecoveryMode={RecoveryMode} RemoteSessionId={RemoteSessionId} ProfileId={ProfileId} ConnectionInstanceId={ConnectionInstanceId}",
            recoveryMode,
            binding.RemoteSessionId,
            binding.ProfileId,
            connectionInstanceId);
        startResult = CreateRemoteSessionRecoveryStartResult(
            existing,
            new AcpSessionRecoveryBufferScope(
                existing.HydrationAttemptId,
                OwnsRecoveryLease: false,
                existing.Token),
            conflictingRecoveryCompletion: null,
            recoveryLeaseKey: existingLease,
            chatService,
            recoveryMode,
            conversationId,
            binding,
            connectionInstanceId,
            cwd,
            adapter,
            canStartRecoveryTransport: false);
        return true;
    }

    private AcpSessionRecoveryBufferScope RegisterRemoteSessionRecoveryRequestLocked(
        RemoteSessionRecoveryLeaseKey key,
        RemoteSessionRecoveryRequest request,
        string remoteSessionId,
        AcpSessionRecoveryMode recoveryMode,
        IAcpSessionUpdateBufferController? adapter)
    {
        EnsureRemoteSessionRecoveryBufferingScope(
            request,
            adapter,
            remoteSessionId,
            recoveryMode);
        _remoteSessionRecoveryRequests[key] = request;
        return new AcpSessionRecoveryBufferScope(
            request.HydrationAttemptId,
            OwnsRecoveryLease: true,
            request.Token);
    }

    private AcpSessionRecoveryStartResult CreateRemoteSessionRecoveryStartResult(
        RemoteSessionRecoveryRequest request,
        AcpSessionRecoveryBufferScope bufferScope,
        Task? conflictingRecoveryCompletion,
        RemoteSessionRecoveryLeaseKey recoveryLeaseKey,
        IChatService chatService,
        AcpSessionRecoveryMode recoveryMode,
        string conversationId,
        ConversationBindingSlice binding,
        string? connectionInstanceId,
        string? cwd,
        IAcpSessionUpdateBufferController? adapter,
        bool canStartRecoveryTransport = true)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return new AcpSessionRecoveryStartResult(
                Task.FromException<AcpSessionRecoveryProjection>(
                    new InvalidOperationException("Cannot recover remote session without a working directory.")),
                new( null, OwnsRecoveryLease: false, CancellationToken.None),
                null,
                null,
                null,
                null);
        }

        return new AcpSessionRecoveryStartResult(
            request.Task,
            bufferScope,
            conflictingRecoveryCompletion,
            request,
            recoveryLeaseKey,
            canStartRecoveryTransport
                ? () => StartRemoteSessionRecoveryProjection(
                chatService,
                recoveryMode,
                conversationId,
                binding,
                connectionInstanceId,
                binding.RemoteSessionId!,
                cwd,
                request,
                adapter)
                : null);
    }

    private void StartRemoteSessionRecoveryProjection(
        IChatService chatService,
        AcpSessionRecoveryMode recoveryMode,
        string conversationId,
        ConversationBindingSlice binding,
        string? connectionInstanceId,
        string remoteSessionId,
        string cwd,
        RemoteSessionRecoveryRequest request,
        IAcpSessionUpdateBufferController? adapter)
    {
        EnsureRemoteSessionRecoveryBufferingScope(
            request,
            adapter,
            remoteSessionId,
            recoveryMode);

        request.Start(_ => RunRemoteSessionRecoveryProjectionAsync(
            chatService,
            recoveryMode,
            conversationId,
            binding,
            connectionInstanceId,
            remoteSessionId,
            cwd,
            request,
            adapter,
            request.HydrationAttemptId));
    }

    private void EnsureRemoteSessionRecoveryBufferingScope(
        RemoteSessionRecoveryRequest request,
        IAcpSessionUpdateBufferController? adapter,
        string remoteSessionId,
        AcpSessionRecoveryMode recoveryMode)
    {
        if (adapter is null
            || request.HydrationAttemptId.HasValue
            || recoveryMode != AcpSessionRecoveryMode.Load)
        {
            return;
        }

        request.HydrationAttemptId = adapter.BeginHydrationBufferingScope(remoteSessionId);
        request.MarkBufferingStarted();
    }

    private async Task<AcpSessionRecoveryProjection> RunRemoteSessionRecoveryProjectionAsync(
        IChatService chatService,
        AcpSessionRecoveryMode recoveryMode,
        string conversationId,
        ConversationBindingSlice binding,
        string? connectionInstanceId,
        string remoteSessionId,
        string cwd,
        RemoteSessionRecoveryRequest request,
        IAcpSessionUpdateBufferController? adapter,
        long? hydrationAttemptId)
    {
        if (recoveryMode == AcpSessionRecoveryMode.Load)
        {
            return await RunRemoteSessionLoadRecoveryProjectionAsync(
                    chatService,
                    conversationId,
                    binding,
                    connectionInstanceId,
                    remoteSessionId,
                    cwd,
                    request,
                    adapter,
                    hydrationAttemptId)
                .ConfigureAwait(false);
        }

        var requestToken = request.Token;
        var mcpServers = await ResolveCurrentMcpServersAsync(requestToken).ConfigureAwait(false);
        var resumeTask = chatService.ResumeSessionAsync(
            CreateSessionResumeParams(remoteSessionId, cwd, mcpServers),
            requestToken);
        request.TrackTransportTask(resumeTask);
        _ = ObserveRemoteSessionRecoveryTransportTaskAsync(resumeTask, recoveryMode, remoteSessionId);
        var projection = AcpSessionRecoveryProjection.FromResume(
            await resumeTask
                .WaitAsync(requestToken)
                .ConfigureAwait(false));
        var wasPublished = await PublishRemoteSessionRecoveryProjectionAsync(
                conversationId,
                binding,
                connectionInstanceId,
                projection,
                adapter: null,
                hydrationAttemptId: null,
                requestToken)
            .ConfigureAwait(false);
        return projection with { WasPublished = wasPublished };
    }

    private async Task<AcpSessionRecoveryProjection> RunRemoteSessionLoadRecoveryProjectionAsync(
        IChatService chatService,
        string conversationId,
        ConversationBindingSlice binding,
        string? connectionInstanceId,
        string remoteSessionId,
        string cwd,
        RemoteSessionRecoveryRequest request,
        IAcpSessionUpdateBufferController? adapter,
        long? hydrationAttemptId)
    {
        var requestToken = request.Token;
        var mcpServers = await ResolveCurrentMcpServersAsync(requestToken).ConfigureAwait(false);
        var loadTask = chatService.LoadSessionAsync(
            CreateSessionLoadParams(remoteSessionId, cwd, mcpServers),
            requestToken);
        request.TrackTransportTask(loadTask);

        _ = ObserveRemoteSessionRecoveryTransportTaskAsync(
            loadTask,
            AcpSessionRecoveryMode.Load,
            remoteSessionId);
        try
        {
            var projection = AcpSessionRecoveryProjection.FromLoad(
                await loadTask
                    .WaitAsync(requestToken)
                    .ConfigureAwait(false));
            var wasPublished = await PublishRemoteSessionRecoveryProjectionAsync(
                    conversationId,
                    binding,
                    connectionInstanceId,
                    projection,
                    adapter,
                    hydrationAttemptId,
                    requestToken)
                .ConfigureAwait(false);
            return projection with { WasPublished = wasPublished };
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            ReleaseBufferedUpdatesAfterInterruptedHydration(
                adapter,
                hydrationAttemptId,
                ownsHydrationScope: true,
                "RemoteSessionRecoveryCanceled");
            throw;
        }
        catch
        {
            ReleaseBufferedUpdatesAfterInterruptedHydration(
                adapter,
                hydrationAttemptId,
                ownsHydrationScope: true,
                "RemoteSessionRecoveryFailed");
            throw;
        }
    }

    private static SessionLoadParams CreateSessionLoadParams(
        string remoteSessionId,
        string cwd,
        IReadOnlyList<McpServer> mcpServers)
        => new(remoteSessionId, cwd, McpServerJsonConverter.CloneServers(mcpServers));

    private static SessionResumeParams CreateSessionResumeParams(
        string remoteSessionId,
        string cwd,
        IReadOnlyList<McpServer> mcpServers)
        => new(remoteSessionId, cwd, McpServerJsonConverter.CloneServers(mcpServers));

    private async Task<bool> PublishRemoteSessionRecoveryProjectionAsync(
        string conversationId,
        ConversationBindingSlice expectedBinding,
        string? expectedConnectionInstanceId,
        AcpSessionRecoveryProjection projection,
        IAcpSessionUpdateBufferController? adapter,
        long? hydrationAttemptId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(conversationId)
            || string.IsNullOrWhiteSpace(expectedBinding.RemoteSessionId))
        {
            ReleaseBufferedUpdatesAfterInterruptedHydration(
                adapter,
                hydrationAttemptId,
                ownsHydrationScope: true,
                "RemoteSessionRecoveryIdentityMissing");
            return false;
        }

        var currentBinding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (currentBinding is null
            || !string.Equals(currentBinding.RemoteSessionId, expectedBinding.RemoteSessionId, StringComparison.Ordinal)
            || !string.Equals(currentBinding.ProfileId, expectedBinding.ProfileId, StringComparison.Ordinal))
        {
            Logger.LogDebug(
                "Discarding remote session recovery projection because binding changed. ConversationId={ConversationId} ExpectedRemoteSessionId={ExpectedRemoteSessionId} CurrentRemoteSessionId={CurrentRemoteSessionId}",
                conversationId,
                expectedBinding.RemoteSessionId,
                currentBinding?.RemoteSessionId);
            ReleaseBufferedUpdatesAfterInterruptedHydration(
                adapter,
                hydrationAttemptId,
                ownsHydrationScope: true,
                "RemoteSessionRecoveryBindingChanged");
            return false;
        }

        var currentConnection = await ResolveAuthoritativeForegroundConnectionAsync(
                expectedBinding.ProfileId,
                cancellationToken)
            .ConfigureAwait(false);
        if (currentConnection is null
            || !string.Equals(currentConnection.Value.ConnectionInstanceId, expectedConnectionInstanceId, StringComparison.Ordinal))
        {
            Logger.LogDebug(
                "Discarding remote session recovery projection because connection identity changed. ConversationId={ConversationId} ExpectedConnectionInstanceId={ExpectedConnectionInstanceId} CurrentConnectionInstanceId={CurrentConnectionInstanceId}",
                conversationId,
                expectedConnectionInstanceId,
                currentConnection?.ConnectionInstanceId);
            ReleaseBufferedUpdatesAfterInterruptedHydration(
                adapter,
                hydrationAttemptId,
                ownsHydrationScope: true,
                "RemoteSessionRecoveryConnectionChanged");
            return false;
        }

        var bufferingCompleted = CompleteRemoteSessionRecoveryBufferingScope(
            adapter,
            hydrationAttemptId,
            "RemoteSessionRecoveryCompleted");
        if (!bufferingCompleted)
        {
            Logger.LogWarning(
                "Discarding remote session recovery projection because buffering attempt is stale. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                conversationId,
                expectedBinding.RemoteSessionId);
            return false;
        }

        await ApplySessionLoadResponseAsync(conversationId, projection.SessionLoadResponse).ConfigureAwait(true);
        await AwaitBufferedSessionReplayProjectionAsync(cancellationToken, hydrationAttemptId).ConfigureAwait(false);
        await SetConversationRuntimeStateAsync(
                conversationId,
                ConversationRuntimePhase.Warm,
                currentBinding,
                projection.CompletedRuntimeReason,
                cancellationToken,
                connectionInstanceId: expectedConnectionInstanceId)
            .ConfigureAwait(false);
        return true;
    }

    private async Task ObserveRemoteSessionRecoveryTransportTaskAsync<TResponse>(
        Task<TResponse> transportTask,
        AcpSessionRecoveryMode recoveryMode,
        string remoteSessionId)
    {
        try
        {
            await transportTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Remote session recovery transport task faulted after the activation waiter stopped observing it. RecoveryMode={RecoveryMode} RemoteSessionId={RemoteSessionId}",
                recoveryMode,
                remoteSessionId);
        }
    }

    private async Task AwaitConflictingRemoteSessionRecoveryCompletionAsync(
        Task conflictingRecoveryCompletion,
        AcpSessionRecoveryMode recoveryMode,
        string remoteSessionId)
    {
        try
        {
            await conflictingRecoveryCompletion.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogDebug(
                ex,
                "Ignoring superseded remote session recovery completion failure. RecoveryMode={RecoveryMode} RemoteSessionId={RemoteSessionId}",
                recoveryMode,
                remoteSessionId);
        }
    }

    private bool TryCancelUnstartedOwnedRemoteSessionRecovery(
        RemoteSessionRecoveryRequest? request,
        RemoteSessionRecoveryLeaseKey? leaseKey,
        bool ownsRecoveryLease)
    {
        if (!ownsRecoveryLease || request is null || leaseKey is null || request.HasStarted)
        {
            return false;
        }

        lock (_remoteSessionRecoveryRequestsSync)
        {
            if (!_remoteSessionRecoveryRequests.TryGetValue(leaseKey.Value, out var current)
                || !ReferenceEquals(current, request)
                || request.HasStarted)
            {
                return false;
            }

            _remoteSessionRecoveryRequests.Remove(leaseKey.Value);
        }

        request.Cancel();
        return true;
    }

    private List<RemoteSessionRecoveryRequest> RemoveConflictingRemoteSessionRecoveryRequestsLocked(RemoteSessionRecoveryLeaseDecision decision)
    {
        if (decision.ConflictingLeasesToCancel.Count == 0)
        {
            return [];
        }

        List<RemoteSessionRecoveryRequest>? requestsToCancel = null;
        foreach (var candidateKey in decision.ConflictingLeasesToCancel)
        {
            if (_remoteSessionRecoveryRequests.Remove(candidateKey, out var candidateRequest))
            {
                (requestsToCancel ??= []).Add(candidateRequest);
            }
        }

        return requestsToCancel ?? [];
    }

    private void CancelAndClearRemoteSessionRecoveryRequests(string reason)
    {
        List<RemoteSessionRecoveryRequest> requestsToCancel;
        lock (_remoteSessionRecoveryRequestsSync)
        {
            if (_remoteSessionRecoveryRequests.Count == 0)
            {
                return;
            }

            requestsToCancel = _remoteSessionRecoveryRequests.Values.ToList();
            _remoteSessionRecoveryRequests.Clear();
        }

        foreach (var request in requestsToCancel)
        {
            request.Cancel();
        }

        Logger.LogInformation(
            "Canceling in-flight remote session recovery requests. Count={Count} Reason={Reason}",
            requestsToCancel.Count,
            reason);
    }

    private async Task RemoveRemoteSessionRecoveryRequestWhenCompleteAsync(
        RemoteSessionRecoveryLeaseKey key,
        RemoteSessionRecoveryRequest request)
    {
        try
        {
            await request.Task.ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            try
            {
                await request.ExecutionTask.ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await request.TransportTask.ConfigureAwait(false);
            }
            catch
            {
            }

            lock (_remoteSessionRecoveryRequestsSync)
            {
                if (_remoteSessionRecoveryRequests.TryGetValue(key, out var current)
                    && ReferenceEquals(current, request))
                {
                    _remoteSessionRecoveryRequests.Remove(key);
                }
            }

            request.Dispose();
        }
    }

    private ConversationWorkspaceSnapshot? ResolveReusableWarmProjectionSnapshot(ChatState state, string conversationId)
    {
        var snapshot = _conversationWorkspace.GetConversationSnapshot(conversationId);
        var binding = ResolveReusableWarmProjectionBinding(state, conversationId);
        return RemoteConversationWorkspaceSnapshotPolicy.CanReuseWarmProjectionSnapshot(
            binding,
            snapshot,
            _conversationWorkspace.GetConversationSnapshotOrigin(conversationId))
            ? snapshot
            : null;
    }

    private ConversationBindingSlice? ResolveReusableWarmProjectionBinding(ChatState state, string conversationId)
    {
        var binding = state.ResolveBinding(conversationId);
        if (binding is not null)
        {
            return binding;
        }

        var workspaceBinding = _conversationWorkspace.GetRemoteBinding(conversationId);
        return workspaceBinding is null
            ? null
            : new ConversationBindingSlice(
                workspaceBinding.ConversationId,
                workspaceBinding.RemoteSessionId,
                workspaceBinding.BoundProfileId);
    }

    private async Task<int> GetProjectedTranscriptCountAsync(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return 0;
        }

        var state = await _chatStore.GetCurrentStateAsync();
        var projectedTranscript = state.ResolveContentSlice(conversationId)?.Transcript
            ?? (string.Equals(state.HydratedConversationId, conversationId, StringComparison.Ordinal)
                ? state.Transcript
                : null);
        return projectedTranscript?.Count(static message => !IsThinkingPlaceholder(message)) ?? 0;
    }

    private static void ReleaseBufferedUpdatesAfterInterruptedHydration(
        IAcpSessionUpdateBufferController? adapter,
        long? hydrationAttemptId,
        bool ownsHydrationScope,
        string reason)
    {
        if (adapter is null || !hydrationAttemptId.HasValue || !ownsHydrationScope)
        {
            return;
        }

        adapter.SuppressBufferedUpdates(hydrationAttemptId.Value, reason);
    }

    private static bool CompleteRemoteSessionRecoveryBufferingScope(
        IAcpSessionUpdateBufferController? adapter,
        long? hydrationAttemptId,
        string reason)
    {
        if (adapter is null || !hydrationAttemptId.HasValue)
        {
            return true;
        }

        return adapter.TryMarkHydrated(hydrationAttemptId.Value, reason: reason);
    }

    private async Task<ServerConfiguration?> ResolveProfileConfigurationAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        return await LoadCanonicalProfileConfigurationAsync(profileId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsRemoteConnectionReadyAsync(
        string? requiredProfileId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (await ResolveAuthoritativeForegroundConnectionAsync(requiredProfileId, cancellationToken).ConfigureAwait(false))
            .HasValue;
    }

    private async Task<ConversationWarmReuseConnectionIdentity> ResolveWarmReuseConnectionIdentityAsync(
        ConversationBindingSlice? binding,
        CancellationToken cancellationToken)
    {
        var authoritativeConnection = await ResolveAuthoritativeForegroundConnectionAsync(
                binding?.ProfileId,
                cancellationToken)
            .ConfigureAwait(false);

        return authoritativeConnection is { } resolvedConnection
            ? new ConversationWarmReuseConnectionIdentity(
                resolvedConnection.ProfileId,
                resolvedConnection.ConnectionInstanceId)
            : default;
    }

    private async Task<string?> ResolveProjectionRestoreConnectionInstanceIdAsync()
    {
        var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
        return ConversationProjectionRestoreConnectionPolicy.ResolveCurrentConnectionInstanceId(
            connectionState,
            ConnectionInstanceId);
    }

    private async Task<bool> WaitForRemoteConnectionReadyAsync(
        string? requiredProfileId,
        CancellationToken cancellationToken)
    {
        while (!await IsRemoteConnectionReadyAsync(requiredProfileId, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
            if (connectionState.Phase is not ConnectionPhase.Connecting
                && connectionState.Phase is not ConnectionPhase.Initializing)
            {
                return false;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<AcpAuthoritativeConnectionSnapshot?> ResolveAuthoritativeForegroundConnectionAsync(
        string? requiredProfileId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
        return _authoritativeConnectionResolver.TryResolveReadyForegroundConnection(
            _chatService,
            connectionState,
            requiredProfileId,
            out var snapshot)
            ? snapshot
            : null;
    }

    private Task SetConversationRuntimeStateAsync(
        string conversationId,
        ConversationRuntimePhase phase,
        string? reason,
        CancellationToken cancellationToken)
        => SetConversationRuntimeStateAsync(
            conversationId,
            phase,
            binding: null,
            reason,
            cancellationToken,
            connectionInstanceId: null);

    private async Task SetConversationRuntimeStateAsync(
        string conversationId,
        ConversationRuntimePhase phase,
        ConversationBindingSlice? binding,
        string? reason,
        CancellationToken cancellationToken,
        string? connectionInstanceId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var currentState = await _chatStore.GetCurrentStateAsync();
        var existingRuntime = currentState.ResolveRuntimeState(conversationId);
        var preserveExistingConnectionInstanceId =
            connectionInstanceId is null
            && phase is ConversationRuntimePhase.Selecting or ConversationRuntimePhase.Selected;
        var effectiveConnectionInstanceId =
            connectionInstanceId
            ?? (preserveExistingConnectionInstanceId ? existingRuntime?.ConnectionInstanceId : null)
            ?? ConnectionInstanceId;
        var remoteSessionId = binding?.RemoteSessionId;
        var profileId = binding?.ProfileId;
        if (string.IsNullOrWhiteSpace(remoteSessionId) || string.IsNullOrWhiteSpace(profileId))
        {
            remoteSessionId ??= existingRuntime?.RemoteSessionId ?? currentState.ResolveBinding(conversationId)?.RemoteSessionId;
            profileId ??= existingRuntime?.ProfileId ?? currentState.ResolveBinding(conversationId)?.ProfileId;
        }

        var runtimeState = new ConversationRuntimeSlice(
            ConversationId: conversationId,
            Phase: phase,
            ConnectionInstanceId: effectiveConnectionInstanceId,
            RemoteSessionId: remoteSessionId,
            ProfileId: profileId,
            Reason: reason,
            UpdatedAtUtc: DateTime.UtcNow);
        await _chatStore.Dispatch(new SetConversationRuntimeStateAction(runtimeState)).ConfigureAwait(false);
        Logger.LogInformation(
            "Conversation runtime stage transitioned. ConversationId={ConversationId} Stage={Stage} ConnectionInstanceId={ConnectionInstanceId} RemoteSessionId={RemoteSessionId} ProfileId={ProfileId} Reason={Reason}",
            conversationId,
            phase,
            effectiveConnectionInstanceId,
            remoteSessionId,
            profileId,
            reason);
    }

    private bool ShouldOwnRemoteHydrationUi(string conversationId, long? activationVersion)
        => IsChatShellVisibleForRemoteUi
            && (activationVersion.HasValue
                ? _conversationActivationOrchestrator.IsLatestActivationVersion(activationVersion.Value)
                : string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal));

    private async Task<bool> TryAcquireConnectionLifecycleOverlayAsync(
        string conversationId,
        long? activationVersion)
    {
        if (!ShouldOwnRemoteHydrationUi(conversationId, activationVersion))
        {
            return false;
        }

        await PostToUiAsync(() =>
        {
            SetConversationOverlayOwners(
                sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                connectionLifecycleConversationId: conversationId,
                historyConversationId: _historyOverlayConversationId);
        }).ConfigureAwait(false);
        return true;
    }

    private Task ReleaseConnectionLifecycleOverlayAsync(string conversationId)
        => PostToUiAsync(() =>
        {
            if (!string.Equals(_connectionLifecycleOverlayConversationId, conversationId, StringComparison.Ordinal))
            {
                return;
            }

            SetConversationOverlayOwners(
                sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                connectionLifecycleConversationId: null,
                historyConversationId: _historyOverlayConversationId);
        });

    private AcpHydrationCompletionMode ResolveHydrationCompletionMode(string? configuredMode)
    {
        if (Enum.TryParse<AcpHydrationCompletionMode>(configuredMode, ignoreCase: true, out var mode))
        {
            return mode;
        }

        if (!string.IsNullOrWhiteSpace(configuredMode))
        {
            Logger.LogWarning(
                "Unknown ACP hydration completion mode '{ConfiguredMode}'. Falling back to StrictReplay.",
                configuredMode);
        }

        return AcpHydrationCompletionMode.StrictReplay;
    }

    private async Task<ConversationSessionInfoSnapshot?> LoadRemoteSessionInfoSnapshotFromSsotAsync(
        string conversationId,
        ConversationBindingSlice binding,
        IChatService chatService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(conversationId)
            || string.IsNullOrWhiteSpace(binding.RemoteSessionId)
            || chatService.AgentCapabilities?.SessionCapabilities?.List is null)
        {
            return null;
        }

        AgentSessionInfo? sessionInfo;
        try
        {
            sessionInfo = await FindRemoteSessionInfoAsync(chatService, binding.RemoteSessionId!, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Failed to refresh remote session metadata before hydration. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                conversationId,
                binding.RemoteSessionId);
            return null;
        }

        if (sessionInfo is null)
        {
            return null;
        }

        return ToConversationSessionInfoSnapshot(new AcpSessionInfoSnapshot(
            sessionInfo.Title,
            HasTitle: true,
            sessionInfo.Description,
            string.IsNullOrWhiteSpace(sessionInfo.Cwd) ? null : sessionInfo.Cwd,
            sessionInfo.UpdatedAt,
            HasUpdatedAt: true,
            sessionInfo.Meta));
    }

    private async Task ApplyRemoteSessionInfoSnapshotWhenReadyAsync(
        string conversationId,
        ConversationBindingSlice expectedBinding,
        string? expectedConnectionInstanceId,
        int expectedChatServiceGeneration,
        Task<ConversationSessionInfoSnapshot?> sessionInfoTask,
        CancellationToken cancellationToken)
    {
        ConversationSessionInfoSnapshot? sessionInfo;
        try
        {
            sessionInfo = await sessionInfoTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Failed to apply asynchronous remote session metadata refresh. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                conversationId,
                expectedBinding.RemoteSessionId);
            return;
        }

        if (sessionInfo is null)
        {
            return;
        }

        sessionInfo = NormalizeSessionInfoSnapshotForEstablishedConversationContext(conversationId, sessionInfo);

        var currentBinding = await ResolveConversationBindingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (currentBinding is null
            || !string.Equals(currentBinding.RemoteSessionId, expectedBinding.RemoteSessionId, StringComparison.Ordinal)
            || !string.Equals(currentBinding.ProfileId, expectedBinding.ProfileId, StringComparison.Ordinal))
        {
            Logger.LogDebug(
                "Discarding asynchronous remote session metadata refresh because binding changed. ConversationId={ConversationId} ExpectedRemoteSessionId={ExpectedRemoteSessionId} CurrentRemoteSessionId={CurrentRemoteSessionId}",
                conversationId,
                expectedBinding.RemoteSessionId,
                currentBinding?.RemoteSessionId);
            return;
        }

        var currentConnectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
        var currentConnectionInstanceId = !string.IsNullOrWhiteSpace(currentConnectionState.ConnectionInstanceId)
            ? currentConnectionState.ConnectionInstanceId
            : ConnectionInstanceId;
        if (expectedChatServiceGeneration != _foregroundChatServiceGeneration
            || !string.Equals(currentConnectionInstanceId, expectedConnectionInstanceId, StringComparison.Ordinal))
        {
            Logger.LogDebug(
                "Discarding asynchronous remote session metadata refresh because connection identity changed. ConversationId={ConversationId} ExpectedConnectionInstanceId={ExpectedConnectionInstanceId} CurrentConnectionInstanceId={CurrentConnectionInstanceId}",
                conversationId,
                expectedConnectionInstanceId,
                currentConnectionInstanceId);
            return;
        }

        await ApplySessionInfoSnapshotProjectionAsync(
                conversationId,
                sessionInfo,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string?> ResolveRecoverySessionCwdOrSessionListAsync(
        IChatService chatService,
        ConversationBindingSlice binding,
        string conversationId,
        CancellationToken cancellationToken)
    {
        var establishedCwd = GetSessionCwdOrDefault(conversationId);
        if (!string.IsNullOrWhiteSpace(establishedCwd))
        {
            return establishedCwd;
        }

        var remoteSessionInfo = await LoadRemoteSessionInfoSnapshotFromSsotAsync(
            conversationId,
            binding,
            chatService,
            cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(remoteSessionInfo?.Cwd))
        {
            return null;
        }

        var normalizedCwd = remoteSessionInfo.Cwd.Trim();
        await ApplySessionInfoSnapshotProjectionAsync(conversationId, remoteSessionInfo, cancellationToken)
            .ConfigureAwait(false);

        return normalizedCwd;
    }

    private async Task ApplySessionInfoSnapshotProjectionAsync(
        string conversationId,
        ConversationSessionInfoSnapshot sessionInfo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || sessionInfo is null)
        {
            return;
        }

        await _chatStore.Dispatch(new MergeConversationSessionStateAction(
            conversationId,
            SessionInfo: ConversationSessionInfoSnapshots.Clone(sessionInfo))).ConfigureAwait(false);
        await PersistProjectedSessionInfoSnapshotAsync(conversationId, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistProjectedSessionInfoSnapshotAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var storeState = await _chatStore.GetCurrentStateAsync();
        var sessionInfo = ConversationSessionInfoSnapshots.Clone(storeState.ResolveSessionStateSlice(conversationId)?.SessionInfo);
        if (sessionInfo is null)
        {
            return;
        }

        await _conversationWorkspace
            .ApplySessionInfoSnapshotAsync(
                conversationId,
                sessionInfo,
                allowRegisterWhenMissing: true,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            await PostToUiAsync(() =>
            {
                if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
                {
                    RefreshCurrentSessionDisplayName();
                }
            }).ConfigureAwait(false);
        }
    }

    private static DateTime? ParseSessionUpdatedAtUtc(string? updatedAt)
        => AcpSessionTimestampPolicy.ParseUpdatedAtUtc(updatedAt);

    private static async Task<AgentSessionInfo?> FindRemoteSessionInfoAsync(
        IChatService chatService,
        string remoteSessionId,
        CancellationToken cancellationToken)
    {
        if (chatService.AgentCapabilities?.SessionCapabilities?.List is null)
        {
            return null;
        }

        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await chatService
                .ListSessionsAsync(new SessionListParams { Cursor = cursor }, cancellationToken)
                .ConfigureAwait(false);
            var match = response?.Sessions?.FirstOrDefault(session =>
                string.Equals(session.SessionId, remoteSessionId, StringComparison.Ordinal));
            if (match != null)
            {
                return match;
            }

            cursor = response?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return null;
    }

    private ConversationSessionInfoSnapshot NormalizeSessionInfoSnapshotForEstablishedConversationContext(
        string conversationId,
        ConversationSessionInfoSnapshot sessionInfo)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return sessionInfo;
        }

        var establishedCwd = _sessionManager.GetSession(conversationId)?.Cwd?.Trim();
        if (string.IsNullOrWhiteSpace(establishedCwd))
        {
            var remoteSessionId = _conversationWorkspace.GetRemoteBinding(conversationId)?.RemoteSessionId;
            if (!string.IsNullOrWhiteSpace(remoteSessionId))
            {
                establishedCwd = _sessionManager.GetSession(remoteSessionId)?.Cwd?.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(establishedCwd))
        {
            establishedCwd = TryGetConversationSnapshot(conversationId)?.SessionInfo?.Cwd?.Trim();
        }

        if (string.IsNullOrWhiteSpace(establishedCwd))
        {
            return sessionInfo;
        }

        var incomingCwd = sessionInfo.Cwd?.Trim();
        if (string.Equals(incomingCwd, establishedCwd, StringComparison.Ordinal))
        {
            return sessionInfo;
        }

        // ACP session/list is a discovery API and must not redefine immutable session setup
        // fields such as cwd after session/load or session/new has already established them.
        return new ConversationSessionInfoSnapshot
        {
            Title = sessionInfo.Title,
            HasTitle = sessionInfo.HasTitle,
            Description = sessionInfo.Description,
            Cwd = establishedCwd,
            UpdatedAtUtc = sessionInfo.UpdatedAtUtc,
            HasUpdatedAt = sessionInfo.HasUpdatedAt,
            Meta = sessionInfo.Meta is null
                ? null
                : new Dictionary<string, object?>(sessionInfo.Meta, StringComparer.Ordinal)
        };
    }

    private void SetConversationOverlayOwners(
        string? sessionSwitchConversationId,
        string? connectionLifecycleConversationId,
        string? historyConversationId)
    {
        if (string.Equals(_sessionSwitchOverlayConversationId, sessionSwitchConversationId, StringComparison.Ordinal)
            && string.Equals(_connectionLifecycleOverlayConversationId, connectionLifecycleConversationId, StringComparison.Ordinal)
            && string.Equals(_historyOverlayConversationId, historyConversationId, StringComparison.Ordinal))
        {
            return;
        }

        _sessionSwitchOverlayConversationId = sessionSwitchConversationId;
        _connectionLifecycleOverlayConversationId = connectionLifecycleConversationId;
        _historyOverlayConversationId = historyConversationId;
        if (string.IsNullOrWhiteSpace(historyConversationId)
            || !string.Equals(_pendingHistoryOverlayDismissConversationId, historyConversationId, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(historyConversationId))
            {
                _pendingHistoryOverlayDismissConversationId = null;
            }
        }

        ResetHydrationOverlayPhaseIfOwnerChanged(historyConversationId);
#if DEBUG
        Logger.LogInformation(
            "Overlay owners updated. currentSession={CurrentSessionId} sessionSwitch={SessionSwitchConversationId} connectionLifecycle={ConnectionLifecycleConversationId} history={HistoryConversationId} pendingHistoryDismiss={PendingHistoryDismissConversationId} isHydrating={IsHydrating} isRemoteHydrationPending={IsRemoteHydrationPending}",
            CurrentSessionId,
            _sessionSwitchOverlayConversationId,
            _connectionLifecycleOverlayConversationId,
            _historyOverlayConversationId,
            _pendingHistoryOverlayDismissConversationId,
            IsHydrating,
            IsRemoteHydrationPending);
#endif
        RaiseOverlayStateChanged();
    }

    private void ScheduleSessionSwitchOverlayDismissal(long activationVersion, string conversationId)
    {
        _uiDispatcher.Enqueue(() => {
            DismissSessionSwitchOverlay(activationVersion, conversationId);
        });
    }

    private Task DismissSessionSwitchOverlayAsync(long activationVersion, string conversationId)
        => PostToUiAsync(() => DismissSessionSwitchOverlay(activationVersion, conversationId));

    private void DismissSessionSwitchOverlay(long activationVersion, string conversationId)
    {
        if (!_conversationActivationOrchestrator.IsLatestActivationVersion(activationVersion)
            || !string.Equals(_sessionSwitchOverlayConversationId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        IsSessionSwitching = false;
        SetConversationOverlayOwners(
            sessionSwitchConversationId: null,
            connectionLifecycleConversationId: _connectionLifecycleOverlayConversationId,
            historyConversationId: _historyOverlayConversationId);
    }

    private void TryCompletePendingHistoryOverlayDismissal(ChatUiProjection projection)
    {
        var hasVisibleTranscript = projection.Transcript.Count > 0;
        if (string.IsNullOrWhiteSpace(projection.HydratedConversationId)
            || !string.Equals(_historyOverlayConversationId, projection.HydratedConversationId, StringComparison.Ordinal)
            || !string.Equals(_pendingHistoryOverlayDismissConversationId, projection.HydratedConversationId, StringComparison.Ordinal)
            || projection.IsHydrating
            || IsRemoteHydrationPending
            || (!hasVisibleTranscript && HasPendingSessionUpdates())
            || !HasSatisfiedKnownTranscriptGrowthRequirement(projection))
        {
            return;
        }

#if DEBUG
        Logger.LogInformation(
            "Completing history overlay dismissal. conversationId={ConversationId} transcriptCount={TranscriptCount} hasVisibleTranscript={HasVisibleTranscript} turnStatusVisible={IsTurnStatusVisible}",
            projection.HydratedConversationId,
            projection.Transcript.Count,
            hasVisibleTranscript,
            projection.IsTurnStatusVisible);
#endif
        var shouldDismissSessionSwitchOverlay =
            string.Equals(_sessionSwitchOverlayConversationId, projection.HydratedConversationId, StringComparison.Ordinal);
        if (shouldDismissSessionSwitchOverlay)
        {
            // Keep the loading lifecycle monotonic: once hydration has produced the final
            // transcript projection, clear the session-switch tail in the same UI pass so
            // the pill cannot regress from "loading chat history" back to "switching chat".
            IsSessionSwitching = false;
        }

        SetConversationOverlayOwners(
            sessionSwitchConversationId: shouldDismissSessionSwitchOverlay ? null : _sessionSwitchOverlayConversationId,
            connectionLifecycleConversationId: _connectionLifecycleOverlayConversationId,
            historyConversationId: null);
        ClearKnownTranscriptGrowthRequirement(projection.HydratedConversationId);
    }

    private bool HasSatisfiedKnownTranscriptGrowthRequirement(ChatUiProjection projection)
    {
        if (string.IsNullOrWhiteSpace(projection.HydratedConversationId)
            || !_remoteHydrationKnownTranscriptBaselineCounts.TryGetValue(projection.HydratedConversationId, out var baselineCount))
        {
            return true;
        }

        if (projection.Transcript.Count > baselineCount)
        {
            return true;
        }

        return !_remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc.TryGetValue(
                projection.HydratedConversationId,
                out var graceDeadlineUtc)
            || DateTime.UtcNow >= graceDeadlineUtc;
    }

    private void ClearKnownTranscriptGrowthRequirement(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _remoteHydrationSessionUpdateBaselineCounts.Remove(conversationId);
        _remoteHydrationKnownTranscriptBaselineCounts.Remove(conversationId);
        _remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc.Remove(conversationId);
    }
}
