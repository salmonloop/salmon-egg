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

    public string? SelectedProfileId => _selectedProfileIdFromStore;

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

        var state = await _chatStore.State ?? ChatState.Empty;
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
        var recoveryMode = AcpSessionRecoveryPolicy.Resolve(chatService.AgentCapabilities);
        if (recoveryMode == AcpSessionRecoveryMode.None)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(binding.RemoteSessionId))
        {
            SetError("Failed to load session: no remote session binding is available for the active conversation.");
            return false;
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
            var requiresTranscriptGrowthObservation = recoveryMode == AcpSessionRecoveryMode.Load && adapter != null;
            var hasCachedTranscript = transcriptBaselineCount > 0;
            var shouldAwaitReplayProjection =
                requiresTranscriptGrowthObservation &&
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
                    if (requiresTranscriptGrowthObservation)
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
                hydrationAttemptId = adapter?.BeginHydrationBufferingScope(binding.RemoteSessionId);
                await ResetConversationProjectionForResyncAsync(conversationId, cancellationToken).ConfigureAwait(false);
                await SetHydrationOverlayPhaseAsync(
                        conversationId,
                        activationVersion,
                        HydrationOverlayPhase.RequestingSessionLoad)
                    .ConfigureAwait(false);
                var sessionLoadTask = chatService.LoadSessionAsync(
                    new SessionLoadParams(binding.RemoteSessionId!, GetSessionCwdOrDefault(conversationId)),
                    cancellationToken);
                recoveryProjection = AcpSessionRecoveryProjection.FromLoad(
                    await sessionLoadTask
                        .WaitAsync(RemoteSessionLoadTimeout, cancellationToken)
                        .ConfigureAwait(false));
                cancellationToken.ThrowIfCancellationRequested();
                if (adapter != null && hydrationAttemptId.HasValue)
                {
                    if (!adapter.TryMarkHydrated(hydrationAttemptId.Value))
                    {
                        Logger.LogWarning(
                            "Discarding remote hydration completion because buffering attempt is stale. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                            conversationId,
                            binding.RemoteSessionId);
                        await RecoverProjectionAfterStaleHydrationAttemptAsync(
                                conversationId,
                                activationVersion,
                                binding,
                                ConversationRuntimePhase.RemoteConnectionReady,
                                cancellationToken)
                            .ConfigureAwait(false);
                        return false;
                    }
                }
            }
            else
            {
                await SetHydrationOverlayPhaseAsync(
                        conversationId,
                        activationVersion,
                        HydrationOverlayPhase.RequestingSessionLoad)
                    .ConfigureAwait(false);
                var sessionResumeTask = chatService.ResumeSessionAsync(
                    new SessionResumeParams(binding.RemoteSessionId!, GetSessionCwdOrDefault(conversationId)),
                    cancellationToken);
                recoveryProjection = AcpSessionRecoveryProjection.FromResume(
                    await sessionResumeTask
                        .WaitAsync(RemoteSessionLoadTimeout, cancellationToken)
                        .ConfigureAwait(false));
                cancellationToken.ThrowIfCancellationRequested();
            }
            await ApplySessionLoadResponseAsync(conversationId, recoveryProjection.SessionLoadResponse).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            if (shouldAwaitReplayProjection)
            {
                await AwaitRemoteReplayProjectionAsync(
                        conversationId,
                        activationVersion,
                        binding.RemoteSessionId!,
                        replayBaseline,
                        transcriptProjectionBaseline,
                        hydrationAttemptId,
                        cancellationToken)
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

            if (recoveryMode == AcpSessionRecoveryMode.Load
                && adapter != null
                && hydrationAttemptId.HasValue
                && !adapter.TryMarkHydrated(hydrationAttemptId.Value, reason: "PostDrainVerification"))
            {
                Logger.LogWarning(
                    "Discarding remote hydration finalization because buffering attempt became stale after replay drain. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                    conversationId,
                    binding.RemoteSessionId);
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
                await _hydrationCoordinator.AwaitKnownTranscriptGrowthRequirementAsync(
                        _hydrationContext,
                        conversationId,
                        transcriptBaselineCount,
                        knownTranscriptGrowthGraceDeadlineUtc,
                        hydrationAttemptId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await RestoreCachedConversationProjectionIfReplayIsEmptyAsync(
                    conversationId)
                .ConfigureAwait(false);
            await ApplyCurrentStoreProjectionAsync(activationVersion).ConfigureAwait(false);
            await SetConversationRuntimeStateAsync(
                    conversationId,
                    ConversationRuntimePhase.Warm,
                    binding,
                    reason: recoveryProjection.CompletedRuntimeReason,
                    cancellationToken,
                    connectionInstanceId: resolvedConnection.ConnectionInstanceId)
                .ConfigureAwait(false);
            _ = ApplyRemoteSessionInfoSnapshotWhenReadyAsync(
                conversationId,
                binding,
                LoadRemoteSessionInfoSnapshotFromSsotAsync(
                    conversationId,
                    binding,
                    chatService,
                    cancellationToken),
                cancellationToken);
            Logger.LogInformation(
                "Conversation hydration completed. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} ElapsedMs={ElapsedMs}",
                conversationId,
                binding.RemoteSessionId,
                hydrationStopwatch.ElapsedMilliseconds);

            return true;
        }
        catch (OperationCanceledException)
        {
            ReleaseBufferedUpdatesAfterInterruptedHydration(adapter, hydrationAttemptId, "RemoteHydrationCanceled");

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
                return false;
            }

            ReleaseBufferedUpdatesAfterInterruptedHydration(adapter, hydrationAttemptId, "DiscoverImportLoadSessionFailed");

            if (AcpErrorClassifier.IsRemoteSessionNotFound(ex))
            {
                Logger.LogWarning(
                    ex,
                    "Remote session binding became stale during hydration. Clearing binding. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId}",
                    conversationId,
                    binding.RemoteSessionId);
                await ClearRemoteBindingAsync(conversationId, binding.ProfileId).ConfigureAwait(false);
                await SetConversationRuntimeStateAsync(
                        conversationId,
                        ConversationRuntimePhase.Stale,
                        binding,
                        reason: "RemoteSessionNotFound",
                        cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
                var restoredFromWorkspace = await TryRestoreConversationProjectionFromWorkspaceSnapshotAsync(conversationId).ConfigureAwait(false);
                if (!restoredFromWorkspace)
                {
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
                return true;
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

    private void ReplaceMessageHistory(string? conversationId, IImmutableList<ConversationMessageSnapshot> transcript)
    {
        var messages = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        MessageHistory = new ObservableCollection<ChatMessageViewModel>(messages.Select(FromSnapshot));
        UpdateVisibleTranscriptConversationId(conversationId, MessageHistory.Count > 0);
        OnPropertyChanged(nameof(HasVisibleTranscriptContent));
        OnPropertyChanged(nameof(OverlayStatusText));
        OnPropertyChanged(nameof(ShouldShowBlockingLoadingMask));
        OnPropertyChanged(nameof(ShouldShowLoadingOverlayPresenter));
    }

    private void ReplaceMessageHistory(string? conversationId, IReadOnlyList<ChatMessageViewModel> transcript)
    {
        var messages = transcript ?? Array.Empty<ChatMessageViewModel>();
        MessageHistory = new ObservableCollection<ChatMessageViewModel>(messages);
        UpdateVisibleTranscriptConversationId(conversationId, MessageHistory.Count > 0);
        OnPropertyChanged(nameof(HasVisibleTranscriptContent));
        OnPropertyChanged(nameof(OverlayStatusText));
        OnPropertyChanged(nameof(ShouldShowBlockingLoadingMask));
        OnPropertyChanged(nameof(ShouldShowLoadingOverlayPresenter));
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

        var state = await _chatStore.State ?? ChatState.Empty;
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
            return true;
        }

        if (AcpSessionRecoveryPolicy.Resolve(_chatService?.AgentCapabilities) == AcpSessionRecoveryMode.None)
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
        var state = await _chatStore.State ?? ChatState.Empty;
        var isAuthoritativelyCurrentConversation =
            string.Equals(state.HydratedConversationId, sessionId, StringComparison.Ordinal)
            || string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal);
        if (!isAuthoritativelyCurrentConversation)
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
            if (ReferenceEquals(PendingPermissionRequest, pending))
            {
                PendingPermissionRequest = null;
            }
        }
    }

    private async Task ClearRemoteBindingAsync(string conversationId, string? boundProfileId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var result = await _bindingCommands
            .UpdateBindingAsync(conversationId, remoteSessionId: null, boundProfileId)
            .ConfigureAwait(false);
        if (result.Status is BindingUpdateStatus.Success)
        {
            return;
        }

        if (result.Status is BindingUpdateStatus.Error
            && string.Equals(result.ErrorMessage, "BindingProjectionTimeout", StringComparison.Ordinal))
        {
            Logger.LogWarning(
                "Binding projection timed out while clearing stale remote binding. Applying local fallback. ConversationId={ConversationId}",
                conversationId);
            await ApplyLocalBindingClearFallbackAsync(conversationId, boundProfileId).ConfigureAwait(false);
            return;
        }

        Logger.LogWarning(
            "Failed to clear stale remote binding after hydration error. ConversationId={ConversationId} Status={Status} Error={Error}",
            conversationId,
            result.Status,
            result.ErrorMessage);
    }

    private async Task ApplyLocalBindingClearFallbackAsync(string conversationId, string? boundProfileId)
    {
        try
        {
            var state = await _chatStore.State ?? ChatState.Empty;
            var preservedSessionInfo = string.Equals(state.HydratedConversationId, conversationId, StringComparison.Ordinal)
                ? ConversationSessionInfoSnapshots.Clone(state.SessionInfo)
                : ConversationSessionInfoSnapshots.Clone(state.ResolveSessionStateSlice(conversationId)?.SessionInfo);
            await _chatStore.Dispatch(new ScrubConversationDerivedStateAction(
                    conversationId,
                    preservedSessionInfo))
                .ConfigureAwait(false);
            var clearedBinding = new ConversationBindingSlice(conversationId, null, boundProfileId);
            await _chatStore.Dispatch(new SetBindingSliceAction(clearedBinding)).ConfigureAwait(false);
            _conversationWorkspace.ClearConversationRuntimeContent(conversationId);
            _conversationWorkspace.UpdateRemoteBinding(conversationId, remoteSessionId: null, boundProfileId);
            _conversationWorkspace.ScheduleSave();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Failed to apply local fallback after stale binding clear timeout. ConversationId={ConversationId}",
                conversationId);
        }
    }

    private void ApplySelectedProfile(ServerConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var selectedProfile = ResolveLoadedProfileSelection(profile);
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
            _ = _chatConnectionStore.Dispatch(new SetSettingsSelectedProfileAction(profile.Id));
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
        await _chatConnectionStore.Dispatch(new SetSettingsSelectedProfileAction(profile.Id)).ConfigureAwait(false);
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
        var storeState = await _chatStore.State ?? ChatState.Empty;
        var preservedSessionState = storeState.ResolveSessionStateSlice(conversationId);
        var preservedSessionInfo = ConversationSessionInfoSnapshots.Clone(
            preservedSessionState?.SessionInfo);
        await _chatStore.Dispatch(new ClearTurnAction(conversationId)).ConfigureAwait(false);
        await _chatStore.Dispatch(new HydrateConversationAction(
            conversationId,
            ImmutableList<ConversationMessageSnapshot>.Empty,
            ImmutableList<ConversationPlanEntrySnapshot>.Empty,
            ShowPlanPanel: false,
            PlanTitle: null)).ConfigureAwait(false);
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
            snapshot.ShowPlanPanel,
            snapshot.PlanTitle)).ConfigureAwait(false);
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
        var state = await _chatStore.State ?? ChatState.Empty;
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
            snapshot.ShowPlanPanel,
            snapshot.PlanTitle)).ConfigureAwait(false);
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

            if (ShouldOwnRemoteHydrationUi(conversationId, activationVersion))
            {
                ownsConnectionLifecycleOverlay = true;
                await PostToUiAsync(() =>
                {
                    SetConversationOverlayOwners(
                        sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                        connectionLifecycleConversationId: conversationId,
                        historyConversationId: _historyOverlayConversationId);
                }).ConfigureAwait(false);
            }

            var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
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

                var finalConnectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
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

            var readyAfterConnect = await IsRemoteConnectionReadyAsync(binding.ProfileId, cancellationToken).ConfigureAwait(false);
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
                await PostToUiAsync(() =>
                {
                    SetConversationOverlayOwners(
                        sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                        connectionLifecycleConversationId: null,
                        historyConversationId: _historyOverlayConversationId);
                }).ConfigureAwait(false);
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

        if (await IsRemoteConnectionReadyAsync(binding.ProfileId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

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
        if (AcpSessionRecoveryPolicy.Resolve(chatService.AgentCapabilities) == AcpSessionRecoveryMode.None)
        {
            return true;
        }

        var state = await _chatStore.State ?? ChatState.Empty;
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

        if (AcpSessionRecoveryPolicy.Resolve(_chatService?.AgentCapabilities) == AcpSessionRecoveryMode.None)
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

        var state = await _chatStore.State ?? ChatState.Empty;
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

        var state = await _chatStore.State ?? ChatState.Empty;
        var projectedTranscript = state.ResolveContentSlice(conversationId)?.Transcript
            ?? (string.Equals(state.HydratedConversationId, conversationId, StringComparison.Ordinal)
                ? state.Transcript
                : null);
        return projectedTranscript?.Count(static message => !IsThinkingPlaceholder(message)) ?? 0;
    }

    private static void ReleaseBufferedUpdatesAfterInterruptedHydration(
        IAcpSessionUpdateBufferController? adapter,
        long? hydrationAttemptId,
        string reason)
    {
        if (adapter is null || !hydrationAttemptId.HasValue)
        {
            return;
        }

        adapter.SuppressBufferedUpdates(hydrationAttemptId.Value, reason);
        adapter.TryMarkHydrated(hydrationAttemptId.Value, lowTrust: true, reason: reason);
    }

    private async Task<ServerConfiguration?> ResolveProfileConfigurationAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        await EnsureAcpProfilesLoadedAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return _acpProfiles.Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.Ordinal))
            ?? await _configurationService.LoadConfigurationAsync(profileId);
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
        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
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
            var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
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
        var connectionState = await _chatConnectionStore.State ?? ChatConnectionState.Empty;
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

        var currentState = await _chatStore.State ?? ChatState.Empty;
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
            sessionInfo.Description,
            null,
            sessionInfo.UpdatedAt,
            sessionInfo.Meta));
    }

    private async Task ApplyRemoteSessionInfoSnapshotWhenReadyAsync(
        string conversationId,
        ConversationBindingSlice expectedBinding,
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

        await ApplySessionInfoSnapshotProjectionAsync(
                conversationId,
                sessionInfo,
                cancellationToken)
            .ConfigureAwait(false);
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

        var storeState = await _chatStore.State ?? ChatState.Empty;
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
                    CurrentSessionDisplayName = ResolveSessionDisplayName(conversationId);
                }
            }).ConfigureAwait(false);
        }
    }

    private static DateTime? ParseSessionUpdatedAtUtc(string? updatedAt)
    {
        if (string.IsNullOrWhiteSpace(updatedAt)
            || !DateTimeOffset.TryParse(
                updatedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedUpdatedAt))
        {
            return null;
        }

        return parsedUpdatedAt.UtcDateTime;
    }

    private static async Task<AgentSessionInfo?> FindRemoteSessionInfoAsync(
        IChatService chatService,
        string remoteSessionId,
        CancellationToken cancellationToken)
    {
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
            Description = sessionInfo.Description,
            Cwd = establishedCwd,
            UpdatedAtUtc = sessionInfo.UpdatedAtUtc,
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
