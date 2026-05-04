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
    private void OnSessionUpdateReceived(object? sender, SessionUpdateEventArgs e)
    {
        TrackPendingSessionUpdate(_sessionUpdateWorkQueue.Enqueue(() => ProcessSessionUpdateAsync(e)));
    }

    private async Task ProcessSessionUpdateAsync(SessionUpdateEventArgs e)
    {
        try
        {
            RecordSessionUpdateObservation(e.SessionId);
            var storeState = await _chatStore.State ?? ChatState.Empty;
            var activeConversationId = storeState.ActiveTurn?.ConversationId ?? storeState.HydratedConversationId;
            var activeBinding = storeState.ResolveBinding(activeConversationId);
            var boundConversationId =
                !string.IsNullOrWhiteSpace(activeConversationId)
                && string.Equals(activeBinding?.RemoteSessionId, e.SessionId, StringComparison.Ordinal)
                    ? activeConversationId
                    : _authoritativeRemoteSessionRouter.ResolveConversationId(storeState, e.SessionId);

            if (string.IsNullOrWhiteSpace(boundConversationId))
            {
                return;
            }

            var targetConversationId = boundConversationId!;
            var isActiveTarget =
                !string.IsNullOrWhiteSpace(activeConversationId)
                && string.Equals(activeConversationId, targetConversationId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(activeBinding?.RemoteSessionId)
                && string.Equals(e.SessionId, activeBinding.RemoteSessionId, StringComparison.Ordinal);
            var activeTurn = isActiveTarget
                ? ResolveSessionUpdateTurn(storeState, activeConversationId, e.SessionId)
                : null;

            if (e.Update is AgentMessageUpdate messageUpdate && messageUpdate.Content != null)
            {
                await AdvanceActiveTurnPhaseAsync(activeTurn, ChatTurnPhase.Responding).ConfigureAwait(true);
                await HandleAgentContentChunkAsync(targetConversationId, messageUpdate.Content).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(e.SessionId);
                if (!isActiveTarget)
                {
                    await MarkConversationUnreadAttentionAsync(targetConversationId, ConversationAttentionSource.AgentMessage).ConfigureAwait(false);
                }
            }
            else if (e.Update is AgentThoughtUpdate)
            {
                // Thought chunks are transient states; they trigger 'thinking' UI feedback.
                await AdvanceActiveTurnPhaseAsync(activeTurn, ChatTurnPhase.Thinking).ConfigureAwait(true);
            }
            else if (e.Update is UserMessageUpdate userMessageUpdate && userMessageUpdate.Content != null)
            {
                await UpsertUserMessageChunkAsync(targetConversationId, userMessageUpdate, activeTurn).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(e.SessionId);
            }
            else if (e.Update is ToolCallUpdate toolCallUpdate)
            {
                await AdvanceActiveTurnPhaseAsync(
                    activeTurn,
                    ChatTurnPhase.ToolPending,
                    toolCallUpdate.ToolCallId,
                    toolCallUpdate.Title).ConfigureAwait(true);

                await UpsertTranscriptSnapshotAsync(targetConversationId, CreateToolCallSnapshot(toolCallUpdate)).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(e.SessionId);
            }
            else if (e.Update is ToolCallStatusUpdate toolCallStatusUpdate)
            {
                var phase = toolCallStatusUpdate.Status switch
                {
                    Domain.Models.Tool.ToolCallStatus.InProgress => ChatTurnPhase.ToolRunning,
                    Domain.Models.Tool.ToolCallStatus.Completed => ChatTurnPhase.WaitingForAgent,
                    Domain.Models.Tool.ToolCallStatus.Failed => ChatTurnPhase.Failed,
                    Domain.Models.Tool.ToolCallStatus.Cancelled => ChatTurnPhase.Cancelled,
                    _ => ChatTurnPhase.ToolPending
                };
                await AdvanceActiveTurnPhaseAsync(activeTurn, phase, toolCallStatusUpdate.ToolCallId).ConfigureAwait(true);
                if (toolCallStatusUpdate.Status == Domain.Models.Tool.ToolCallStatus.Cancelled)
                {
                    await PreemptivelyCancelTurnAsync(expectedConversationId: targetConversationId).ConfigureAwait(true);
                }
                await UpdateToolCallStatusAsync(targetConversationId, toolCallStatusUpdate).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(e.SessionId);
            }
            else if (e.Update != null)
            {
                var route = _sessionUpdateRouter.Route(
                    e,
                    IsConversationConfigAuthoritative(targetConversationId));
                if (!route.Handled)
                {
                    // FUTURE-PROOFING: Log unknown protocol extensions to detect agent version mismatches.
                    Logger.LogInformation("Unhandled session update type: {UpdateType}", e.Update.GetType().Name);
                    return;
                }

                if (route.Ignored)
                {
                    if (e.Update is CurrentModeUpdate modeChange)
                    {
                        Logger.LogDebug(
                            "Ignoring legacy current mode update because config options are authoritative. conversationId={ConversationId} remoteSessionId={RemoteSessionId} modeId={ModeId}",
                            targetConversationId,
                            e.SessionId,
                            modeChange.NormalizedModeId);
                    }

                    return;
                }

                if (route.ShouldSetConfigAuthoritative)
                {
                    SetConversationConfigAuthority(targetConversationId, true);
                }

                if (route.Delta is not null)
                {
                    await ApplySessionUpdateDeltaAsync(targetConversationId, route.Delta).ConfigureAwait(true);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing session update");
        }
    }

    private void RaiseOverlayStateChanged()
    {
        OnPropertyChanged(nameof(IsActivationOverlayVisible));
        OnPropertyChanged(nameof(IsOverlayVisible));
        OnPropertyChanged(nameof(ShouldShowActiveConversationRoot));
        OnPropertyChanged(nameof(ShouldLoadActiveConversationRoot));
        OnPropertyChanged(nameof(ShouldShowSessionHeader));
        OnPropertyChanged(nameof(ShouldShowTranscriptSurface));
        OnPropertyChanged(nameof(ShouldLoadTranscriptSurface));
        OnPropertyChanged(nameof(ShouldShowConversationInputSurface));
        OnPropertyChanged(nameof(OverlayLoadingStage));
        OnPropertyChanged(nameof(OverlayStatusText));
        OnPropertyChanged(nameof(ShouldShowBlockingLoadingMask));
        OnPropertyChanged(nameof(ShouldShowLoadingOverlayStatusPill));
        OnPropertyChanged(nameof(ShouldShowLoadingOverlayPresenter));
        OnPropertyChanged(nameof(IsInputEnabled));
        OnPropertyChanged(nameof(CanSendPromptUi));
        OnPropertyChanged(nameof(CanStartVoiceInput));
        OnPropertyChanged(nameof(CanStopVoiceInput));
    }

    private void TrackPendingSessionUpdate(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_sessionUpdateTrackingSync)
        {
            if (_pendingSessionUpdateCount == 0)
            {
                _sessionUpdatesDrainedTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _pendingSessionUpdateCount++;
        }

        _ = ObservePendingSessionUpdateAsync(task);
    }

    private async Task ObservePendingSessionUpdateAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            TaskCompletionSource<object?>? drained = null;

            lock (_sessionUpdateTrackingSync)
            {
                if (_pendingSessionUpdateCount > 0)
                {
                    _pendingSessionUpdateCount--;
                    if (_pendingSessionUpdateCount == 0)
                    {
                        drained = _sessionUpdatesDrainedTcs;
                    }
                }
            }

            drained?.TrySetResult(null);
        }
    }

    private Task WaitForPendingSessionUpdatesAsync()
    {
        lock (_sessionUpdateTrackingSync)
        {
            if (_pendingSessionUpdateCount == 0)
            {
                return Task.CompletedTask;
            }

            _sessionUpdatesDrainedTcs ??= new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _sessionUpdatesDrainedTcs.Task;
        }
    }

    private bool HasPendingSessionUpdates()
    {
        lock (_sessionUpdateTrackingSync)
        {
            return _pendingSessionUpdateCount > 0;
        }
    }

    private Task WaitForAdapterReplayDrainAsync(long hydrationAttemptId, CancellationToken cancellationToken)
    {
        if (_chatService is not IAcpSessionUpdateBufferController adapter)
        {
            return Task.CompletedTask;
        }

        return adapter.WaitForBufferedUpdatesDrainedAsync(hydrationAttemptId, cancellationToken);
    }

    private async Task AwaitBufferedSessionReplayProjectionAsync(
        CancellationToken cancellationToken,
        long? hydrationAttemptId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await PostToUiAsync(static () => { }).ConfigureAwait(false);
        if (_chatService is IAcpSessionUpdateBufferController adapter
            && hydrationAttemptId.HasValue)
        {
            await adapter
                .WaitForBufferedUpdatesDrainedAsync(hydrationAttemptId.Value, cancellationToken)
                .WaitAsync(RemoteReplayDrainTimeout, cancellationToken)
                .ConfigureAwait(false);

            await PostToUiAsync(static () => { }).ConfigureAwait(false);
        }

        var pendingUpdates = WaitForPendingSessionUpdatesAsync();
        if (!pendingUpdates.IsCompleted)
        {
            await pendingUpdates.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await PostToUiAsync(static () => { }).ConfigureAwait(false);
    }

    private void RecordSessionUpdateObservation(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sessionUpdateObservationSync)
        {
            _sessionUpdateObservationCounts[sessionId] =
                _sessionUpdateObservationCounts.TryGetValue(sessionId, out var current)
                    ? checked(current + 1)
                    : 1;
            _sessionUpdateLastObservedAtUtc[sessionId] = DateTime.UtcNow;
        }

        if (OverlayLoadingStage != LoadingOverlayStage.HydratingHistory)
        {
            return;
        }

        if (TryResolveCurrentHydrationConversationForRemoteSession(sessionId, out var conversationId))
        {
            SetHydrationOverlayPhase(conversationId, HydrationOverlayPhase.ReplayingSessionUpdates);
            RaiseOverlayStatusTextChanged();
        }
    }

    private void RecordTranscriptProjectionObservation(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        lock (_sessionUpdateObservationSync)
        {
            _sessionTranscriptProjectionObservationCounts[sessionId] =
                _sessionTranscriptProjectionObservationCounts.TryGetValue(sessionId, out var current)
                    ? checked(current + 1)
                    : 1;
        }

        if (OverlayLoadingStage != LoadingOverlayStage.HydratingHistory)
        {
            return;
        }

        if (TryResolveCurrentHydrationConversationForRemoteSession(sessionId, out var conversationId))
        {
            SetHydrationOverlayPhase(conversationId, HydrationOverlayPhase.ProjectingTranscript);
            RaiseOverlayStatusTextChanged();
        }
    }

    private void RaiseOverlayStatusTextChanged()
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            OnPropertyChanged(nameof(OverlayStatusText));
            return;
        }

        _ = PostToUiAsync(() => OnPropertyChanged(nameof(OverlayStatusText)));
    }

    private long GetSessionUpdateObservationCount(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return 0;
        }

        lock (_sessionUpdateObservationSync)
        {
            return _sessionUpdateObservationCounts.TryGetValue(sessionId, out var count)
                ? count
                : 0;
        }
    }

    private long GetTranscriptProjectionObservationCount(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return 0;
        }

        lock (_sessionUpdateObservationSync)
        {
            return _sessionTranscriptProjectionObservationCounts.TryGetValue(sessionId, out var count)
                ? count
                : 0;
        }
    }

    private DateTime? GetSessionUpdateLastObservedAtUtc(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        lock (_sessionUpdateObservationSync)
        {
            return _sessionUpdateLastObservedAtUtc.TryGetValue(sessionId, out var observedAtUtc)
                ? observedAtUtc
                : null;
        }
    }

    private Task AwaitRemoteReplaySettleQuietPeriodAsync(
        string remoteSessionId,
        long replayBaseline,
        CancellationToken cancellationToken)
        => _hydrationCoordinator.AwaitRemoteReplaySettleQuietPeriodAsync(
            _hydrationContext,
            remoteSessionId,
            replayBaseline,
            cancellationToken);

    private async Task AwaitRemoteReplayProjectionAsync(
        string conversationId,
        long? activationVersion,
        string remoteSessionId,
        long replayBaseline,
        long transcriptProjectionBaseline,
        long? hydrationAttemptId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SetHydrationOverlayPhaseAsync(
                conversationId,
                activationVersion,
                HydrationOverlayPhase.AwaitingReplayStart)
            .ConfigureAwait(false);

        var replayStartTimeoutAt = DateTime.UtcNow + RemoteReplayStartTimeout;

        while (GetSessionUpdateObservationCount(remoteSessionId) <= replayBaseline
            && DateTime.UtcNow < replayStartTimeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(RemoteReplayPollDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        if (GetSessionUpdateObservationCount(remoteSessionId) > replayBaseline)
        {
            await SetHydrationOverlayPhaseAsync(
                    conversationId,
                    activationVersion,
                    HydrationOverlayPhase.ReplayingSessionUpdates)
                .ConfigureAwait(false);
        }

        var transcriptTimeoutAt = DateTime.UtcNow + RemoteReplayStartTimeout;
        while (GetTranscriptProjectionObservationCount(remoteSessionId) <= transcriptProjectionBaseline
            && DateTime.UtcNow < transcriptTimeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(RemoteReplayPollDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        if (GetTranscriptProjectionObservationCount(remoteSessionId) > transcriptProjectionBaseline)
        {
            await SetHydrationOverlayPhaseAsync(
                    conversationId,
                    activationVersion,
                    HydrationOverlayPhase.ProjectingTranscript)
                .ConfigureAwait(false);
            await SetHydrationOverlayPhaseAsync(
                    conversationId,
                    activationVersion,
                    HydrationOverlayPhase.SettlingReplay)
                .ConfigureAwait(false);
            await AwaitRemoteReplaySettleQuietPeriodAsync(remoteSessionId, replayBaseline, cancellationToken).ConfigureAwait(false);
        }

        await SetHydrationOverlayPhaseAsync(
                conversationId,
                activationVersion,
                HydrationOverlayPhase.FinalizingProjection)
            .ConfigureAwait(false);

#if DEBUG
        Logger.LogInformation(
            "Remote replay wait finished. remoteSessionId={RemoteSessionId} replayBaseline={ReplayBaseline} observedCount={ObservedCount} transcriptBaseline={TranscriptProjectionBaseline} transcriptObservedCount={TranscriptObservedCount} startTimedOut={StartTimedOut} transcriptTimedOut={TranscriptTimedOut}",
            remoteSessionId,
            replayBaseline,
            GetSessionUpdateObservationCount(remoteSessionId),
            transcriptProjectionBaseline,
            GetTranscriptProjectionObservationCount(remoteSessionId),
            DateTime.UtcNow >= replayStartTimeoutAt,
            DateTime.UtcNow >= transcriptTimeoutAt);
#endif
        await AwaitBufferedSessionReplayProjectionAsync(cancellationToken, hydrationAttemptId).ConfigureAwait(false);
    }

    private static ActiveTurnState? ResolveSessionUpdateTurn(ChatState storeState, string? activeConversationId, string remoteSessionId)
    {
        if (storeState.ActiveTurn is not { } activeTurn
            || string.IsNullOrWhiteSpace(activeConversationId)
            || !string.Equals(activeTurn.ConversationId, activeConversationId, StringComparison.Ordinal))
        {
            return null;
        }

        var turnBinding = storeState.ResolveBinding(activeTurn.ConversationId);
        return string.Equals(turnBinding?.RemoteSessionId, remoteSessionId, StringComparison.Ordinal)
            ? activeTurn
            : null;
    }

    private async Task AdvanceActiveTurnPhaseAsync(
        ActiveTurnState? activeTurn,
        ChatTurnPhase phase,
        string? toolCallId = null,
        string? toolTitle = null)
    {
        if (activeTurn is null)
        {
            return;
        }

        await _chatStore.Dispatch(
            new AdvanceTurnPhaseAction(
                activeTurn.ConversationId,
                activeTurn.TurnId,
                phase,
                ToolCallId: toolCallId,
                ToolTitle: toolTitle)).ConfigureAwait(true);
    }

    private async Task ApplyPromptDispatchResultAsync(
        string conversationId,
        string turnId,
        SessionPromptResponse response)
    {
        switch (response.StopReason)
        {
            case StopReason.Cancelled:
                await PreemptivelyCancelTurnAsync(conversationId, turnId).ConfigureAwait(true);
                break;

            case StopReason.Refusal:
                await _chatStore.Dispatch(new FailTurnAction(conversationId, turnId, StopReason.Refusal.ToString())).ConfigureAwait(true);
                break;

            case StopReason.EndTurn:
            case StopReason.MaxTokens:
            case StopReason.MaxTurnRequests:
                await _chatStore.Dispatch(new CompleteTurnAction(conversationId, turnId)).ConfigureAwait(true);
                break;
        }
    }

    private async Task ReconcilePromptUserMessageIdAsync(
        string? conversationId,
        string pendingUserMessageLocalId,
        string requestMessageId,
        string? responseUserMessageId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(pendingUserMessageLocalId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(responseUserMessageId))
        {
            return;
        }

        var currentState = await _chatStore.State ?? ChatState.Empty;
        var transcript = currentState.ResolveContentSlice(conversationId)?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var reconciled = _outgoingUserMessageProjector.TryReconcilePromptAcknowledgement(
            transcript,
            pendingUserMessageLocalId,
            responseUserMessageId);
        if (reconciled is null)
        {
            return;
        }
        await UpsertTranscriptSnapshotAsync(conversationId, reconciled).ConfigureAwait(false);
    }

    private async Task HandleAgentContentChunkAsync(string? conversationId, ContentBlock content)
    {
        // ACP streams response content as an array of blocks. We coalesce adjacent text blocks
        // into a single UI element to mimic a natural typing effect.
        if (content is TextContentBlock text)
        {
            await AppendAgentTextChunkAsync(conversationId, text.Text ?? string.Empty).ConfigureAwait(true);
            return;
        }

        await AddMessageToHistoryAsync(conversationId, content, isOutgoing: false).ConfigureAwait(true);
    }

    private async Task MarkConversationUnreadAttentionAsync(string conversationId, ConversationAttentionSource source)
    {
        var attentionStore = _conversationAttentionStore;
        if (attentionStore is null || string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        await attentionStore.Dispatch(
                new MarkConversationUnreadAction(conversationId, source, DateTime.UtcNow))
            .ConfigureAwait(false);
    }

    private async Task ClearConversationUnreadAttentionAsync(string conversationId)
    {
        var attentionStore = _conversationAttentionStore;
        if (attentionStore is null || string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        await attentionStore.Dispatch(new ClearConversationUnreadAction(conversationId)).ConfigureAwait(false);
    }

    private async Task RemoveConversationAttentionAsync(string conversationId)
    {
        var attentionStore = _conversationAttentionStore;
        if (attentionStore is null || string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        await attentionStore.Dispatch(new RemoveConversationAttentionAction(conversationId)).ConfigureAwait(false);
    }

    private async Task AppendAgentTextChunkAsync(string? conversationId, string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk) || string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        await _chatStore.Dispatch(new AppendTextDeltaAction(conversationId, chunk)).ConfigureAwait(false);
    }

    private Task<bool> ActivateConversationAsync(string sessionId, CancellationToken cancellationToken = default)
        => ActivateConversationCoreAsync(sessionId, awaitRemoteHydration: true, cancellationToken);

    private async Task<bool> ActivateConversationCoreAsync(
        string sessionId,
        bool awaitRemoteHydration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var result = await _conversationActivationOrchestrator
            .ActivateAsync(
                new ConversationActivationOrchestratorRequest(sessionId, awaitRemoteHydration),
                this,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded;
    }

    private async Task<bool> CompleteConversationRemoteActivationAsync(
        string sessionId,
        long activationVersion,
        CancellationToken cancellationToken,
        ConversationRuntimeSlice? warmRuntimeSnapshot = null,
        bool allowWarmReuseShortCircuit = true)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var state = await _chatStore.State ?? ChatState.Empty;
        var binding = await ResolveConversationBindingAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var runtimeState = warmRuntimeSnapshot ?? state.ResolveRuntimeState(sessionId);
        var hasReusableProjection = HasReusableWarmProjection(state, sessionId);
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            await SetConversationRuntimeStateAsync(
                    sessionId,
                    ConversationRuntimePhase.Warm,
                    binding,
                    reason: "LocalConversationReady",
                    cancellationToken)
                .ConfigureAwait(false);
            await ClearConversationUnreadAttentionAsync(sessionId).ConfigureAwait(false);
            await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                    sessionId,
                    activationVersion,
                    SessionActivationPhase.Hydrated,
                    reason: "LocalConversationReady")
                .ConfigureAwait(false);
            return true;
        }

        var currentConnectionInstanceId = await GetAuthoritativeConnectionInstanceIdAsync().ConfigureAwait(false);
        if (allowWarmReuseShortCircuit
            && ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
                runtimeState,
                binding,
                currentConnectionInstanceId,
                hasReusableProjection))
        {
            Logger.LogInformation(
                "Skipping remote hydration because the selected conversation is already warm. ConversationId={ConversationId}",
                sessionId);
            await ClearConversationUnreadAttentionAsync(sessionId).ConfigureAwait(false);
            await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                    sessionId,
                    activationVersion,
                    SessionActivationPhase.Hydrated,
                    reason: "WarmReuse")
                .ConfigureAwait(false);
            return true;
        }

        {
            var denialReason = allowWarmReuseShortCircuit
                ? ConversationWarmReusePolicy.GetWarmReuseDenialReason(
                    runtimeState, binding, currentConnectionInstanceId, hasReusableProjection)
                : "SupersededInFlightActivationRequiresAuthoritativeHydration";
            Logger.LogInformation(
                "Warm reuse denied in HydrateConversationAsync, falling back to slow hydration. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} ExpectedConnectionInstanceId={ExpectedConnectionInstanceId} ActualConnectionInstanceId={ActualConnectionInstanceId} Reason={Reason}",
                sessionId,
                binding?.RemoteSessionId,
                runtimeState?.ConnectionInstanceId,
                currentConnectionInstanceId,
                denialReason);
        }

        await EnsureSelectedProfileConnectionForConversationAsync(
                sessionId,
                activationVersion,
                cancellationToken)
            .ConfigureAwait(false);
        if (IsActivationContextStale(activationVersion, cancellationToken))
        {
            return false;
        }

        state = await _chatStore.State ?? ChatState.Empty;
        binding = await ResolveConversationBindingAsync(sessionId, cancellationToken).ConfigureAwait(false);
        currentConnectionInstanceId = await GetAuthoritativeConnectionInstanceIdAsync().ConfigureAwait(false);
        var warmRuntimeAfterProfileReconnect = warmRuntimeSnapshot ?? state.ResolveRuntimeState(sessionId);
        var warmReuseAfterReconnectLivenessCheck = BuildWarmReuseConnectionLivenessCheck(binding);
        hasReusableProjection = HasReusableWarmProjection(state, sessionId);
        if (allowWarmReuseShortCircuit
            && ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
                warmRuntimeAfterProfileReconnect,
                binding,
                currentConnectionInstanceId,
                hasReusableProjection,
                warmReuseAfterReconnectLivenessCheck))
        {
            Logger.LogInformation(
                "Skipping remote hydration because the selected conversation became warm after restoring the reusable profile connection. ConversationId={ConversationId}",
                sessionId);
            await SetConversationRuntimeStateAsync(
                    sessionId,
                    ConversationRuntimePhase.Warm,
                    binding,
                    reason: "WarmReuseAfterProfileReconnect",
                    cancellationToken,
                    connectionInstanceId: currentConnectionInstanceId)
                .ConfigureAwait(false);
            await ClearConversationUnreadAttentionAsync(sessionId).ConfigureAwait(false);
            return true;
        }

        var remotePhaseStopwatch = Stopwatch.StartNew();
        await _remoteConversationActivationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remoteConnectionReady = await EnsureActiveConversationRemoteConnectionReadyAsync(
                    sessionId,
                    activationVersion,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!remoteConnectionReady)
            {
                Logger.LogInformation(
                    "Conversation remote activation failed before hydration. ConversationId={ConversationId} ElapsedMs={ElapsedMs}",
                    sessionId,
                    remotePhaseStopwatch.ElapsedMilliseconds);
                await SetConversationRuntimeStateAsync(
                        sessionId,
                        ConversationRuntimePhase.Faulted,
                        reason: "RemoteConnectionNotReady",
                        cancellationToken)
                    .ConfigureAwait(false);
                return false;
            }

            await SetConversationRuntimeStateAsync(
                    sessionId,
                    ConversationRuntimePhase.RemoteConnectionReady,
                    reason: "RemoteConnectionReady",
                    cancellationToken)
                .ConfigureAwait(false);
            await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                    sessionId,
                    activationVersion,
                    SessionActivationPhase.RemoteConnectionReady,
                    reason: "RemoteConnectionReady")
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var hydrated = await EnsureActiveConversationRemoteHydratedAsync(
                    sessionId,
                    activationVersion,
                    cancellationToken,
                    allowWarmReuseShortCircuit)
                .ConfigureAwait(false);
            if (hydrated)
            {
                await ClearConversationUnreadAttentionAsync(sessionId).ConfigureAwait(false);
                await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                        sessionId,
                        activationVersion,
                        SessionActivationPhase.Hydrated,
                        reason: "Hydrated")
                    .ConfigureAwait(false);
            }
            Logger.LogInformation(
                "Conversation remote activation completed. ConversationId={ConversationId} Succeeded={Succeeded} ElapsedMs={ElapsedMs}",
                sessionId,
                hydrated,
                remotePhaseStopwatch.ElapsedMilliseconds);
            return hydrated;
        }
        finally
        {
            _remoteConversationActivationGate.Release();
        }
    }

    private async Task HandleConversationActivationExceptionAsync(string sessionId, long? activationVersion, Exception ex)
    {
        Logger.LogError(ex, "Switching session failed (SessionId={SessionId})", sessionId);

        if (!_conversationActivationOutcomePublisher.CanPublish(activationVersion))
        {
            Logger.LogInformation(
                "Discarding stale conversation activation failure because the chat shell no longer owns the latest intent. conversationId={ConversationId} activationVersion={ActivationVersion}",
                sessionId,
                activationVersion);
            return;
        }

        await _conversationActivationOutcomePublisher.TryPublishPhaseAsync(
                sessionId,
                activationVersion,
                SessionActivationPhase.Faulted,
                ex.GetType().Name)
            .ConfigureAwait(false);
        await _conversationActivationOutcomePublisher.TrySetActivationErrorAsync(
                sessionId,
                activationVersion,
                $"Failed to switch session: {ex.Message}")
            .ConfigureAwait(false);
        await PostToUiAsync(() => IsSessionActive = !string.IsNullOrWhiteSpace(CurrentSessionId)).ConfigureAwait(false);
    }

    private bool IsActivationContextStale(long? activationVersion, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        if (!activationVersion.HasValue)
        {
            return false;
        }

        return !_conversationActivationOrchestrator.IsLatestActivationVersion(activationVersion.Value);
    }

    private async Task ResetRemoteHydrationUiStateAsync(long activationVersion)
    {
        if (!_conversationActivationOrchestrator.IsLatestActivationVersion(activationVersion))
        {
            return;
        }

        await _chatStore.Dispatch(new SetIsHydratingAction(false)).ConfigureAwait(false);
        await PostToUiAsync(() =>
        {
            IsRemoteHydrationPending = false;
            _remoteHydrationSessionUpdateBaselineCounts.Clear();
            _remoteHydrationKnownTranscriptBaselineCounts.Clear();
            _remoteHydrationKnownTranscriptGrowthGraceDeadlineUtc.Clear();
            SetConversationOverlayOwners(
                sessionSwitchConversationId: _sessionSwitchOverlayConversationId,
                connectionLifecycleConversationId: null,
                historyConversationId: null);
        }).ConfigureAwait(false);
    }

    private Task EnsureCurrentSessionIdAlignedAsync(string sessionId, long activationVersion)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.CompletedTask;
        }

        return PostToUiAsync(() =>
        {
            if (_disposed
                || !_conversationActivationOrchestrator.IsLatestActivationVersion(activationVersion)
                || string.Equals(CurrentSessionId, sessionId, StringComparison.Ordinal))
            {
                return;
            }

            CurrentSessionId = sessionId;
        });
    }

    private void ApplySlashCommandProjection(IReadOnlyList<ConversationAvailableCommandSnapshot> commands)
    {
        UpdateSlashCommandCollection(AvailableSlashCommands, commands);

        RefreshSlashCommandFilter();
    }

    private static void UpdateSlashCommandCollection(
        ObservableCollection<SlashCommandViewModel> current,
        IReadOnlyList<ConversationAvailableCommandSnapshot> projected)
    {
        for (var index = 0; index < projected.Count; index++)
        {
            var projectedCommand = projected[index];
            if (index >= current.Count)
            {
                current.Add(CreateSlashCommandViewModel(projectedCommand));
                continue;
            }

            var existing = current[index];
            if (string.Equals(existing.Name, projectedCommand.Name, StringComparison.Ordinal)
                && string.Equals(existing.Description, projectedCommand.Description, StringComparison.Ordinal)
                && string.Equals(existing.InputHint, projectedCommand.InputHint, StringComparison.Ordinal))
            {
                continue;
            }

            existing.Name = projectedCommand.Name;
            existing.Description = projectedCommand.Description;
            existing.InputHint = projectedCommand.InputHint;
        }

        while (current.Count > projected.Count)
        {
            current.RemoveAt(current.Count - 1);
        }
    }

    private static SlashCommandViewModel CreateSlashCommandViewModel(ConversationAvailableCommandSnapshot command)
        => new()
        {
            Name = command.Name,
            Description = command.Description,
            InputHint = command.InputHint
        };

    private void RefreshSlashCommandFilter()
    {
        var trimmed = (CurrentPrompt ?? string.Empty).TrimStart();
        if (!trimmed.StartsWith("/"))
        {
            ShowSlashCommands = false;
            SlashGhostSuffix = string.Empty;
            FilteredSlashCommands.Clear();
            SelectedSlashCommand = null;
            return;
        }

        var afterSlash = trimmed.Length > 1 ? trimmed[1..] : string.Empty;
        var token = afterSlash.Split(new[] { ' ', '\t', '\r', '\n' }, 2)[0];
        var hasAny = AvailableSlashCommands.Count > 0;

        FilteredSlashCommands.Clear();
        if (hasAny)
        {
            foreach (var cmd in AvailableSlashCommands.Where(c => c.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase)))
            {
                FilteredSlashCommands.Add(cmd);
            }
        }

        SelectedSlashCommand = FilteredSlashCommands.FirstOrDefault();
        ShowSlashCommands = FilteredSlashCommands.Count > 0;

        UpdateSlashGhostSuffix(token, trimmed);
    }

    private void UpdateSlashGhostSuffix(string token, string trimmedPrompt)
    {
        if (SelectedSlashCommand != null && token.Length <= SelectedSlashCommand.Name.Length)
        {
            var suffix = SelectedSlashCommand.Name[token.Length..];
            SlashGhostSuffix = suffix + (trimmedPrompt.Contains(' ') ? string.Empty : " ");
        }
        else
        {
            SlashGhostSuffix = string.Empty;
        }
    }

    public bool TryAcceptSelectedSlashCommand(bool commitWithInputPlaceholder = false)
    {
        if (!ShowSlashCommands || SelectedSlashCommand == null)
        {
            return false;
        }

        var trimmed = (CurrentPrompt ?? string.Empty).TrimStart();
        var prefixWhitespaceCount = (CurrentPrompt ?? string.Empty).Length - trimmed.Length;
        var afterSlash = trimmed.Length > 1 ? trimmed[1..] : string.Empty;
        var rest = afterSlash.Split(new[] { ' ', '\t', '\r', '\n' }, 2);
        var existingToken = rest.Length > 0 ? rest[0] : string.Empty;
        var remainder = afterSlash.Length >= existingToken.Length ? afterSlash[existingToken.Length..] : string.Empty;

        var completed = "/" + SelectedSlashCommand.Name;
        if (!remainder.StartsWith(" "))
        {
            completed += " ";
        }

        if (commitWithInputPlaceholder && string.IsNullOrWhiteSpace(remainder) && !string.IsNullOrWhiteSpace(SelectedSlashCommand.InputHint))
        {
            // no-op: hint is shown in list; we don't inject it into the prompt
        }

        CurrentPrompt = new string(' ', prefixWhitespaceCount) + completed + remainder.TrimStart();
        ShowSlashCommands = false;
        SlashGhostSuffix = string.Empty;
        return true;
    }

    public bool TryMoveSlashSelection(int delta)
    {
        if (!ShowSlashCommands || FilteredSlashCommands.Count == 0)
        {
            return false;
        }

        var index = SelectedSlashCommand != null ? FilteredSlashCommands.IndexOf(SelectedSlashCommand) : 0;
        if (index < 0) index = 0;
        index = Math.Clamp(index + delta, 0, FilteredSlashCommands.Count - 1);
        SelectedSlashCommand = FilteredSlashCommands[index];

        var trimmed = (CurrentPrompt ?? string.Empty).TrimStart();
        var afterSlash = trimmed.Length > 1 ? trimmed[1..] : string.Empty;
        var token = afterSlash.Split(new[] { ' ', '\t', '\r', '\n' }, 2)[0];
        UpdateSlashGhostSuffix(token, trimmed);
        return true;
    }

    private void SyncConversationPanelState(string? conversationId)
    {
        var selection = _panelRuntimeCoordinator.SyncConversation(_panelStateCoordinator, conversationId);
        BottomPanelTabs = selection.Tabs;
        SelectedBottomPanelTab = selection.SelectedTab;
        TerminalSessions = selection.TerminalSessions;
        SelectedTerminalSession = selection.SelectedTerminal;
        PendingAskUserRequest = selection.PendingAskUserRequest;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            ActiveLocalTerminalSession = null;
        }
    }

    private async Task ActivateLocalTerminalPanelAsync(string? conversationId)
    {
        var version = Interlocked.Increment(ref _localTerminalActivationVersion);
        if (_localTerminalPanelCoordinator is null || string.IsNullOrWhiteSpace(conversationId))
        {
            ActiveLocalTerminalSession = null;
            return;
        }

        try
        {
            var terminalSession = await _panelRuntimeCoordinator
                .ActivateLocalTerminalSessionAsync(_localTerminalPanelCoordinator, _chatStore, _sessionManager, conversationId)
                .ConfigureAwait(true);

            if (version == Interlocked.Read(ref _localTerminalActivationVersion)
                && string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
            {
                ActiveLocalTerminalSession = terminalSession;
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning(ex, "Failed to activate local terminal panel. ConversationId={ConversationId}", conversationId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Unexpected error while activating local terminal panel. ConversationId={ConversationId}", conversationId);
        }
    }

    private async Task RemoveLocalTerminalSessionAsync(string conversationId)
    {
        if (_localTerminalPanelCoordinator is null)
        {
            return;
        }

        try
        {
            await _localTerminalPanelCoordinator.RemoveConversationAsync(conversationId).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    void IConversationPanelCleanup.CleanupAfterMutation(string conversationId, bool isCurrentSession)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            RemoveBottomPanelState(conversationId);
            return;
        }

        _ = PostToUiAsync(() => RemoveBottomPanelState(conversationId));
    }

    private void RemoveBottomPanelState(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var selection = _panelRuntimeCoordinator.RemoveConversation(
            _panelStateCoordinator,
            conversationId,
            string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal));
        _ = _chatStore.Dispatch(new ClearConversationRuntimeStateAction(conversationId));
        _ = RemoveLocalTerminalSessionAsync(conversationId);

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            BottomPanelTabs = selection.Tabs;
            SelectedBottomPanelTab = selection.SelectedTab;
            TerminalSessions = selection.TerminalSessions;
            SelectedTerminalSession = selection.SelectedTerminal;
            ActiveLocalTerminalSession = null;
            PendingAskUserRequest = selection.PendingAskUserRequest;
        }
    }

    public async Task<bool> SwitchConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        return await ActivateConversationCoreAsync(
                conversationId,
                awaitRemoteHydration: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    Task<bool> IConversationSessionSwitcher.SwitchConversationAsync(string conversationId, CancellationToken cancellationToken)
        => ActivateConversationCoreAsync(conversationId, awaitRemoteHydration: false, cancellationToken);

    public Task PrepareActivationAsync(
        ConversationActivationOrchestratorRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancelAmbientConnectionRequest();
        return Task.CompletedTask;
    }

    public Task<bool> CanReuseWarmCurrentConversationAsync(
        ConversationActivationOrchestratorRequest request,
        CancellationToken cancellationToken = default)
        => CanReuseWarmCurrentConversationAsync(request.ConversationId, cancellationToken);

    public Task<bool> CanReusePendingRemoteHydrationCurrentConversationAsync(
        ConversationActivationOrchestratorRequest request,
        CancellationToken cancellationToken = default)
        => CanReusePendingRemoteHydrationCurrentConversationAsync(request.ConversationId, cancellationToken);

    public async Task SupersedePendingActivationForWarmConversationAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        CancellationToken cancellationToken = default)
    {
        await ResetRemoteHydrationUiStateAsync(context.ActivationVersion).ConfigureAwait(false);
        await PostToUiAsync(() =>
        {
            _sessionSwitchPreviewConversationId = null;
            IsSessionSwitching = false;
            SetConversationOverlayOwners(
                sessionSwitchConversationId: null,
                connectionLifecycleConversationId: null,
                historyConversationId: null);
        }).ConfigureAwait(false);
    }

    public async Task<ConversationActivationOrchestratorResult> ExecuteActivationAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        CancellationToken cancellationToken = default)
    {
        var sessionId = request.ConversationId;
        var activationStartState = await _chatStore.State ?? ChatState.Empty;
        var forceRemoteHydrationAfterSupersedingInFlightActivation =
            HasCompetingInFlightConversationActivation(activationStartState, sessionId);
        var warmRuntimeSnapshot = activationStartState.ResolveRuntimeState(sessionId);

        await SetConversationRuntimeStateAsync(
                sessionId,
                ConversationRuntimePhase.Selecting,
                reason: "ActivationStarted",
                context.CancellationToken)
            .ConfigureAwait(false);
        var activationStopwatch = Stopwatch.StartNew();
        var initialWarmReuseBinding = await ResolveConversationBindingAsync(sessionId, context.CancellationToken).ConfigureAwait(false);
        var initialWarmReuseLivenessCheck = BuildWarmReuseConnectionLivenessCheck(initialWarmReuseBinding);
        var initialWarmReuseConnectionInstanceId = await GetAuthoritativeConnectionInstanceIdAsync().ConfigureAwait(false);
        var initialHasReusableProjection = HasReusableWarmProjection(activationStartState, sessionId);
        var canOptimisticallyReuseWarmRemoteConversation =
            !forceRemoteHydrationAfterSupersedingInFlightActivation
            && ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
                warmRuntimeSnapshot,
                initialWarmReuseBinding,
                initialWarmReuseConnectionInstanceId,
                initialHasReusableProjection,
                initialWarmReuseLivenessCheck);

        ((IConversationActivationPreview)this).ClearSessionSwitchPreview(sessionId);
        await PostToUiAsync(() =>
        {
            if (canOptimisticallyReuseWarmRemoteConversation)
            {
                SetConversationOverlayOwners(
                    sessionSwitchConversationId: null,
                    connectionLifecycleConversationId: null,
                    historyConversationId: null);
                IsSessionSwitching = false;
            }
            else
            {
                SetConversationOverlayOwners(
                    sessionSwitchConversationId: sessionId,
                    connectionLifecycleConversationId: null,
                    historyConversationId: null);
                IsSessionSwitching = true;
            }
        }).ConfigureAwait(false);

        await EnsureConversationWorkspaceRestoredAsync(context.CancellationToken).ConfigureAwait(false);
        var activationHydrationMode = await ResolveConversationActivationHydrationModeAsync(
                sessionId,
                context.CancellationToken)
            .ConfigureAwait(false);
        _chatUiProjectionApplicationCoordinator.ArmActivationSelectionProjection(
            sessionId,
            context.ActivationVersion);
        var activationResult = activationHydrationMode == ConversationActivationHydrationMode.SelectionOnly
            ? await _conversationActivationCoordinator
                .ActivateSessionAsync(sessionId, activationHydrationMode, context.CancellationToken)
                .ConfigureAwait(false)
            : await _conversationActivationCoordinator
                .ActivateSessionAsync(sessionId, context.CancellationToken)
                .ConfigureAwait(false);
        if (!activationResult.Succeeded)
        {
            await SetConversationRuntimeStateAsync(
                    sessionId,
                    ConversationRuntimePhase.Faulted,
                    reason: activationResult.FailureReason,
                    context.CancellationToken)
                .ConfigureAwait(false);
            return ConversationActivationOrchestratorResult.Failed();
        }

        if (IsActivationContextStale(context.ActivationVersion, context.CancellationToken))
        {
            return ConversationActivationOrchestratorResult.Superseded();
        }

        var warmReuseBinding = await ResolveConversationBindingAsync(sessionId, context.CancellationToken).ConfigureAwait(false);
        var warmReuseConnectionInstanceId = await GetAuthoritativeConnectionInstanceIdAsync().ConfigureAwait(false);
        var warmReuseAfterSelectionLivenessCheck = BuildWarmReuseConnectionLivenessCheck(warmReuseBinding);
        var warmReuseState = await _chatStore.State ?? ChatState.Empty;
        var hasReusableWarmProjection = HasReusableWarmProjection(warmReuseState, sessionId);
        var canReuseWarmConversationAfterSelection =
            !forceRemoteHydrationAfterSupersedingInFlightActivation
            && ConversationWarmReusePolicy.CanReuseRemoteWarmConversation(
                warmRuntimeSnapshot,
                warmReuseBinding,
                warmReuseConnectionInstanceId,
                hasReusableWarmProjection,
                warmReuseAfterSelectionLivenessCheck);

        await SetConversationRuntimeStateAsync(
                sessionId,
                ConversationRuntimePhase.Selected,
                reason: "WorkspaceProjectionReady",
                context.CancellationToken)
            .ConfigureAwait(false);

        await ResetRemoteHydrationUiStateAsync(context.ActivationVersion).ConfigureAwait(false);
        if (IsActivationContextStale(context.ActivationVersion, context.CancellationToken))
        {
            return ConversationActivationOrchestratorResult.Superseded();
        }

        await ApplyCurrentStoreProjectionAsync(context.ActivationVersion).ConfigureAwait(false);
        const int slowSelectionActivationThresholdMs = 1200;
        if (activationStopwatch.ElapsedMilliseconds >= slowSelectionActivationThresholdMs)
        {
            Logger.LogWarning(
                "Slow conversation selection detected. conversationId={ConversationId} activationVersion={ActivationVersion} elapsedMs={ElapsedMs}",
                sessionId,
                context.ActivationVersion,
                activationStopwatch.ElapsedMilliseconds);
        }

        if (canReuseWarmConversationAfterSelection)
        {
            await SetConversationRuntimeStateAsync(
                    sessionId,
                    ConversationRuntimePhase.Warm,
                    warmReuseBinding,
                    reason: "WarmReuse",
                    context.CancellationToken,
                    connectionInstanceId: warmReuseConnectionInstanceId)
                .ConfigureAwait(false);
            await ClearConversationUnreadAttentionAsync(sessionId).ConfigureAwait(false);
            return ConversationActivationOrchestratorResult.Success(usedWarmReuse: true);
        }

        if (activationHydrationMode == ConversationActivationHydrationMode.WorkspaceSnapshot)
        {
            await DismissSessionSwitchOverlayAsync(context.ActivationVersion, sessionId).ConfigureAwait(false);
        }

        context.ReleaseForegroundGate();
        if (!request.AwaitRemoteHydration)
        {
            _ = ContinueConversationActivationAsync(
                request,
                context,
                warmRuntimeSnapshot,
                allowWarmReuseShortCircuit: !forceRemoteHydrationAfterSupersedingInFlightActivation);
            return ConversationActivationOrchestratorResult.BackgroundOwnedSuccess();
        }

        var remoteActivationSucceeded = await CompleteConversationRemoteActivationAsync(
                sessionId,
                context.ActivationVersion,
                context.CancellationToken,
                warmRuntimeSnapshot,
                allowWarmReuseShortCircuit: !forceRemoteHydrationAfterSupersedingInFlightActivation)
            .ConfigureAwait(false);
        return remoteActivationSucceeded
            ? ConversationActivationOrchestratorResult.Success()
            : ConversationActivationOrchestratorResult.Failed();
    }

    private async Task ContinueConversationActivationAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        ConversationRuntimeSlice? warmRuntimeSnapshot,
        bool allowWarmReuseShortCircuit = true)
    {
        await Task.Yield();
        ConversationActivationOrchestratorResult result;
        try
        {
            var remoteActivationSucceeded = await CompleteConversationRemoteActivationAsync(
                    request.ConversationId,
                    context.ActivationVersion,
                    context.CancellationToken,
                    warmRuntimeSnapshot,
                    allowWarmReuseShortCircuit)
                .ConfigureAwait(false);
            result = remoteActivationSucceeded
                ? ConversationActivationOrchestratorResult.Success()
                : ConversationActivationOrchestratorResult.Failed();
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            result = ConversationActivationOrchestratorResult.Superseded();
        }
        catch (Exception ex)
        {
            await HandleConversationActivationExceptionAsync(
                    request.ConversationId,
                    context.ActivationVersion,
                    ex)
                .ConfigureAwait(false);
            result = ConversationActivationOrchestratorResult.Failed();
        }

        await _conversationActivationOrchestrator
            .CompleteDeferredActivationAsync(
                request,
                context,
                this,
                result,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    public async Task OnActivationCompletedAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        ConversationActivationOrchestratorResult result,
        CancellationToken cancellationToken = default)
    {
        if (result.Succeeded && !result.WasSuperseded)
        {
            await EnsureCurrentSessionIdAlignedAsync(
                    request.ConversationId,
                    context.ActivationVersion)
                .ConfigureAwait(false);
            NotifyConversationListChanged();
        }

        ScheduleSessionSwitchOverlayDismissal(context.ActivationVersion, request.ConversationId);
    }

    private void ApplySessionSwitchPreview(string conversationId)
    {
        if (_disposed)
        {
            return;
        }

        _sessionSwitchPreviewConversationId = conversationId;
        RaiseOverlayStateChanged();
    }

    private void ApplySessionSwitchPreviewClear(string conversationId)
    {
        if (_disposed
            || !string.Equals(_sessionSwitchPreviewConversationId, conversationId, StringComparison.Ordinal))
        {
            return;
        }

        _sessionSwitchPreviewConversationId = null;
        RaiseOverlayStateChanged();
    }

    void IConversationActivationPreview.PrimeSessionSwitchPreview(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        if (_uiDispatcher.HasThreadAccess)
        {
            ApplySessionSwitchPreview(conversationId);
            return;
        }

        _uiDispatcher.Enqueue(() => {
            ApplySessionSwitchPreview(conversationId);
        });
    }

    void IConversationActivationPreview.ClearSessionSwitchPreview(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        if (_uiDispatcher.HasThreadAccess)
        {
            ApplySessionSwitchPreviewClear(conversationId);
            return;
        }

        _uiDispatcher.Enqueue(() => {
            ApplySessionSwitchPreviewClear(conversationId);
        });
    }

    private void OnAskUserRequestReceived(object? sender, AskUserRequestEventArgs e)
    {
        _ = ProcessAskUserRequestAsync(e);
    }

    private async Task ProcessAskUserRequestAsync(AskUserRequestEventArgs e)
    {
        try
        {
            var projection = await _interactionEventBridge.BuildAskUserRequestAsync(
                e,
                conversationId => PostToUiAsync(() => RemovePendingAskUserRequestState(conversationId)),
                Logger).ConfigureAwait(false);
            if (projection is null)
            {
                return;
            }

            await PostToUiAsync(() =>
            {
                _panelStateCoordinator.StoreAskUserRequest(projection.Value.ConversationId, projection.Value.ViewModel);
                PendingAskUserRequest = _panelStateCoordinator.GetPendingAskUserRequest(CurrentSessionId);
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing ask-user request");
        }
    }

    private void RemovePendingAskUserRequestState(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _panelStateCoordinator.RemoveAskUserRequest(conversationId);
        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            PendingAskUserRequest = _panelStateCoordinator.GetPendingAskUserRequest(conversationId);
        }
    }

    private void OnPermissionRequestReceived(object? sender, PermissionRequestEventArgs e)
    {
        _uiDispatcher.Enqueue(() => {
            try
            {
                PendingPermissionRequest = _interactionEventBridge.CreatePermissionRequestViewModel(
                    e,
                    async (messageId, outcome, optionId) =>
                    {
                        if (_chatService == null)
                        {
                            return false;
                        }

                        return await _chatService.RespondToPermissionRequestAsync(messageId, outcome, optionId).ConfigureAwait(true);
                    },
                    () =>
                    {
                        ShowPermissionDialog = false;
                        PendingPermissionRequest = null;
                    });
                ShowPermissionDialog = true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing permission request");
            }
        });
    }

    private void OnFileSystemRequestReceived(object? sender, FileSystemRequestEventArgs e)
    {
        _uiDispatcher.Enqueue(() => {
            try
            {
                PendingFileSystemRequest = _interactionEventBridge.CreateFileSystemRequestViewModel(
                    e,
                    async (messageId, success, content, message) =>
                    {
                        if (_chatService != null)
                        {
                            await _chatService.RespondToFileSystemRequestAsync(messageId, success, content, message).ConfigureAwait(true);
                        }
                    },
                    () =>
                    {
                        ShowFileSystemDialog = false;
                        PendingFileSystemRequest = null;
                    });
                ShowFileSystemDialog = true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing file system request");
            }
        });
    }

    private void OnTerminalRequestReceived(object? sender, TerminalRequestEventArgs e)
    {
        _uiDispatcher.Enqueue(() => {
            try
            {
                Logger.LogInformation("Terminal request received: Method={Method}, TerminalId={TerminalId}", e.Method, e.TerminalId);
                _ = ProcessTerminalRequestAsync(e);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing terminal request");
            }
        });
    }

    private void OnTerminalStateChangedReceived(object? sender, TerminalStateChangedEventArgs e)
    {
        _uiDispatcher.Enqueue(() => {
            try
            {
                _ = ProcessTerminalStateChangedAsync(e);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing terminal state update");
            }
        });
    }

    private async Task ProcessTerminalRequestAsync(TerminalRequestEventArgs request)
    {
        try
        {
            var projection = await _interactionEventBridge.BuildTerminalRequestSelectionAsync(
                request,
                _panelStateCoordinator,
                CurrentSessionId,
                Logger).ConfigureAwait(false);
            if (projection is null)
            {
                return;
            }

            await PostToUiAsync(() => ApplyTerminalSelection(projection.Value.ConversationId, projection.Value.Selection)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing terminal request");
        }
    }

    private async Task ProcessTerminalStateChangedAsync(TerminalStateChangedEventArgs update)
    {
        try
        {
            var projection = await _interactionEventBridge.BuildTerminalStateSelectionAsync(
                update,
                _panelStateCoordinator,
                CurrentSessionId,
                Logger).ConfigureAwait(false);
            if (projection is null)
            {
                return;
            }

            await PostToUiAsync(() => ApplyTerminalSelection(projection.Value.ConversationId, projection.Value.Selection)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing terminal state update");
        }
    }

    private void ApplyTerminalSelection(string conversationId, ChatConversationPanelSelection selection)
    {
        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            TerminalSessions = selection.TerminalSessions;
            SelectedTerminalSession = selection.SelectedTerminal;
        }
    }

    private void SelectBottomPanelTab(string tabId)
    {
        if (BottomPanelTabs.FirstOrDefault(tab =>
            string.Equals(tab.Id, tabId, StringComparison.Ordinal)) is { } tab)
        {
            SelectedBottomPanelTab = tab;
        }
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        _uiDispatcher.Enqueue(() => {
            SetError(error);
            Logger.LogError(error);
        });
    }

    private Task<bool> TryAuthenticateAsync(CancellationToken cancellationToken)
        => _authenticationCoordinator.TryAuthenticateAsync(
            _chatService,
            IsInitialized,
            _acpConnectionCoordinator,
            Logger,
            message => ShowTransientNotificationToast(message),
            cancellationToken);

    private Task AddMessageToHistoryAsync(string? conversationId, ContentBlock content, bool isOutgoing)
    {
        return UpsertTranscriptSnapshotAsync(conversationId, CreateContentSnapshot(content, isOutgoing));
    }

    private async Task UpsertUserMessageChunkAsync(
        string conversationId,
        UserMessageUpdate userMessageUpdate,
        ActiveTurnState? activeTurn)
    {
        var content = userMessageUpdate.Content;
        if (content is null)
        {
            return;
        }

        var currentState = await _chatStore.State ?? ChatState.Empty;
        var transcript = currentState.ResolveContentSlice(conversationId)?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var projection = _outgoingUserMessageProjector.ResolveAuthoritativeProjection(
            transcript,
            userMessageUpdate,
            activeTurn);
        var existing = projection.ExistingSnapshot;
        var resolvedProtocolMessageId = projection.ProtocolMessageId;

        var snapshot = existing is null
            ? CreateContentSnapshot(content, isOutgoing: true, protocolMessageId: resolvedProtocolMessageId)
            : CreateContentSnapshot(
                content,
                isOutgoing: true,
                id: existing.Id,
                timestamp: existing.Timestamp,
                protocolMessageId: resolvedProtocolMessageId);

        await UpsertTranscriptSnapshotAsync(conversationId, snapshot).ConfigureAwait(true);
    }

    private ConversationMessageSnapshot CreateContentSnapshot(
        ContentBlock content,
        bool isOutgoing,
        string? id = null,
        DateTime? timestamp = null,
        string? protocolMessageId = null)
    {
        var snapshot = new ConversationMessageSnapshot
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id,
            Timestamp = timestamp ?? DateTime.UtcNow,
            IsOutgoing = isOutgoing,
            ProtocolMessageId = protocolMessageId
        };

        switch (content)
        {
            case TextContentBlock text:
                snapshot.ContentType = "text";
                snapshot.TextContent = text.Text ?? string.Empty;
                break;
            case ImageContentBlock image:
                snapshot.ContentType = "image";
                snapshot.ImageData = image.Data ?? string.Empty;
                snapshot.ImageMimeType = image.MimeType ?? string.Empty;
                break;
            case AudioContentBlock audio:
                snapshot.ContentType = "audio";
                snapshot.AudioData = audio.Data ?? string.Empty;
                snapshot.AudioMimeType = audio.MimeType ?? string.Empty;
                break;
            case ResourceContentBlock resourceContent:
                snapshot.ContentType = "resource";
                snapshot.TextContent = resourceContent.Resource?.Uri?.ToString() ?? string.Empty;
                break;
            case ResourceLinkContentBlock resourceLink:
                snapshot.ContentType = "resource_link";
                snapshot.TextContent = resourceLink.Uri?.ToString() ?? string.Empty;
                break;
            default:
                snapshot.ContentType = "text";
                snapshot.TextContent = $"[{content.GetType().Name}]";
                break;
        }

        return snapshot;
    }

    private ConversationMessageSnapshot CreateToolCallSnapshot(ToolCallUpdate toolCall)
    {
        return new ConversationMessageSnapshot
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            IsOutgoing = false,
            ContentType = "tool_call",
            Title = toolCall.Title ?? string.Empty,
            TextContent = ResolveToolCallOutput(toolCall.RawOutput, toolCall.Content, string.Empty),
            ToolCallId = toolCall.ToolCallId,
            ToolCallKind = toolCall.Kind,
            ToolCallStatus = toolCall.Status,
            ToolCallJson = ResolveToolCallPayload(toolCall.RawInput, toolCall.Content),
            ToolCallContent = ToolCallContentSnapshots.CloneList(toolCall.Content)
        };
    }

    private async Task UpsertTranscriptSnapshotAsync(string? conversationId, ConversationMessageSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        await _chatStore.Dispatch(new UpsertTranscriptMessageAction(conversationId, snapshot)).ConfigureAwait(false);
    }

    private async Task UpdateToolCallStatusAsync(string? conversationId, ToolCallStatusUpdate toolCallStatusUpdate)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrEmpty(toolCallStatusUpdate.ToolCallId))
        {
            return;
        }

        var state = await _chatStore.State ?? ChatState.Empty;
        var currentTranscript = state.ResolveContentSlice(conversationId)?.Transcript
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var existing = currentTranscript.LastOrDefault(message =>
            string.Equals(message.ToolCallId, toolCallStatusUpdate.ToolCallId, StringComparison.Ordinal)
            && string.Equals(message.ContentType, "tool_call", StringComparison.Ordinal));
        var merged = existing is null
            ? CreateToolCallSnapshot(toolCallStatusUpdate)
            : new ConversationMessageSnapshot
            {
                Id = existing.Id,
                Timestamp = DateTime.UtcNow,
                IsOutgoing = existing.IsOutgoing,
                ContentType = existing.ContentType,
                Title = string.IsNullOrWhiteSpace(toolCallStatusUpdate.Title) ? existing.Title : toolCallStatusUpdate.Title,
                TextContent = ResolveToolCallOutput(
                    toolCallStatusUpdate.RawOutput,
                    toolCallStatusUpdate.Content,
                    existing.TextContent),
                ImageData = existing.ImageData,
                ImageMimeType = existing.ImageMimeType,
                AudioData = existing.AudioData,
                AudioMimeType = existing.AudioMimeType,
                ProtocolMessageId = existing.ProtocolMessageId,
                ToolCallId = existing.ToolCallId,
                ToolCallKind = toolCallStatusUpdate.Kind ?? existing.ToolCallKind,
                ToolCallStatus = toolCallStatusUpdate.Status ?? existing.ToolCallStatus,
                ToolCallJson = ResolveToolCallPayload(toolCallStatusUpdate.RawInput, toolCallStatusUpdate.Content) ?? existing.ToolCallJson,
                ToolCallContent = toolCallStatusUpdate.Content is not null
                    ? ToolCallContentSnapshots.CloneList(toolCallStatusUpdate.Content)
                    : existing.ToolCallContent,
                PlanEntry = ClonePlanEntrySnapshot(existing.PlanEntry),
                ModeId = existing.ModeId
            };

        await _chatStore.Dispatch(new UpsertTranscriptMessageAction(conversationId, merged)).ConfigureAwait(false);
    }

    private ConversationMessageSnapshot CreateToolCallSnapshot(ToolCallStatusUpdate toolCallStatusUpdate)
    {
        return new ConversationMessageSnapshot
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            IsOutgoing = false,
            ContentType = "tool_call",
            Title = toolCallStatusUpdate.Title ?? string.Empty,
            TextContent = ResolveToolCallOutput(toolCallStatusUpdate.RawOutput, toolCallStatusUpdate.Content, string.Empty),
            ToolCallId = toolCallStatusUpdate.ToolCallId,
            ToolCallKind = toolCallStatusUpdate.Kind,
            ToolCallStatus = toolCallStatusUpdate.Status,
            ToolCallJson = ResolveToolCallPayload(toolCallStatusUpdate.RawInput, toolCallStatusUpdate.Content),
            ToolCallContent = ToolCallContentSnapshots.CloneList(toolCallStatusUpdate.Content)
        };
    }

    private static string? TryGetRawJson(System.Text.Json.JsonElement? element)
        => element?.GetRawText();

    private static List<Domain.Models.Tool.ToolCallContent>? CloneToolCallContentList(
        IReadOnlyList<Domain.Models.Tool.ToolCallContent>? content)
        => ToolCallContentSnapshots.CloneList(content);

    private static string? ResolveToolCallPayload(
        System.Text.Json.JsonElement? rawPayload,
        IReadOnlyList<Domain.Models.Tool.ToolCallContent>? content)
        => TryGetRawJson(rawPayload)
            ?? ToolCallContentSnapshots.SerializePayload(content);

    private static string ResolveToolCallOutput(
        System.Text.Json.JsonElement? rawOutput,
        IReadOnlyList<Domain.Models.Tool.ToolCallContent>? content,
        string? fallback)
    {
        var serializedOutput = TryGetRawJson(rawOutput);
        if (!string.IsNullOrWhiteSpace(serializedOutput))
        {
            return serializedOutput;
        }

        var flattened = FlattenToolCallContent(content);
        if (!string.IsNullOrWhiteSpace(flattened))
        {
            return flattened;
        }

        return fallback ?? string.Empty;
    }

    private static string FlattenToolCallContent(IReadOnlyList<Domain.Models.Tool.ToolCallContent>? content)
    {
        if (content == null || content.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(content.Count);
        foreach (var item in content)
        {
            switch (item)
            {
                case Domain.Models.Tool.ContentToolCallContent { Content: TextContentBlock textBlock } when !string.IsNullOrWhiteSpace(textBlock.Text):
                    parts.Add(textBlock.Text);
                    break;
                case Domain.Models.Tool.DiffToolCallContent diff when !string.IsNullOrWhiteSpace(diff.NewText):
                    parts.Add(diff.NewText);
                    break;
                case Domain.Models.Tool.DiffToolCallContent diff when !string.IsNullOrWhiteSpace(diff.Path):
                    parts.Add(diff.Path);
                    break;
                case Domain.Models.Tool.TerminalToolCallContent terminal when !string.IsNullOrWhiteSpace(terminal.TerminalId):
                    parts.Add(terminal.TerminalId);
                    break;
            }
        }

        return parts.Count == 0 ? string.Empty : string.Join(Environment.NewLine, parts);
    }

    private async Task PreemptivelyCancelTurnAsync(string? expectedConversationId = null, string? expectedTurnId = null)
    {
        var state = await _chatStore.State ?? ChatState.Empty;
        var activeTurn = state.ActiveTurn;
        if (activeTurn is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(expectedConversationId)
            && !string.Equals(activeTurn.ConversationId, expectedConversationId, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(expectedTurnId)
            && !string.Equals(activeTurn.TurnId, expectedTurnId, StringComparison.Ordinal))
        {
            return;
        }

        await PreemptivelyCancelOutstandingToolCallsAsync(state, activeTurn).ConfigureAwait(true);
        await _chatStore.Dispatch(new CancelTurnAction(activeTurn.ConversationId, activeTurn.TurnId)).ConfigureAwait(true);
    }

    private async Task PreemptivelyCancelOutstandingToolCallsAsync(ChatState state, ActiveTurnState activeTurn)
    {
        if (string.IsNullOrWhiteSpace(activeTurn.ConversationId))
        {
            return;
        }

        var transcript = state.ResolveContentSlice(activeTurn.ConversationId)?.Transcript
            ?? (string.Equals(state.HydratedConversationId, activeTurn.ConversationId, StringComparison.Ordinal)
                ? state.Transcript
                : null)
            ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var cutoff = activeTurn.StartedAtUtc == default ? DateTime.MinValue : activeTurn.StartedAtUtc;
        var pendingToolCalls = transcript
            .Where(message =>
                string.Equals(message.ContentType, "tool_call", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(message.ToolCallId)
                && message.Timestamp >= cutoff
                && message.ToolCallStatus is null
                    or Domain.Models.Tool.ToolCallStatus.Pending
                    or Domain.Models.Tool.ToolCallStatus.InProgress)
            .ToArray();

        foreach (var existing in pendingToolCalls)
        {
            await _chatStore.Dispatch(new UpsertTranscriptMessageAction(activeTurn.ConversationId, new ConversationMessageSnapshot
            {
                Id = existing.Id,
                Timestamp = DateTime.UtcNow,
                IsOutgoing = existing.IsOutgoing,
                ContentType = existing.ContentType,
                Title = existing.Title,
                TextContent = existing.TextContent,
                ImageData = existing.ImageData,
                ImageMimeType = existing.ImageMimeType,
                AudioData = existing.AudioData,
                AudioMimeType = existing.AudioMimeType,
                ProtocolMessageId = existing.ProtocolMessageId,
                ToolCallId = existing.ToolCallId,
                ToolCallKind = existing.ToolCallKind,
                ToolCallStatus = Domain.Models.Tool.ToolCallStatus.Cancelled,
                ToolCallJson = existing.ToolCallJson,
                ToolCallContent = CloneToolCallContentList(existing.ToolCallContent),
                PlanEntry = ClonePlanEntrySnapshot(existing.PlanEntry),
                ModeId = existing.ModeId
            })).ConfigureAwait(true);
        }
    }

    private async Task ApplySessionUpdateDeltaAsync(string conversationId, AcpSessionUpdateDelta delta)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var nextModes = delta.AvailableModes != null
            ? delta.AvailableModes.Select(ToConversationModeOptionSnapshot).ToImmutableList()
            : null;
        var nextConfigOptions = delta.ConfigOptions != null
            ? delta.ConfigOptions.Select(ToConversationConfigOptionSnapshot).ToImmutableList()
            : null;
        var nextAvailableCommands = delta.AvailableCommands != null
            ? delta.AvailableCommands.Select(ToConversationAvailableCommandSnapshot).ToImmutableList()
            : null;
        var nextSessionInfo = ToConversationSessionInfoSnapshot(delta.SessionInfo);
        if (nextSessionInfo is not null)
        {
            nextSessionInfo = NormalizeSessionInfoSnapshotForEstablishedConversationContext(
                conversationId,
                nextSessionInfo);
        }
        var nextUsage = ToConversationUsageSnapshot(delta.Usage);
        var hasSelectedModeId = !string.IsNullOrWhiteSpace(delta.SelectedModeId)
            || delta.AvailableModes is { Count: 0 };
        var nextSelectedModeId = !string.IsNullOrWhiteSpace(delta.SelectedModeId)
            ? delta.SelectedModeId
            : null;
        var nextShowConfigOptionsPanel = delta.ShowConfigOptionsPanel;
        if (nextShowConfigOptionsPanel is null && nextConfigOptions != null)
        {
            nextShowConfigOptionsPanel = nextConfigOptions.Count > 0;
        }

        await _chatStore.Dispatch(new MergeConversationSessionStateAction(
            conversationId,
            nextModes,
            nextSelectedModeId,
            hasSelectedModeId,
            nextConfigOptions,
            nextShowConfigOptionsPanel,
            nextAvailableCommands,
            nextSessionInfo,
            nextUsage)).ConfigureAwait(true);

        if (delta.PlanEntries != null)
        {
            await _chatStore.Dispatch(new ReplacePlanEntriesAction(
                conversationId,
                delta.PlanEntries.ToImmutableList(),
                delta.ShowPlanPanel ?? true,
                delta.PlanTitle)).ConfigureAwait(true);
        }

        if (nextSessionInfo is not null)
        {
            await PersistProjectedSessionInfoSnapshotAsync(conversationId).ConfigureAwait(true);
        }
    }

    private static ConversationModeOptionSnapshot ToConversationModeOptionSnapshot(AcpModeOption option)
        => new()
        {
            ModeId = option.ModeId,
            ModeName = option.ModeName,
            Description = option.Description
        };

    private static ConversationConfigOptionSnapshot ToConversationConfigOptionSnapshot(AcpConfigOptionSnapshot option)
        => new()
        {
            Id = option.Id,
            Name = option.Name,
            Description = option.Description,
            Category = option.Category,
            ValueType = option.ValueType,
            SelectedValue = option.SelectedValue,
            Options = option.Options
                .Select(static item => new ConversationConfigOptionChoiceSnapshot
                {
                    Value = item.Value,
                    Name = item.Name,
                    Description = item.Description
                })
                .ToList()
        };

    private static ConversationAvailableCommandSnapshot ToConversationAvailableCommandSnapshot(AcpAvailableCommandSnapshot command)
        => new(command.Name, command.Description, command.InputHint);

    private static ConversationSessionInfoSnapshot? ToConversationSessionInfoSnapshot(AcpSessionInfoSnapshot? sessionInfo)
    {
        if (sessionInfo is null)
        {
            return null;
        }

        var normalizedTitle = string.IsNullOrWhiteSpace(sessionInfo.Title) ? null : sessionInfo.Title;
        var normalizedDescription = string.IsNullOrWhiteSpace(sessionInfo.Description) ? null : sessionInfo.Description;
        var normalizedCwd = string.IsNullOrWhiteSpace(sessionInfo.Cwd) ? null : sessionInfo.Cwd;
        var normalizedUpdatedAt = string.IsNullOrWhiteSpace(sessionInfo.UpdatedAt) ? null : sessionInfo.UpdatedAt;
        var normalizedMeta = sessionInfo.Meta is { Count: > 0 }
            ? new Dictionary<string, object?>(sessionInfo.Meta, StringComparer.Ordinal)
            : null;
        if (normalizedTitle is null
            && normalizedDescription is null
            && normalizedCwd is null
            && normalizedUpdatedAt is null
            && normalizedMeta is null)
        {
            return null;
        }

        return new ConversationSessionInfoSnapshot
        {
            Title = normalizedTitle,
            Description = normalizedDescription,
            Cwd = normalizedCwd,
            UpdatedAtUtc = ParseSessionUpdatedAtUtc(normalizedUpdatedAt),
            Meta = normalizedMeta
        };
    }

    private static ConversationUsageSnapshot? ToConversationUsageSnapshot(AcpUsageSnapshot? usage)
    {
        if (usage is null)
        {
            return null;
        }

        return new ConversationUsageSnapshot(
            usage.Used,
            usage.Size,
            usage.Cost is null
                ? null
                : new ConversationUsageCostSnapshot(usage.Cost.Amount, usage.Cost.Currency));
    }

    private void SetSelectedModeWithoutDispatch(SessionModeViewModel? mode)
    {
        _suppressModeSelectionDispatch = true;
        try
        {
            SelectedMode = mode;
        }
        finally
        {
            _suppressModeSelectionDispatch = false;
        }
    }

    private async Task ApplySessionConfigOptionResponseAsync(
        string conversationId,
        SessionSetConfigOptionResponse response,
        string remoteSessionId)
    {
        if (response?.ConfigOptions == null)
        {
            return;
        }

        SetConversationConfigAuthority(conversationId, true);

        await ApplySessionUpdateDeltaAsync(conversationId, _acpSessionUpdateProjector.Project(
            new SessionUpdateEventArgs(
                remoteSessionId,
                new ConfigOptionUpdate
                {
                    ConfigOptions = response.ConfigOptions
                }))).ConfigureAwait(true);
    }

    private async Task ApplySessionModeResponseAsync(
        string conversationId,
        SessionSetModeResponse response,
        string remoteSessionId)
    {
        if (response is null || string.IsNullOrWhiteSpace(response.ModeId))
        {
            return;
        }

        await ApplySessionUpdateDeltaAsync(conversationId, _acpSessionUpdateProjector.Project(
            new SessionUpdateEventArgs(
                remoteSessionId,
                new CurrentModeUpdate(response.ModeId)))).ConfigureAwait(true);
    }

    private bool IsConversationConfigAuthoritative(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        return _configAuthoritativeConversationIds.Contains(conversationId);
    }

    private void SetConversationConfigAuthority(string conversationId, bool isAuthoritative)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        if (isAuthoritative)
        {
            _configAuthoritativeConversationIds.Add(conversationId);
            return;
        }

        _configAuthoritativeConversationIds.Remove(conversationId);
    }
}
