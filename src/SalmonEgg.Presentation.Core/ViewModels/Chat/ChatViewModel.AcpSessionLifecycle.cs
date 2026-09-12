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
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.ViewModels.Chat.AskUser;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;
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
using SalmonEgg.Acp.Client;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    private void OnSessionUpdateReceived(AcpSessionEventSource source, SessionUpdateEventArgs e, bool isReplay = false)
    {
        if (_disposed || _disposeCts.IsCancellationRequested || !IsCurrentEventSource(source) || !e.IsCurrent)
        {
            return;
        }

        TrackPendingSessionUpdate(_sessionUpdateWorkQueue.Enqueue(() => ProcessSessionUpdateAsync(source, e, _disposeCts.Token, isReplay)));
    }

    private async Task ProcessSessionUpdateAsync(AcpSessionEventSource source, SessionUpdateEventArgs e, CancellationToken cancellationToken, bool isReplay = false)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentEventSource(source) || !e.IsCurrent) return;
            var storeState = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentEventSource(source) || !e.IsCurrent) return;
            var activeConversationId = storeState.HydratedConversationId;
            var boundConversationId = _authoritativeRemoteSessionRouter.ResolveConversationId(storeState, e.SessionId, source);

            if (string.IsNullOrWhiteSpace(boundConversationId))
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var targetConversationId = boundConversationId!;
            RecordSessionUpdateObservation(source, e.SessionId, targetConversationId);
            var activeTurn = ResolveSessionUpdateTurn(storeState, targetConversationId, e.SessionId, source.ConnectionInstanceId);
            var targetBinding = storeState.ResolveBinding(targetConversationId);
            var isActiveTarget = string.Equals(activeConversationId, targetConversationId, StringComparison.Ordinal);
            bool IsProjectionCurrent() => e.IsCurrent && !_disposed && IsCurrentEventSource(source)
                && _chatStore.ReadCommittedState()?.ResolveBinding(targetConversationId) == targetBinding;

            if (e.View is { } applicationView && await ApplySessionUpdateViewAsync(
                targetConversationId, applicationView, activeTurn, isActiveTarget, IsProjectionCurrent, source, isReplay).ConfigureAwait(true))
            {
                RecordTranscriptProjectionObservation(source, e.SessionId, targetConversationId);
            }
            else if (e.Update is AgentMessageUpdate messageUpdate && messageUpdate.Content != null)
            {
                await AdvanceActiveTurnPhaseAsync(activeTurn, ChatTurnPhase.Responding).ConfigureAwait(true);
                await HandleAgentContentChunkAsync(targetConversationId, messageUpdate).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(source, e.SessionId, targetConversationId);
                await RecordLiveReplyAsync(targetConversationId, source, isReplay).ConfigureAwait(false);
            }
            else if (e.Update is AgentThoughtUpdate)
            {
                // Thought chunks are transient states; they trigger 'thinking' UI feedback.
                await AdvanceActiveTurnPhaseAsync(activeTurn, ChatTurnPhase.Thinking).ConfigureAwait(true);
            }
            else if (e.Update is UserMessageUpdate userMessageUpdate && userMessageUpdate.Content != null)
            {
                await UpsertUserMessageChunkAsync(targetConversationId, userMessageUpdate, activeTurn).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(source, e.SessionId, targetConversationId);
            }
            else if (e.Update is ToolCallUpdate toolCallUpdate)
            {
                await AdvanceActiveTurnPhaseAsync(
                    activeTurn,
                    ChatTurnPhase.ToolPending,
                    toolCallUpdate.ToolCallId,
                    toolCallUpdate.Title).ConfigureAwait(true);

                await UpsertTranscriptSnapshotAsync(targetConversationId, CreateToolCallSnapshot(toolCallUpdate)).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(source, e.SessionId, targetConversationId);
            }
            else if (e.Update is ToolCallStatusUpdate toolCallStatusUpdate)
            {
                var status = toolCallStatusUpdate.Status;
                ChatTurnPhase phase;
                if (status == SalmonEgg.Acp.Tool.ToolCallStatus.InProgress)
                {
                    phase = ChatTurnPhase.ToolRunning;
                }
                else if (status == SalmonEgg.Acp.Tool.ToolCallStatus.Completed
                    || status == SalmonEgg.Acp.Tool.ToolCallStatus.Failed
                    || status == SalmonEgg.Acp.Tool.ToolCallStatus.Cancelled)
                {
                    phase = ChatTurnPhase.WaitingForAgent;
                }
                else
                {
                    phase = ChatTurnPhase.ToolPending;
                }
                await AdvanceActiveTurnPhaseAsync(activeTurn, phase, toolCallStatusUpdate.ToolCallId).ConfigureAwait(true);
                await UpdateToolCallStatusAsync(targetConversationId, toolCallStatusUpdate).ConfigureAwait(true);
                RecordTranscriptProjectionObservation(source, e.SessionId, targetConversationId);
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
                            "Ignoring session mode update because config options are authoritative. conversationId={ConversationId} remoteSessionId={RemoteSessionId} modeId={ModeId}",
                            targetConversationId,
                            e.SessionId,
                            modeChange.ModeId);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _disposeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing session update");
        }
    }

    private async Task<bool> ApplySessionUpdateViewAsync(
        string conversationId, AcpSessionUpdateView view, ActiveTurnState? activeTurn, bool isActiveTarget,
        Func<bool> isCurrent, AcpSessionEventSource source, bool isReplay)
    {
        if (!isCurrent()) return true;
        if (view.Message is { Role: "thought" })
        {
            await AdvanceSessionViewPhaseAsync(activeTurn, ChatTurnPhase.Thinking, isCurrent).ConfigureAwait(true);
            return true;
        }
        if (view.Message is { } message)
        {
            var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(true);
            if (!isCurrent()) return true;
            var transcript = state.ResolveContentSlice(conversationId)?.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
            var isOutgoing = string.Equals(message.Role, "user", StringComparison.Ordinal);
            var content = message.Content;
            var existing = transcript.FirstOrDefault(item => item.IsOutgoing == isOutgoing
                && item.ProtocolMessageId == message.MessageId);
            if (existing is null && isOutgoing && content.Length > 0)
            {
                existing = _outgoingUserMessageProjector.ResolveAuthoritativeProjection(transcript,
                    new UserMessageUpdate { MessageId = message.MessageId, Content = content[0] }, activeTurn).ExistingSnapshot;
            }
            var items = ImmutableList.CreateBuilder<ConversationMessageSnapshot>();
            for (var index = 0; index < content.Length; index++)
            {
                var id = index == 0 && existing is not null ? existing.Id
                    : $"acp:{message.Role}:{message.MessageId.Length}:{message.MessageId}:{index}";
                items.Add(CreateContentSnapshot(content[index], isOutgoing, id,
                    timestamp: existing?.Timestamp, protocolMessageId: message.MessageId));
            }
            await CommitSessionViewAsync(new ReplaceProtocolMessageAction(conversationId, message.MessageId,
                isOutgoing, items.ToImmutable(), existing?.Id), isCurrent).ConfigureAwait(true);
            if (!isCurrent()) return true;
            if (!isOutgoing)
            {
                await AdvanceSessionViewPhaseAsync(activeTurn, ChatTurnPhase.Responding, isCurrent).ConfigureAwait(true);
                await RecordLiveReplyAsync(conversationId, source, isReplay).ConfigureAwait(false);
            }
            return true;
        }
        if (view.ToolCall is { } tool)
        {
            var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(true);
            if (!isCurrent()) return true;
            var existing = state.ResolveContentSlice(conversationId)?.Transcript
                .LastOrDefault(item => item.ToolCallId == tool.ToolCallId && item.ContentType == "tool_call");
            var replacement = CreateToolCallSnapshot(tool);
            if (existing is not null)
            {
                replacement.Id = existing.Id;
                replacement.Timestamp = existing.Timestamp;
            }
            await CommitSessionViewAsync(new UpsertTranscriptMessageAction(conversationId, replacement), isCurrent).ConfigureAwait(true);
            if (!isCurrent()) return true;
            await AdvanceSessionViewPhaseAsync(activeTurn,
                tool.Status == SalmonEgg.Acp.Tool.ToolCallStatus.InProgress ? ChatTurnPhase.ToolRunning : ChatTurnPhase.WaitingForAgent,
                isCurrent, tool.ToolCallId, tool.Title).ConfigureAwait(true);
            return true;
        }
        if (view.Terminal is { } terminal)
        {
            await PostToUiAsync(() =>
            {
                if (!isCurrent()) return;
                var row = _panelStateCoordinator.GetOrCreateTerminalSession(conversationId, terminal.TerminalId);
                row.DisplayName = terminal.Command ?? terminal.TerminalId;
                row.Output = terminal.OutputText;
                row.ExitCode = terminal.ExitCode;
                row.IsReleased = terminal.HasExited;
                row.LastMethod = "session/update";
                ApplyTerminalSelection(conversationId, _panelStateCoordinator.SelectTerminal(conversationId, row, isActiveTarget));
            }).ConfigureAwait(false);
            return true;
        }
        if (view.WorkState is { } workState)
        {
            if (workState is "running" or "requires_action"
                && (activeTurn is null || activeTurn.Phase is ChatTurnPhase.Completed or ChatTurnPhase.Cancelled or ChatTurnPhase.Failed))
            {
                var turnId = Guid.NewGuid().ToString();
                var binding = _chatStore.ReadCommittedState()?.ResolveBinding(conversationId);
                await CommitSessionViewAsync(new BeginTurnAction(conversationId, turnId, ChatTurnPhase.WaitingForAgent,
                    ProfileId: source.ProfileId, RemoteSessionId: binding?.RemoteSessionId,
                    ConnectionInstanceId: source.ConnectionInstanceId), isCurrent).ConfigureAwait(true);
                if (!isCurrent()) return true;
                activeTurn = (await _chatStore.GetCurrentStateAsync().ConfigureAwait(true)).ResolveTurn(conversationId);
                if (!isCurrent()) return true;
            }
            if (activeTurn is not null && workState == "idle")
            {
                await RequestVisibleReplyObservationAsync().ConfigureAwait(true);
                if (!isCurrent()) return true;
                if (view.StopReason == StopReason.Cancelled)
                    await CommitSessionViewAsync(new CancelTurnAction(conversationId, activeTurn.TurnId,
                        ConnectionInstanceId: source.ConnectionInstanceId), isCurrent).ConfigureAwait(true);
                else if (view.StopReason == StopReason.Refusal || view.StopReason == StopReason.MaxTokens || view.StopReason == StopReason.MaxTurnRequests)
                    await CommitSessionViewAsync(new FailTurnAction(conversationId, activeTurn.TurnId,
                        StopReason: view.StopReason?.Value, HasStopReason: view.StopReason is not null,
                        ConnectionInstanceId: source.ConnectionInstanceId), isCurrent).ConfigureAwait(true);
                else
                    await CommitSessionViewAsync(new CompleteTurnAction(conversationId, activeTurn.TurnId,
                        StopReason: view.StopReason?.Value, HasStopReason: view.StopReason is not null,
                        ConnectionInstanceId: source.ConnectionInstanceId), isCurrent).ConfigureAwait(true);
            }
            else if (workState is "running" or "requires_action")
            {
                await AdvanceSessionViewPhaseAsync(activeTurn,
                    workState == "requires_action" ? ChatTurnPhase.WaitingForUser : ChatTurnPhase.WaitingForAgent,
                    isCurrent).ConfigureAwait(true);
            }
            return true;
        }
        if (view.HasPlanEntries)
        {
            var plan = _acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(string.Empty, new PlanUpdate(view.PlanEntries.ToList())));
            await CommitSessionViewAsync(new ReplacePlanEntriesAction(conversationId,
                plan.PlanEntries!.ToImmutableList(), true), isCurrent).ConfigureAwait(true);
            return true;
        }
        if (view.HasConfigOptions)
        {
            var config = _acpSessionUpdateProjector.Project(new SessionUpdateEventArgs(string.Empty,
                new ConfigOptionUpdate { ConfigOptions = view.ConfigOptions.ToList() }));
            await PostToUiAsync(async () =>
            {
                if (!isCurrent()) return;
                SetConversationConfigAuthority(conversationId, true);
                await _chatStore.Dispatch(new MergeConversationSessionStateAction(conversationId,
                    AvailableModes: config.AvailableModes?.Select(ToConversationModeOptionSnapshot).ToImmutableList(),
                    SelectedModeId: config.SelectedModeId,
                    HasSelectedModeId: true,
                    ConfigOptions: config.ConfigOptions?.Select(ToConversationConfigOptionSnapshot).ToImmutableList(),
                    ShowConfigOptionsPanel: config.ShowConfigOptionsPanel)).ConfigureAwait(true);
            }).ConfigureAwait(false);
            return true;
        }
        return false;
    }

    private Task CommitSessionViewAsync(ChatAction action, Func<bool> isCurrent)
        => PostToUiAsync(async () =>
        {
            if (!isCurrent()) return;
            await _chatStore.Dispatch(action).ConfigureAwait(true);
        });

    private Task AdvanceSessionViewPhaseAsync(ActiveTurnState? turn, ChatTurnPhase phase,
        Func<bool> isCurrent, string? toolCallId = null, string? title = null)
        => turn is null ? Task.CompletedTask : CommitSessionViewAsync(
            new AdvanceTurnPhaseAction(turn.ConversationId, turn.TurnId, phase, toolCallId, title,
                ConnectionInstanceId: turn.ConnectionInstanceId), isCurrent);

    private void RaiseOverlayStateChanged()
    {
        RefreshCurrentSessionDisplayName();
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
        _ = PublishTaskOverviewContentAvailabilityAsync();
        NotifyComposerProjectionChanged();
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

    private int GetPendingSessionUpdateCount()
    {
        lock (_sessionUpdateTrackingSync)
        {
            return _pendingSessionUpdateCount;
        }
    }

    private static Task WaitForAdapterReplayDrainAsync(AcpSessionEventSource source, long hydrationAttemptId, CancellationToken cancellationToken)
    {
        if (source.Service is not IAcpSessionUpdateBufferController adapter)
        {
            return Task.CompletedTask;
        }

        return adapter.WaitForBufferedUpdatesDrainedAsync(hydrationAttemptId, cancellationToken);
    }

    private async Task AwaitBufferedSessionReplayProjectionAsync(
        AcpSessionEventSource source,
        CancellationToken cancellationToken,
        long? hydrationAttemptId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await AwaitUiProjectionTurnAsync().ConfigureAwait(false);
        if (source.Service is IAcpSessionUpdateBufferController adapter
            && hydrationAttemptId.HasValue)
        {
            await adapter
                .WaitForBufferedUpdatesDrainedAsync(hydrationAttemptId.Value, cancellationToken)
                .ConfigureAwait(false);

            await AwaitUiProjectionTurnAsync().ConfigureAwait(false);
        }

        var pendingUpdates = WaitForPendingSessionUpdatesAsync();
        if (!pendingUpdates.IsCompleted)
        {
            await pendingUpdates.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await AwaitUiProjectionTurnAsync().ConfigureAwait(false);
    }

    private readonly record struct SessionObservationKey(AcpSessionEventSource Source, string RemoteSessionId);

    private ConversationHydrationContext CreateHydrationContext(AcpSessionEventSource source)
        => new()
        {
            SetHydrationPhaseAsync = SetConversationHydrationPhaseAsync,
            GetSessionUpdateObservationCount = remoteSessionId => GetSessionUpdateObservationCount(source, remoteSessionId),
            GetTranscriptProjectionObservationCount = remoteSessionId => GetTranscriptProjectionObservationCount(source, remoteSessionId),
            GetSessionUpdateLastObservedAtUtc = remoteSessionId => GetSessionUpdateLastObservedAtUtc(source, remoteSessionId),
            AwaitBufferedReplayProjectionAsync = (token, attempt) => AwaitBufferedSessionReplayProjectionAsync(source, token, attempt),
            GetProjectedTranscriptCountAsync = GetProjectedTranscriptCountAsync,
            YieldToUiAsync = AwaitUiProjectionTurnAsync,
            WaitForAdapterDrainAsync = (attempt, token) => WaitForAdapterReplayDrainAsync(source, attempt, token),
            WaitForPendingSessionUpdatesAsync = WaitForPendingSessionUpdatesAsync
        };

    private void RecordSessionUpdateObservation(AcpSessionEventSource source, string? sessionId, string conversationId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var key = new SessionObservationKey(source, sessionId);
        lock (_sessionUpdateObservationSync)
        {
            _sessionUpdateObservationCounts[key] =
                _sessionUpdateObservationCounts.TryGetValue(key, out var current)
                    ? checked(current + 1)
                    : 1;
            _sessionUpdateLastObservedAtUtc[key] = DateTime.UtcNow;
        }

        if (OverlayLoadingStage != LoadingOverlayStage.HydratingHistory)
        {
            return;
        }

        if (string.Equals(_historyOverlayConversationId, conversationId, StringComparison.Ordinal)
            && _remoteHydrationObservationSources.TryGetValue(conversationId, out var expected)
            && expected.Matches(source))
        {
            SetHydrationOverlayPhase(conversationId, HydrationOverlayPhase.ReplayingSessionUpdates);
            RaiseOverlayStatusTextChanged();
        }
    }

    private void RecordTranscriptProjectionObservation(AcpSessionEventSource source, string? sessionId, string conversationId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var key = new SessionObservationKey(source, sessionId);
        lock (_sessionUpdateObservationSync)
        {
            _sessionTranscriptProjectionObservationCounts[key] =
                _sessionTranscriptProjectionObservationCounts.TryGetValue(key, out var current)
                    ? checked(current + 1)
                    : 1;
        }

        if (OverlayLoadingStage != LoadingOverlayStage.HydratingHistory)
        {
            return;
        }

        if (string.Equals(_historyOverlayConversationId, conversationId, StringComparison.Ordinal)
            && _remoteHydrationObservationSources.TryGetValue(conversationId, out var expected)
            && expected.Matches(source))
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

    private long GetSessionUpdateObservationCount(AcpSessionEventSource source, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return 0;
        }

        lock (_sessionUpdateObservationSync)
        {
            return _sessionUpdateObservationCounts.TryGetValue(new(source, sessionId), out var count)
                ? count
                : 0;
        }
    }

    private long GetTranscriptProjectionObservationCount(AcpSessionEventSource source, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return 0;
        }

        lock (_sessionUpdateObservationSync)
        {
            return _sessionTranscriptProjectionObservationCounts.TryGetValue(new(source, sessionId), out var count)
                ? count
                : 0;
        }
    }

    private DateTime? GetSessionUpdateLastObservedAtUtc(AcpSessionEventSource source, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        lock (_sessionUpdateObservationSync)
        {
            return _sessionUpdateLastObservedAtUtc.TryGetValue(new(source, sessionId), out var observedAtUtc)
                ? observedAtUtc
                : null;
        }
    }

    private Task AwaitRemoteReplaySettleQuietPeriodAsync(
        ConversationHydrationContext context,
        string remoteSessionId,
        long replayBaseline,
        CancellationToken cancellationToken)
        => _hydrationCoordinator.AwaitRemoteReplaySettleQuietPeriodAsync(
            context,
            remoteSessionId,
            replayBaseline,
            cancellationToken);

    private async Task AwaitRemoteReplayProjectionAsync(
        AcpSessionEventSource source,
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

        while (GetSessionUpdateObservationCount(source, remoteSessionId) <= replayBaseline
            && DateTime.UtcNow < replayStartTimeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(RemoteReplayPollDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        if (GetSessionUpdateObservationCount(source, remoteSessionId) > replayBaseline)
        {
            await SetHydrationOverlayPhaseAsync(
                    conversationId,
                    activationVersion,
                    HydrationOverlayPhase.ReplayingSessionUpdates)
                .ConfigureAwait(false);
        }

        var transcriptTimeoutAt = DateTime.UtcNow + RemoteReplayStartTimeout;
        while (GetTranscriptProjectionObservationCount(source, remoteSessionId) <= transcriptProjectionBaseline
            && DateTime.UtcNow < transcriptTimeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(RemoteReplayPollDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        if (GetTranscriptProjectionObservationCount(source, remoteSessionId) > transcriptProjectionBaseline)
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
            await AwaitRemoteReplaySettleQuietPeriodAsync(CreateHydrationContext(source), remoteSessionId, replayBaseline, cancellationToken).ConfigureAwait(false);
        }

        await SetHydrationOverlayPhaseAsync(
                conversationId,
                activationVersion,
                HydrationOverlayPhase.FinalizingProjection)
            .ConfigureAwait(false);

#if DEBUG
        Logger.LogDebug(
            "Remote replay wait finished. remoteSessionId={RemoteSessionId} replayBaseline={ReplayBaseline} observedCount={ObservedCount} transcriptBaseline={TranscriptProjectionBaseline} transcriptObservedCount={TranscriptObservedCount} startTimedOut={StartTimedOut} transcriptTimedOut={TranscriptTimedOut}",
            remoteSessionId,
            replayBaseline,
            GetSessionUpdateObservationCount(source, remoteSessionId),
            transcriptProjectionBaseline,
            GetTranscriptProjectionObservationCount(source, remoteSessionId),
            DateTime.UtcNow >= replayStartTimeoutAt,
            DateTime.UtcNow >= transcriptTimeoutAt);
#endif
        await AwaitBufferedSessionReplayProjectionAsync(source, cancellationToken, hydrationAttemptId).ConfigureAwait(false);
    }

    private static ActiveTurnState? ResolveSessionUpdateTurn(ChatState storeState, string? conversationId, string remoteSessionId, string? connectionInstanceId)
    {
        if (storeState.ResolveTurn(conversationId) is not { } activeTurn
            || !string.Equals(activeTurn.ConnectionInstanceId, connectionInstanceId, StringComparison.Ordinal))
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
                ToolTitle: toolTitle,
                ConnectionInstanceId: activeTurn.ConnectionInstanceId)).ConfigureAwait(true);
    }

    private async Task ApplyPromptDispatchResultAsync(
        string conversationId,
        string turnId,
        string? remoteSessionId,
        SessionPromptResponse response,
        AcpSessionEventSource source,
        CancellationToken cancellationToken)
    {
        // The adapter queues native dispatch separately from state ingress. Drain before joining
        // the latter so end_turn cannot overtake its final content or deadlock waiting behind itself.
        if (source.Service is AcpChatServiceAdapter adapter)
        {
            await adapter.WaitForSessionUpdatesDrainedAsync(remoteSessionId, cancellationToken).ConfigureAwait(false);
        }

        await _sessionUpdateWorkQueue.Enqueue(async () =>
        {
            if (!IsCurrentEventSource(source)) return;
            await ApplyPromptTerminalResultAsync(conversationId, turnId, remoteSessionId, response, source).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task ApplyPromptTerminalResultAsync(
        string conversationId, string turnId, string? remoteSessionId, SessionPromptResponse response, AcpSessionEventSource source)
    {
        await RequestVisibleReplyObservationAsync().ConfigureAwait(false);
        var pendingSessionUpdateCount = GetPendingSessionUpdateCount();
        Logger.LogInformation(
            "Chat prompt response received. ConversationId={ConversationId} TurnId={TurnId} RemoteSessionId={RemoteSessionId} StopReason={StopReason} PendingSessionUpdateCount={PendingSessionUpdateCount}",
            conversationId,
            turnId,
            remoteSessionId,
            response.StopReason,
            pendingSessionUpdateCount);

        ChatTurnPhase terminalPhase;
        var stopReason = response.StopReason;
        if (!response.HasStopReason)
        {
            await _chatStore.Dispatch(new FailTurnAction(conversationId, turnId,
                Localize("ChatOperation_MissingStopReason", "The agent ended the response without an ACP stop reason."),
                HasStopReason: false, ConnectionInstanceId: source.ConnectionInstanceId)).ConfigureAwait(false);
            terminalPhase = ChatTurnPhase.Failed;
        }
        else if (stopReason == StopReason.Cancelled)
        {
            await PreemptivelyCancelTurnAsync(conversationId, turnId, source.ConnectionInstanceId).ConfigureAwait(true);
            terminalPhase = ChatTurnPhase.Cancelled;
        }
        else if (stopReason == StopReason.Refusal || stopReason == StopReason.MaxTokens || stopReason == StopReason.MaxTurnRequests)
        {
            await _chatStore.Dispatch(new FailTurnAction(conversationId, turnId, stopReason.ToString(),
                stopReason.Value, response.HasStopReason, source.ConnectionInstanceId)).ConfigureAwait(true);
            terminalPhase = ChatTurnPhase.Failed;
        }
        else
        {
            await _chatStore.Dispatch(new CompleteTurnAction(conversationId, turnId,
                stopReason.Value, response.HasStopReason, source.ConnectionInstanceId)).ConfigureAwait(true);
            terminalPhase = ChatTurnPhase.Completed;
        }

        Logger.LogInformation(
                "Chat prompt terminal phase applied. ConversationId={ConversationId} TurnId={TurnId} RemoteSessionId={RemoteSessionId} ConnectionInstanceId={ConnectionInstanceId} StopReason={StopReason} TerminalPhase={TerminalPhase}",
                conversationId,
                turnId,
                remoteSessionId,
                source.ConnectionInstanceId,
                response.StopReason,
                terminalPhase);
    }

    private async Task HandleAgentContentChunkAsync(string? conversationId, AgentMessageUpdate update)
    {
        var content = update.Content;
        if (content is null)
        {
            return;
        }

        // ACP streams response content as an array of blocks. We coalesce adjacent text blocks
        // into a single UI element only when they belong to the same authoritative messageId.
        if (content is TextContentBlock text)
        {
            await AppendAgentTextChunkAsync(conversationId, text.Text ?? string.Empty, update.MessageId).ConfigureAwait(true);
            return;
        }

        await AddMessageToHistoryAsync(conversationId, content, isOutgoing: false).ConfigureAwait(true);
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

    private async Task AppendAgentTextChunkAsync(string? conversationId, string chunk, string? protocolMessageId)
    {
        if (string.IsNullOrWhiteSpace(chunk) || string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        await _chatStore.Dispatch(new AppendTextDeltaAction(conversationId, chunk, protocolMessageId)).ConfigureAwait(false);
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
        ConversationFailurePublicationContext failureContext,
        CancellationToken cancellationToken,
        ConversationRuntimeSlice? warmRuntimeSnapshot = null,
        bool allowWarmReuseShortCircuit = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessionId = failureContext.ConversationId;
        var activationVersion = failureContext.ActivationVersion!.Value;

        var state = await _chatStore.GetCurrentStateAsync();
        var binding = await ResolveConversationBindingAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var runtimeState = ResolveWarmReuseRuntimeState(
            warmRuntimeSnapshot,
            state.ResolveRuntimeState(sessionId));
        await MaterializeWarmReusableProjectionFromWorkspaceIfNeededAsync(
                sessionId,
                cancellationToken)
            .ConfigureAwait(false);
        state = await _chatStore.GetCurrentStateAsync();
        binding = await ResolveConversationBindingAsync(sessionId, cancellationToken).ConfigureAwait(false);
        runtimeState = ResolveWarmReuseRuntimeState(
            warmRuntimeSnapshot,
            state.ResolveRuntimeState(sessionId));
        var hasReusableProjection = HasReusableWarmProjection(state, sessionId);
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            if (!string.IsNullOrWhiteSpace(binding?.ProfileId))
            {
                await SetConversationRuntimeStateAsync(
                        sessionId,
                        ConversationRuntimePhase.Faulted,
                        binding,
                        reason: "MissingRemoteSessionId",
                        cancellationToken)
                    .ConfigureAwait(false);
                await PublishConversationFailureAsync(
                        failureContext,
                        "MissingRemoteSessionId",
                        "ChatOperation_LoadSessionMissingProfileBinding",
                        "Failed to load session: no remote session binding is available for the profile-bound conversation.")
                    .ConfigureAwait(false);
                return false;
            }

            await SetConversationRuntimeStateAsync(
                    sessionId,
                    ConversationRuntimePhase.Warm,
                    binding,
                    reason: "LocalConversationReady",
                    cancellationToken)
                .ConfigureAwait(false);
            var localActivationStillCurrent = _conversationActivationOrchestrator.IsLatestActivationVersion(activationVersion);
            if (localActivationStillCurrent)
            {
                await PublishConversationActivationPhaseAsync(
                        failureContext,
                        SessionActivationPhase.Hydrated,
                        reason: "LocalConversationReady")
                    .ConfigureAwait(false);
            }

            return localActivationStillCurrent;
        }

        var currentConnection = await ResolveWarmReuseConnectionIdentityAsync(binding, cancellationToken).ConfigureAwait(false);
        var canAttemptWarmReuseShortCircuit = CanAttemptWarmReuseShortCircuit(
            allowWarmReuseShortCircuit,
            runtimeState);
        var warmReuseDecision = ConversationWarmReusePolicy.EvaluateRemoteWarmConversation(
            runtimeState,
            binding,
            currentConnection,
            hasReusableProjection);
        if (canAttemptWarmReuseShortCircuit && warmReuseDecision.CanReuse)
        {
            Logger.LogInformation(
                "Skipping remote hydration because the selected conversation is already warm. ConversationId={ConversationId}",
                sessionId);
            var warmActivationStillCurrent = _conversationActivationOrchestrator.IsLatestActivationVersion(activationVersion);
            if (warmActivationStillCurrent)
            {
                await PublishConversationActivationPhaseAsync(
                        failureContext,
                        SessionActivationPhase.Hydrated,
                        reason: ConversationRuntimeReasons.WarmReuse)
                    .ConfigureAwait(false);
            }

            return warmActivationStillCurrent;
        }

        {
            var denialReason = canAttemptWarmReuseShortCircuit
                ? warmReuseDecision.DenialReason
                : "SupersededInFlightActivationRequiresAuthoritativeHydration";
            Logger.LogInformation(
                "Warm reuse denied in HydrateConversationAsync, falling back to slow hydration. ConversationId={ConversationId} RemoteSessionId={RemoteSessionId} ExpectedConnectionInstanceId={ExpectedConnectionInstanceId} ActualConnectionInstanceId={ActualConnectionInstanceId} Reason={Reason}",
                sessionId,
                binding?.RemoteSessionId,
                runtimeState?.ConnectionInstanceId,
                currentConnection.ConnectionInstanceId,
                denialReason);
        }

        await EnsureSelectedProfileConnectionForConversationAsync(
                sessionId,
                activationVersion,
                cancellationToken)
            .ConfigureAwait(false);

        state = await _chatStore.GetCurrentStateAsync();
        binding = await ResolveConversationBindingAsync(sessionId, cancellationToken).ConfigureAwait(false);
        currentConnection = await ResolveWarmReuseConnectionIdentityAsync(binding, cancellationToken).ConfigureAwait(false);
        var warmRuntimeAfterProfileReconnect = ResolveWarmReuseRuntimeState(
            warmRuntimeSnapshot,
            state.ResolveRuntimeState(sessionId));
        hasReusableProjection = HasReusableWarmProjection(state, sessionId);
        canAttemptWarmReuseShortCircuit = CanAttemptWarmReuseShortCircuit(
            allowWarmReuseShortCircuit,
            warmRuntimeAfterProfileReconnect);
        warmReuseDecision = ConversationWarmReusePolicy.EvaluateRemoteWarmConversation(
            warmRuntimeAfterProfileReconnect,
            binding,
            currentConnection,
            hasReusableProjection);
        if (canAttemptWarmReuseShortCircuit && warmReuseDecision.CanReuse)
        {
            Logger.LogInformation(
                "Skipping remote hydration because the selected conversation became warm after restoring the reusable profile connection. ConversationId={ConversationId}",
                sessionId);
            await SetConversationRuntimeStateAsync(
                    sessionId,
                    ConversationRuntimePhase.Warm,
                    binding,
                    reason: ConversationRuntimeReasons.WarmReuseAfterProfileReconnect,
                    cancellationToken,
                    connectionInstanceId: currentConnection.ConnectionInstanceId)
                .ConfigureAwait(false);
            var reconnectActivationStillCurrent = _conversationActivationOrchestrator.IsLatestActivationVersion(activationVersion);
            if (reconnectActivationStillCurrent)
            {
            }

            return reconnectActivationStillCurrent;
        }

        var remotePhaseStopwatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();

        var remoteConnectionReady = await EnsureActiveConversationRemoteConnectionReadyAsync(
                sessionId,
                failureContext,
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
        await PublishConversationActivationPhaseAsync(
                failureContext,
                SessionActivationPhase.RemoteConnectionReady,
                reason: "RemoteConnectionReady")
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var hydrated = await EnsureActiveConversationRemoteHydratedAsync(
                sessionId,
                failureContext,
                cancellationToken,
                allowWarmReuseShortCircuit)
            .ConfigureAwait(false);
        var activationStillCurrent = _conversationActivationOrchestrator.IsLatestActivationVersion(activationVersion);
        if (hydrated && activationStillCurrent)
        {
            await PublishConversationActivationPhaseAsync(
                    failureContext,
                    SessionActivationPhase.Hydrated,
                    reason: "Hydrated")
                .ConfigureAwait(false);
        }
        else if (!hydrated && activationStillCurrent)
        {
            // Hydration can end without a terminal phase: a recovery projection is discarded when
            // the binding or the remote connection identity changes mid-flight, and that path
            // restores the runtime slice without touching the activation surface. Nothing further
            // downstream publishes a terminal phase either, so the activation would sit on a
            // non-terminal phase forever and the shell would neither settle nor report a failure.
            // Publishing here is the terminal backstop for this owner's own activation; the
            // publisher keeps an already-published, more specific fault reason.
            await PublishConversationActivationPhaseAsync(
                    failureContext,
                    SessionActivationPhase.Faulted,
                    reason: "RemoteHydrationStalled")
                .ConfigureAwait(false);
        }
        var succeeded = hydrated && activationStillCurrent;
        Logger.LogInformation(
            "Conversation remote activation completed. ConversationId={ConversationId} Succeeded={Succeeded} Hydrated={Hydrated} ActivationStillCurrent={ActivationStillCurrent} ElapsedMs={ElapsedMs}",
            sessionId,
            succeeded,
            hydrated,
            activationStillCurrent,
            remotePhaseStopwatch.ElapsedMilliseconds);
        return succeeded;
    }

    private async Task HandleConversationActivationExceptionAsync(
        ConversationFailurePublicationContext failureContext,
        Exception ex)
    {
        var sessionId = failureContext.ConversationId;
        var activationVersion = failureContext.ActivationVersion;

        // A superseded (no longer the latest intent) or canceled activation is expected churn during
        // rapid session switching, not a real failure. Log it at Information and stop before surfacing
        // any user-facing failure, so these do not masquerade as [ERR] "Switching session failed".
        var superseded = !_conversationActivationOutcomePublisher.CanPublish(activationVersion);
        var canceled = ex is OperationCanceledException || AcpErrorClassifier.IsRequestCancelled(ex);
        if (superseded || canceled)
        {
            Logger.LogInformation(
                ex,
                "Conversation activation stopped without surfacing an error because it was {Outcome}. conversationId={ConversationId} activationVersion={ActivationVersion}",
                superseded ? "superseded by a newer activation" : "canceled",
                sessionId,
                activationVersion);
            return;
        }

        Logger.LogError(ex, "Switching session failed (SessionId={SessionId})", sessionId);

        await PublishConversationFailureAsync(
                failureContext,
                ex.GetType().Name,
                "ChatOperation_SwitchSessionFailed",
                "Failed to switch session: {0}",
                ex.Message)
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

    private async Task<bool> CommitActivatedConversationStateAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var committed = await _conversationWorkspace
            .CommitActivatedConversationAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (!committed)
        {
            Logger.LogWarning(
                "Failed to commit activated conversation state. conversationId={ConversationId}",
                sessionId);
        }

        return committed;
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

    private void SyncConversationPanelState(string? conversationId)
    {
        var selection = _panelRuntimeCoordinator.SyncConversation(_panelStateCoordinator, conversationId);
        TerminalSessions = selection.TerminalSessions;
        SelectedTerminalSession = selection.SelectedTerminal;
        PendingAskUserRequest = selection.PendingAskUserRequest;
        PendingElicitationRequest = selection.PendingElicitationRequest;
        SyncPermissionRequestProjection();
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

    void IConversationPanelCleanup.CleanupAfterMutation(string conversationId, bool clearsActiveConversation)
    {
        if (_uiDispatcher.HasThreadAccess)
        {
            RemoveBottomPanelState(conversationId);
            RetireRemovedConversationActivation(conversationId, clearsActiveConversation);
            return;
        }

        _ = PostToUiAsync(() =>
        {
            RemoveBottomPanelState(conversationId);
            RetireRemovedConversationActivation(conversationId, clearsActiveConversation);
        });
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
            TerminalSessions = selection.TerminalSessions;
            SelectedTerminalSession = selection.SelectedTerminal;
            ActiveLocalTerminalSession = null;
            PendingAskUserRequest = selection.PendingAskUserRequest;
            PendingElicitationRequest = selection.PendingElicitationRequest;
            SyncPermissionRequestProjection();
        }
    }

    private void RetireRemovedConversationActivation(string conversationId, bool clearsActiveConversation)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        // The activation fields travel as a set, so report the removal to the runtime state and let it
        // retire them together rather than clearing individual fields from here.
        var ownsShellActivation = _shellNavigationRuntimeState?.TryRetireSession(conversationId) == true;
        _ = RemoveConversationOverlayOwners(conversationId);
        if (ownsShellActivation
            || string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            _conversationActivationOrchestrator.SupersedeCurrentActivation("ConversationRemoved");
        }
    }

    private bool RemoveConversationOverlayOwners(string conversationId)
    {
        var ownsSessionSwitchOverlay = string.Equals(_sessionSwitchOverlayConversationId, conversationId, StringComparison.Ordinal);
        var ownsSessionSwitchPreview = string.Equals(_sessionSwitchPreviewConversationId, conversationId, StringComparison.Ordinal);
        var ownsConnectionLifecycleOverlay = string.Equals(_connectionLifecycleOverlayConversationId, conversationId, StringComparison.Ordinal);
        var ownsHistoryOverlay = string.Equals(_historyOverlayConversationId, conversationId, StringComparison.Ordinal);
        var ownsPendingHistoryDismissal = string.Equals(_pendingHistoryOverlayDismissConversationId, conversationId, StringComparison.Ordinal);
        var ownsOverlay = ownsSessionSwitchOverlay
            || ownsSessionSwitchPreview
            || ownsConnectionLifecycleOverlay
            || ownsHistoryOverlay
            || ownsPendingHistoryDismissal;

        if (!ownsOverlay)
        {
            return false;
        }

        if (ownsSessionSwitchPreview)
        {
            _sessionSwitchPreviewConversationId = null;
        }

        if (ownsPendingHistoryDismissal)
        {
            _pendingHistoryOverlayDismissConversationId = null;
        }

        if (ownsSessionSwitchOverlay || ownsSessionSwitchPreview)
        {
            IsSessionSwitching = false;
        }

        if (ownsHistoryOverlay || ownsConnectionLifecycleOverlay)
        {
            IsRemoteHydrationPending = false;
        }

        ClearKnownTranscriptGrowthRequirement(conversationId);
        var overlayOwnersChanged = SetConversationOverlayOwners(
            sessionSwitchConversationId: ownsSessionSwitchOverlay ? null : _sessionSwitchOverlayConversationId,
            connectionLifecycleConversationId: ownsConnectionLifecycleOverlay ? null : _connectionLifecycleOverlayConversationId,
            historyConversationId: ownsHistoryOverlay ? null : _historyOverlayConversationId);
        if (ownsSessionSwitchPreview && !overlayOwnersChanged)
        {
            RaiseOverlayStateChanged();
        }

        return true;
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

    async Task<DiscoverRemoteSessionOpenResult> IConversationSessionSwitcher.OpenDiscoveredRemoteSessionAsync(
        DiscoverRemoteSessionOpenRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.RemoteSessionId))
        {
            return new DiscoverRemoteSessionOpenResult(false, null, "RemoteSessionIdMissing");
        }

        if (string.IsNullOrWhiteSpace(request.RemoteSessionCwd))
        {
            return new DiscoverRemoteSessionOpenResult(
                false,
                null,
                AcpSessionNewCwdResolver.MissingRemoteCwdMessage);
        }

        var localConversationId = Guid.NewGuid().ToString("N");
        try
        {
            await _sessionManager.CreateSessionAsync(localConversationId, request.RemoteSessionCwd).ConfigureAwait(false);

            await _conversationWorkspace.RegisterConversationAsync(
                localConversationId,
                createdAt: DateTime.UtcNow,
                lastUpdatedAt: DateTime.UtcNow,
                cancellationToken).ConfigureAwait(false);

            var bindingResult = await _bindingCommands
                .UpdateBindingAsync(localConversationId, request.RemoteSessionId.Trim(), request.ProfileId)
                .ConfigureAwait(false);
            if (bindingResult.Status is not BindingUpdateStatus.Success)
            {
                await RollBackDiscoveredConversationAsync(localConversationId, CancellationToken.None).ConfigureAwait(false);
                return new DiscoverRemoteSessionOpenResult(
                    false,
                    null,
                    bindingResult.ErrorMessage ?? $"BindingUpdateFailed:{bindingResult.Status}");
            }

            await _conversationWorkspace
                .ApplySessionInfoSnapshotAsync(
                    localConversationId,
                    new ConversationSessionInfoSnapshot
                    {
                        Title = request.RemoteSessionTitle,
                        HasTitle = true,
                        Cwd = request.RemoteSessionCwd,
                        AdditionalDirectories = request.RemoteSessionAdditionalDirectories is null
                            ? null
                            : new List<string>(request.RemoteSessionAdditionalDirectories)
                    },
                    allowRegisterWhenMissing: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // The conversation must survive restart on its own, so the cwd has to outlive the
            // runtime session too: register it as a remote-directory project. Best-effort — a
            // registration failure must not turn an otherwise successful import into an error.
            try
            {
                var registeredDirectoryId = _remoteDirectoryRegistrar.EnsureRegistered(request.RemoteSessionCwd);
                if (string.IsNullOrEmpty(registeredDirectoryId))
                {
                    Logger.LogWarning(
                        "Skipped remote directory registration for imported session. remoteSessionId={RemoteSessionId} remoteCwd={RemoteCwd}",
                        request.RemoteSessionId,
                        request.RemoteSessionCwd);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    ex,
                    "Failed to register remote directory for imported session. remoteSessionId={RemoteSessionId} remoteCwd={RemoteCwd}",
                    request.RemoteSessionId,
                    request.RemoteSessionCwd);
            }

            return new DiscoverRemoteSessionOpenResult(true, localConversationId, null);
        }
        catch (OperationCanceledException)
        {
            await RollBackDiscoveredConversationAsync(localConversationId, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await RollBackDiscoveredConversationAsync(localConversationId, CancellationToken.None).ConfigureAwait(false);
            Logger.LogError(ex, "Failed to open discovered remote session. remoteSessionId={RemoteSessionId}", request.RemoteSessionId);
            return new DiscoverRemoteSessionOpenResult(false, null, ex.Message);
        }
    }

    Task IConversationSessionSwitcher.DiscardDiscoveredRemoteSessionAsync(
        string localConversationId,
        CancellationToken cancellationToken)
        => RollBackDiscoveredConversationAsync(localConversationId, cancellationToken);

    private async Task RollBackDiscoveredConversationAsync(
        string localConversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(localConversationId))
        {
            return;
        }

        var result = await _conversationCatalogFacade
            .DeleteConversationAsync(localConversationId, cancellationToken, CurrentSessionId)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            Logger.LogWarning(
                "Failed to rollback discovered remote session. conversationId={ConversationId} reason={Reason}",
                localConversationId,
                result.FailureReason ?? "Unknown");
        }
    }

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
        var failureContext = CaptureFailurePublicationContext(
            sessionId,
            context.ActivationVersion,
            operationOwner: sessionId);
        var activationStartState = await _chatStore.GetCurrentStateAsync();
        var hasCompetingNonWarmActivation =
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
        var initialWarmReuseConnection = await ResolveWarmReuseConnectionIdentityAsync(initialWarmReuseBinding, context.CancellationToken).ConfigureAwait(false);
        var initialHasReusableProjection = HasReusableWarmProjection(activationStartState, sessionId);
        var initialWarmReuseDecision = ConversationWarmReusePolicy.EvaluateRemoteWarmConversation(
            warmRuntimeSnapshot,
            initialWarmReuseBinding,
            initialWarmReuseConnection,
            initialHasReusableProjection);
        var canOptimisticallyReuseWarmRemoteConversation =
            !hasCompetingNonWarmActivation
            && initialWarmReuseDecision.CanReuse;

        ClearSessionSwitchPreview(sessionId);
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
        await ClearNonAuthoritativeRemoteProjectionBeforeSelectionAsync(
                sessionId,
                activationHydrationMode,
                warmRuntimeSnapshot,
                context.CancellationToken)
            .ConfigureAwait(false);
        _chatUiProjectionApplicationCoordinator.ArmActivationSelectionProjection(
            sessionId,
            context.ActivationVersion);
        var activationResult = activationHydrationMode == ConversationActivationHydrationMode.WorkspaceSnapshot
            ? await _conversationActivationCoordinator
                .ActivateSessionAsync(sessionId, context.CancellationToken)
                .ConfigureAwait(false)
            : await _conversationActivationCoordinator
                .ActivateSessionAsync(sessionId, activationHydrationMode, context.CancellationToken)
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

        if (!await CommitActivatedConversationStateAsync(sessionId, context.CancellationToken).ConfigureAwait(false))
        {
            await SetConversationRuntimeStateAsync(
                    sessionId,
                    ConversationRuntimePhase.Faulted,
                    reason: "WorkspaceActivationCommitRejected",
                    context.CancellationToken)
                .ConfigureAwait(false);
            return ConversationActivationOrchestratorResult.Failed();
        }

        if (IsActivationContextStale(context.ActivationVersion, context.CancellationToken))
        {
            return ConversationActivationOrchestratorResult.Superseded();
        }

        // Warm short-circuit reuses store/workspace projection and skips session/load. If a
        // prior SelectionOnly clear left the store empty while the RuntimeProjection snapshot
        // still holds the authoritative transcript, materialize that snapshot before the warm
        // decision and UI projection so A->B->A never lands on a blank chat.
        await MaterializeWarmReusableProjectionFromWorkspaceIfNeededAsync(
                sessionId,
                context.CancellationToken)
            .ConfigureAwait(false);

        var warmReuseBinding = await ResolveConversationBindingAsync(sessionId, context.CancellationToken).ConfigureAwait(false);
        var warmReuseConnection = await ResolveWarmReuseConnectionIdentityAsync(warmReuseBinding, context.CancellationToken).ConfigureAwait(false);
        var warmReuseState = await _chatStore.GetCurrentStateAsync();
        var hasReusableWarmProjection = HasReusableWarmProjection(warmReuseState, sessionId);
        var warmRuntimeAfterSelection = ResolveWarmReuseRuntimeState(
            warmRuntimeSnapshot,
            warmReuseState.ResolveRuntimeState(sessionId));
        var warmReuseDecisionAfterSelection = ConversationWarmReusePolicy.EvaluateRemoteWarmConversation(
            warmRuntimeAfterSelection,
            warmReuseBinding,
            warmReuseConnection,
            hasReusableWarmProjection);
        var canReuseWarmConversationAfterSelection = warmReuseDecisionAfterSelection.CanReuse;

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
        await EnsureCurrentSessionIdAlignedAsync(sessionId, context.ActivationVersion).ConfigureAwait(false);
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
                    reason: ConversationRuntimeReasons.WarmReuse,
                    context.CancellationToken,
                    connectionInstanceId: warmReuseConnection.ConnectionInstanceId)
                .ConfigureAwait(false);
            return ConversationActivationOrchestratorResult.Success(usedWarmReuse: true);
        }

        if (activationHydrationMode == ConversationActivationHydrationMode.WorkspaceSnapshot)
        {
            await DismissSessionSwitchOverlayAsync(context.ActivationVersion, sessionId).ConfigureAwait(false);
        }

        context.ReleaseForegroundGate();
        var backgroundToken = _disposeCts.Token;
        if (!request.AwaitRemoteHydration)
        {
            _ = ContinueConversationActivationAsync(
                request,
                context,
                failureContext,
                backgroundToken,
                warmRuntimeSnapshot,
                allowWarmReuseShortCircuit: !hasCompetingNonWarmActivation);
            return ConversationActivationOrchestratorResult.BackgroundOwnedSuccess();
        }

        var remoteActivationSucceeded = await CompleteConversationRemoteActivationAsync(
                failureContext,
                backgroundToken,
                warmRuntimeSnapshot,
                allowWarmReuseShortCircuit: !hasCompetingNonWarmActivation)
            .ConfigureAwait(false);
        return remoteActivationSucceeded
            ? ConversationActivationOrchestratorResult.Success()
            : ConversationActivationOrchestratorResult.Failed();
    }

    private async Task ClearNonAuthoritativeRemoteProjectionBeforeSelectionAsync(
        string sessionId,
        ConversationActivationHydrationMode activationHydrationMode,
        ConversationRuntimeSlice? warmRuntimeSnapshot,
        CancellationToken cancellationToken)
    {
        if (activationHydrationMode != ConversationActivationHydrationMode.SelectionOnly)
        {
            return;
        }

        var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        var binding = await ResolveConversationBindingAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(binding?.RemoteSessionId))
        {
            return;
        }

        var currentConnection = await ResolveWarmReuseConnectionIdentityAsync(
                binding,
                cancellationToken)
            .ConfigureAwait(false);
        var runtimeState = ResolveWarmReuseRuntimeState(
            warmRuntimeSnapshot,
            state.ResolveRuntimeState(sessionId));
        var warmReuseDecision = ConversationWarmReusePolicy.EvaluateRemoteWarmConversation(
            runtimeState,
            binding,
            currentConnection,
            HasReusableWarmProjection(state, sessionId));
        if (warmReuseDecision.CanReuse)
        {
            return;
        }

        await ResetConversationProjectionForResyncAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task ContinueConversationActivationAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        ConversationFailurePublicationContext failureContext,
        CancellationToken backgroundToken,
        ConversationRuntimeSlice? warmRuntimeSnapshot,
        bool allowWarmReuseShortCircuit = true)
    {
        await Task.Yield();
        ConversationActivationOrchestratorResult result;
        try
        {
            var remoteActivationSucceeded = await CompleteConversationRemoteActivationAsync(
                    failureContext,
                    backgroundToken,
                    warmRuntimeSnapshot,
                    allowWarmReuseShortCircuit)
                .ConfigureAwait(false);
            result = remoteActivationSucceeded
                ? ConversationActivationOrchestratorResult.Success()
                : ConversationActivationOrchestratorResult.Failed();
        }
        catch (OperationCanceledException) when (backgroundToken.IsCancellationRequested)
        {
            result = ConversationActivationOrchestratorResult.Superseded();
        }
        catch (Exception ex)
        {
            await HandleConversationActivationExceptionAsync(
                    failureContext,
                    ex)
                .ConfigureAwait(false);
            result = ConversationActivationOrchestratorResult.Failed();
        }

        try
        {
            await _conversationActivationOrchestrator
                .CompleteDeferredActivationAsync(
                    request,
                    context,
                    this,
                    result,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (_disposed || _disposeCts.IsCancellationRequested)
        {
        }
    }

    private static ConversationRuntimeSlice? ResolveWarmReuseRuntimeState(
        ConversationRuntimeSlice? preSelectionSnapshot,
        ConversationRuntimeSlice? currentRuntimeState)
    {
        if (ConversationWarmReusePolicy.HasAuthoritativeWarmRuntime(currentRuntimeState))
        {
            return currentRuntimeState;
        }

        return preSelectionSnapshot ?? currentRuntimeState;
    }

    private static bool CanAttemptWarmReuseShortCircuit(
        bool allowWarmReuseShortCircuit,
        ConversationRuntimeSlice? runtimeState)
        => allowWarmReuseShortCircuit
            || ConversationWarmReusePolicy.HasAuthoritativeWarmRuntime(runtimeState);

    public async Task OnActivationCompletedAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        ConversationActivationOrchestratorResult result,
        CancellationToken cancellationToken = default)
    {
        if (result.Succeeded && !result.WasSuperseded)
        {
            if (result.UsedWarmReuse)
            {
                var publication = CaptureFailurePublicationContext(
                    request.ConversationId, context.ActivationVersion, request.ConversationId);
                await PublishConversationActivationPhaseAsync(
                    publication, SessionActivationPhase.Hydrated, ConversationRuntimeReasons.WarmReuse).ConfigureAwait(false);
            }
            await EnsureCurrentSessionIdAlignedAsync(
                    request.ConversationId,
                    context.ActivationVersion)
                .ConfigureAwait(false);
            await ApplyConversationListProjectionAsync().ConfigureAwait(false);
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

    internal void PrimeSessionSwitchPreview(string conversationId)
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

        _uiDispatcher.Enqueue(() =>
        {
            ApplySessionSwitchPreview(conversationId);
        });
    }

    internal void ClearSessionSwitchPreview(string conversationId)
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

        _uiDispatcher.Enqueue(() =>
        {
            ApplySessionSwitchPreviewClear(conversationId);
        });
    }

    private void ProcessElicitationRequest(AcpSessionEventSource source, ElicitationRequestEventArgs request)
    {
        var requestingAgent = source.Service.AgentInfo;
        _ = ProcessElicitationRequestAsync(source, request,
            requestingAgent?.Title ?? requestingAgent?.Name ?? CurrentAgentDisplayText);
    }

    private async Task ProcessElicitationRequestAsync(
        AcpSessionEventSource source, ElicitationRequestEventArgs request, string agentName)
    {
        try
        {
            ConversationBindingSlice? binding = null;
            var projection = await _interactionEventBridge.BuildElicitationRequestAsync(
                request,
                (conversationId, request) => PostToUiAsync(() => RemovePendingElicitationRequestState(conversationId, request)),
                Logger, PostToUiAsync, agentName,
                unshown => CancelUndisplayedElicitationAsync(source, unshown), source,
                async remoteSessionId =>
                {
                    var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
                    var conversationId = _authoritativeRemoteSessionRouter.ResolveConversationId(state, remoteSessionId, source);
                    binding = state.ResolveBinding(conversationId);
                    return conversationId;
                }).ConfigureAwait(false);
            if (projection is null)
            {
                return;
            }

            var displayed = false;
            await PostToUiAsync(() =>
            {
                // Routing and dispatcher work may finish after the foreground owner was replaced.
                // A stale form must not occupy the new connection's only interaction slot.
                if (binding is null || !request.State.CanRespond || !IsInteractionBindingCurrent(source, binding))
                {
                    return;
                }

                AttachElicitationOwnership(projection.Value.ViewModel, source, binding, request);
                displayed = _panelStateCoordinator.TryStoreElicitationRequest(projection.Value.ConversationId, projection.Value.ViewModel);
                PendingElicitationRequest = _panelStateCoordinator.GetPendingElicitationRequest(CurrentSessionId);
            }).ConfigureAwait(true);
            if (!displayed)
            {
                projection.Value.ViewModel.Dispose();
                // This surface holds one form per conversation. Keep the visible request and cancel
                // the unshown one instead of replacing an interaction the user still needs to answer.
                await CancelUndisplayedElicitationAsync(source, request).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            await CancelUndisplayedElicitationAsync(source, request).ConfigureAwait(false);
        }
    }

    private async Task CancelUndisplayedElicitationAsync(
        AcpSessionEventSource source, ElicitationRequestEventArgs request)
    {
        if (await ChatInteractionEventBridge.CancelUndisplayedElicitationAsync(request, Logger).ConfigureAwait(false)) return;

        await PostToUiAsync(async () =>
        {
            if (_disposed || !IsCurrentEventSource(source) || !request.State.CanCancel) return;

            // This surface has no request-scoped retry. Release only the service captured when
            // subscribing; forwarded senders and the current service may identify a different owner.
            await _acpConnectionCommands.DisconnectAfterInteractionFailureAsync(source.Service, this,
                ResolveLocalizerText("Elicitation_CancellationFailedDisconnected",
                    "Could not cancel a request that cannot be displayed. The connection was closed. Reconnect to the agent."))
                .ConfigureAwait(true);
        }).ConfigureAwait(false);
    }

    private void RemovePendingElicitationRequestState(string conversationId, ElicitationRequestViewModel request)
    {
        if (!_panelStateCoordinator.RemoveElicitationRequest(conversationId, request))
        {
            return;
        }

        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            PendingElicitationRequest = _panelStateCoordinator.GetPendingElicitationRequest(conversationId);
        }
    }

    private void OnAskUserRequestReceived(AcpSessionEventSource source, AskUserRequestEventArgs e)
    {
        if (IsCurrentEventSource(source)) _ = ProcessAskUserRequestAsync(source, e);
    }

    private async Task ProcessAskUserRequestAsync(AcpSessionEventSource source, AskUserRequestEventArgs e)
    {
        try
        {
            AskUserRequestViewModel? ownedRequest = null;
            var projection = await _interactionEventBridge.BuildAskUserRequestAsync(
                e,
                conversationId => PostToUiAsync(() => RemovePendingAskUserRequestState(conversationId, ownedRequest)),
                Logger, source).ConfigureAwait(false);
            if (projection is null)
            {
                return;
            }

            await PostToUiAsync(() =>
            {
                if (!IsCurrentEventSource(source)) return;
                ownedRequest = projection.Value.ViewModel;
                _panelStateCoordinator.StoreAskUserRequest(projection.Value.ConversationId, projection.Value.ViewModel);
                PendingAskUserRequest = _panelStateCoordinator.GetPendingAskUserRequest(CurrentSessionId);
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing ask-user request");
        }
    }

    private void RemovePendingAskUserRequestState(string conversationId, AskUserRequestViewModel? expectedRequest = null)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        _panelStateCoordinator.RemoveAskUserRequest(conversationId, expectedRequest);
        if (string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            PendingAskUserRequest = _panelStateCoordinator.GetPendingAskUserRequest(conversationId);
        }
    }

    private void ProcessPermissionRequest(AcpSessionEventSource source, PermissionRequestEventArgs request)
    {
        _ = ProcessPermissionRequestAsync(source, request);
    }

    private async Task ProcessPermissionRequestAsync(AcpSessionEventSource source, PermissionRequestEventArgs request)
    {
        try
        {
            var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
            var conversationId = _authoritativeRemoteSessionRouter.ResolveConversationId(state, request.SessionId, source);
            var binding = state.ResolveBinding(conversationId);
            await PostToUiAsync(async () =>
            {
                if (!IsCurrentEventSource(source) || !request.CanRespond)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(conversationId) || binding is null)
                {
                    await CancelUndisplayedPermissionAsync(source, request).ConfigureAwait(true);
                    return;
                }

                var currentState = await _chatStore.GetCurrentStateAsync().ConfigureAwait(true);
                if (!IsCurrentEventSource(source) || !request.CanRespond)
                {
                    return;
                }

                var viewModel = CreateOwnedPermissionRequest(source, request, conversationId, binding);
                _panelStateCoordinator.StorePermissionRequest(conversationId, viewModel);
                SubscribePermissionRequestChanges(source, request, conversationId, viewModel);
                RefreshPermissionRequestProjection(source, request, conversationId, viewModel);
                if (conversationId is not null) _panelStateCoordinator.NotifyPermissionRequestChanged(conversationId, viewModel);
                ReconcilePermissionBindings(currentState.Bindings);
                SyncPermissionRequestProjection();
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Logger.LogError("Error processing permission request. ExceptionType={ExceptionType}", error.GetType().FullName);
            if (IsCurrentEventSource(source))
            {
                await CancelUndisplayedPermissionAsync(source, request).ConfigureAwait(false);
            }
        }
    }

    private PermissionRequestViewModel CreateOwnedPermissionRequest(
        AcpSessionEventSource source, PermissionRequestEventArgs request,
        string? conversationId, ConversationBindingSlice? binding)
    {
        PermissionRequestViewModel? viewModel = null;
        viewModel = _interactionEventBridge.CreatePermissionRequestViewModel(
            request,
            (_, outcome, optionId) =>
            {
                var bindingCurrent = conversationId is not null && binding is not null
                    && IsInteractionBindingCurrent(source, binding);
                if (!IsCurrentEventSource(source) || !request.CanRespond)
                {
                    return Task.FromResult(false);
                }

                // A binding change withdraws the old choice, but the original request still
                // requires an answer. Its callback keeps that cancellation on its own client.
                var canSelect = bindingCurrent && viewModel?.IsCancellationOnly != true;
                return request.TryRespondAsync(canSelect ? outcome : "cancelled", canSelect ? optionId : null);
            },
            () => PostToUiAsync(() => RemovePermissionRequestProjection(conversationId, viewModel!)));
        viewModel.ToolCallId = TryResolvePermissionToolCallId(request.ToolCall);
        viewModel.RequestTitle = request.Title;
        viewModel.Title = string.IsNullOrWhiteSpace(viewModel.RequestTitle)
            ? ResolveLocalizerText("Permission_DefaultTitle", "Permission required") : viewModel.RequestTitle;
        viewModel.Binding = binding;
        viewModel.Source = source;
        viewModel.Description = request.Description ?? string.Empty;
        viewModel.IsRequestAvailable = () => IsCurrentEventSource(source) && request.CanRespond;
        viewModel.IsBindingCurrent = () => binding is not null && IsInteractionBindingCurrent(source, binding);
        viewModel.IsResponsePrepared = () => request.IsResponsePrepared;
        viewModel.IsResponseSending = () => request.IsResponseSending;
        viewModel.IsRequestCancellationRequested = () => request.IsCancellationRequested;
        return viewModel;
    }

    private void SubscribePermissionRequestChanges(
        AcpSessionEventSource source, PermissionRequestEventArgs request,
        string? conversationId, PermissionRequestViewModel viewModel)
    {
        EventHandler changed = (_, _) => _ = ProcessPermissionRequestChangedAsync(
            source, request, conversationId, viewModel);
        request.Changed += changed;
        viewModel.UnsubscribeRequestChanges = () => request.Changed -= changed;
    }

    private async Task ProcessPermissionRequestChangedAsync(
        AcpSessionEventSource source, PermissionRequestEventArgs request,
        string? conversationId, PermissionRequestViewModel viewModel)
    {
        try
        {
            await PostToUiAsync(async () =>
            {
                if (_disposed || !_panelStateCoordinator.ContainsPermissionRequest(conversationId, viewModel)) return;
                var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(true);
                // A captured SDK callback can outlive unsubscription, a binding change or replacement.
                // Only the exact projection still held by this conversation may change its surface.
                if (_disposed || !_panelStateCoordinator.ContainsPermissionRequest(conversationId, viewModel)) return;
                RefreshPermissionRequestProjection(source, request, conversationId, viewModel);
                ReconcilePermissionBindings(state.Bindings);
                SyncPermissionRequestProjection();
                if (conversationId is not null) _panelStateCoordinator.NotifyPermissionRequestChanged(conversationId, viewModel);
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Logger.LogWarning("Could not refresh a permission request. ExceptionType={ExceptionType}", error.GetType().FullName);
        }
    }

    private void RefreshPermissionRequestProjection(
        AcpSessionEventSource source, PermissionRequestEventArgs request,
        string? conversationId, PermissionRequestViewModel viewModel)
    {
        if (!IsCurrentEventSource(source) || !request.CanRespond)
        {
            RemovePermissionRequestProjection(conversationId, viewModel);
        }
        else if (request.IsCancellationRequested && !request.IsResponseSending && !viewModel.BindingCancellationAttempted)
        {
            viewModel.ShowCancellationRetry(
                ResolveLocalizerText("Permission_RetryCancellation", "Retry cancellation"),
                ResolveLocalizerText("Permission_Cancelled",
                    "This request was cancelled. Retry cancellation to dismiss it."), bindingChanged: false);
        }
    }

    private void ReconcilePermissionBindings(IImmutableDictionary<string, ConversationBindingSlice>? bindings)
    {
        foreach (var (conversationId, request) in _panelStateCoordinator.GetObsoletePermissionRequests(bindings))
        {
            if (!request.BindingCancellationAttempted && request.BindingCancellationTask is not { IsCompleted: false })
            {
                request.BindingCancellationTask = CancelObsoletePermissionAsync(conversationId, request);
            }
        }
    }

    private async Task CancelObsoletePermissionAsync(string conversationId, PermissionRequestViewModel request)
    {
        try
        {
            if (request.Binding is null || !request.IsAvailable || request.OnRespond is null
                || request.IsBindingCurrent?.Invoke() == true)
            {
                return;
            }
            // Once the store withdraws this permission, returning to the old binding must not
            // resurrect consent. The in-flight physical write may still be the single reply.
            request.ShowCancellationRetry(
                ResolveLocalizerText("Permission_RetryCancellation", "Retry cancellation"),
                ResolveLocalizerText("Permission_BindingChanged",
                    "This request is no longer valid because the conversation changed. Retry cancellation to dismiss it."), bindingChanged: true);
            SyncPermissionRequestProjection();
            if (request.IsAvailable)
            {
                // Latch cancellation on the original SDK owner immediately. An in-flight write
                // still owns its terminal response; waiting here first can strand a prepared batch.
                await request.OnRespond("cancelled", null).ConfigureAwait(true);
            }
        }
        catch (Exception error)
        {
            Logger.LogWarning("Could not cancel an obsolete permission request. ExceptionType={ExceptionType}", error.GetType().FullName);
        }
    }

    private async Task CancelUndisplayedPermissionAsync(
        AcpSessionEventSource source, PermissionRequestEventArgs request)
    {
        try
        {
            if (await request.TryRespondAsync("cancelled").ConfigureAwait(false)) return;
        }
        catch (Exception error)
        {
            Logger.LogWarning("Could not cancel an undisplayed permission request. ExceptionType={ExceptionType}", error.GetType().FullName);
        }

        await PostToUiAsync(async () =>
        {
            if (!IsCurrentEventSource(source) || !request.CanRespond) return;
            if (string.IsNullOrWhiteSpace(CurrentSessionId) || !IsChatShellVisibleForRemoteUi)
            {
                await _acpConnectionCommands.DisconnectAfterInteractionFailureAsync(source.Service, this,
                    ResolveLocalizerText("Permission_CancellationFailedDisconnected",
                        "Could not cancel an undisplayed permission request. The connection was closed. Reconnect to the agent."))
                    .ConfigureAwait(true);
                return;
            }
            // No conversation owns this request, so retain only its cancellation on the existing
            // interaction owner. A later selection or reused id cannot turn it into permission.
            var viewModel = CreateOwnedPermissionRequest(source, request, null, null);
            viewModel.ShowCancellationRetry(
                ResolveLocalizerText("Permission_RetryCancellation", "Retry cancellation"),
                ResolveLocalizerText("Permission_Cancelled",
                    "This request was cancelled. Retry cancellation to dismiss it."), bindingChanged: false);
            _panelStateCoordinator.StoreUnboundPermissionCancellation(viewModel);
            SubscribePermissionRequestChanges(source, request, null, viewModel);
            RefreshPermissionRequestProjection(source, request, null, viewModel);
            SyncPermissionRequestProjection();
        }).ConfigureAwait(false);
    }

    private void RemovePermissionRequestProjection(string? conversationId, PermissionRequestViewModel request)
    {
        var removed = conversationId is null
            ? _panelStateCoordinator.RemoveUnboundPermissionCancellation(request)
            : _panelStateCoordinator.RemovePermissionRequest(conversationId, request);
        if (removed)
        {
            SyncPermissionRequestProjection();
        }
    }

    private void SyncPermissionRequestProjection()
    {
        var conversationRequest = _panelStateCoordinator.GetPendingPermissionRequest(
            CurrentSessionId, toolCallId: null, PendingPermissionRequest);
        var unboundCancellation = _panelStateCoordinator.GetUnboundPermissionCancellation(PendingPermissionRequest);
        // A bound answer and an unbound cancellation can share the same physical batch write.
        // Keep the current member while either authoritative collection still selects that sender.
        if (PendingPermissionRequest is not { IsSendingResponse: true } current
            || (!ReferenceEquals(current, conversationRequest) && !ReferenceEquals(current, unboundCancellation)))
        {
            PendingPermissionRequest = unboundCancellation is { IsSendingResponse: true }
                ? unboundCancellation : conversationRequest ?? unboundCancellation;
        }
        ShowPermissionDialog = PendingPermissionRequest is not null;
        StandalonePermissionRequest = PendingPermissionRequest is { } pending
            && (pending.ToolCallId is null || !string.Equals(_visibleTranscriptConversationId, CurrentSessionId, StringComparison.Ordinal)
                || !MessageHistory.ContainsToolCall(pending.ToolCallId))
            ? pending : null;
        MessageHistory.UpdateCachedItems(message =>
        {
            // CurrentSessionId changes before the transcript is replaced. Do not briefly attach
            // another conversation's permission to an old card that happens to share its tool id.
            if (string.Equals(_visibleTranscriptConversationId, CurrentSessionId, StringComparison.Ordinal))
            {
                ApplyPendingInlinePermissionProjection(message);
            }
            else
            {
                message.PendingPermissionRequest = null;
            }
        });
    }

    private void ClearPermissionRequests()
    {
        _panelStateCoordinator.ClearPermissionRequests();
        SyncPermissionRequestProjection();
    }

    private static string? TryResolvePermissionToolCallId(object? toolCall)
    {
        if (toolCall is null)
        {
            return null;
        }

        if (toolCall is JsonElement element)
        {
            return TryGetToolCallId(element);
        }

        if (toolCall is ToolCallUpdate update)
        {
            return string.IsNullOrWhiteSpace(update.ToolCallId) ? null : update.ToolCallId;
        }

        return null;
    }

    private static string? TryGetToolCallId(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return element.TryGetProperty("toolCallId", out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private void OnFileSystemRequestReceived(AcpSessionEventSource source, FileSystemRequestEventArgs e)
    {
        _uiDispatcher.Enqueue(() =>
        {
            try
            {
                PendingFileSystemRequest = _interactionEventBridge.CreateFileSystemRequestViewModel(
                    e,
                    async (messageId, success, content, message) =>
                    {
                        if (IsCurrentEventSource(source))
                        {
                            await source.Service.RespondToFileSystemRequestAsync(messageId, success, content, message).ConfigureAwait(true);
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

    private void OnTerminalRequestReceived(AcpSessionEventSource source, TerminalRequestEventArgs e)
    {
        _uiDispatcher.Enqueue(() =>
        {
            try
            {
                Logger.LogInformation("Terminal request received: Method={Method}, TerminalId={TerminalId}", e.Method, e.TerminalId);
                if (IsCurrentEventSource(source)) _ = ProcessTerminalRequestAsync(source, e);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing terminal request");
            }
        });
    }

    private void OnTerminalStateChangedReceived(AcpSessionEventSource source, TerminalStateChangedEventArgs e)
    {
        _uiDispatcher.Enqueue(() =>
        {
            try
            {
                if (IsCurrentEventSource(source)) _ = ProcessTerminalStateChangedAsync(source, e);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error processing terminal state update");
            }
        });
    }

    private async Task ProcessTerminalRequestAsync(AcpSessionEventSource source, TerminalRequestEventArgs request)
    {
        try
        {
            var projection = await _interactionEventBridge.BuildTerminalRequestSelectionAsync(
                request,
                _panelStateCoordinator,
                CurrentSessionId,
                Logger, source).ConfigureAwait(false);
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

    private async Task ProcessTerminalStateChangedAsync(AcpSessionEventSource source, TerminalStateChangedEventArgs update)
    {
        try
        {
            var projection = await _interactionEventBridge.BuildTerminalStateSelectionAsync(
                update,
                _panelStateCoordinator,
                CurrentSessionId,
                Logger, source).ConfigureAwait(false);
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

    private void OnErrorOccurred(AcpSessionEventSource source, string error)
    {
        if (_disposed) return;
        var conversationOwner = ReferenceEquals(source.Service, _chatService) ? CurrentSessionId : null;
        if (!source.Service.IsConnected)
        {
            if (_connectionSessionRegistry?.RemoveByService(source.Service, out _, AcpConnectionRetirementReason.TransportLost) != true)
            {
                TrackPendingSessionUpdate(_sessionUpdateWorkQueue.Enqueue(
                    () => RetireConnectionStateAsync(source, AcpConnectionRetirementReason.TransportLost)));
            }
        }
        else if (!IsCurrentEventSource(source)) return;

        _uiDispatcher.Enqueue(() =>
        {
            Logger.LogError("ACP connection reported an error. ProfileId={ProfileId} Error={Error}", source.ProfileId, error);
            if (ReferenceEquals(source.Service, _chatService))
            {
                PublishConversationOperationFailure(conversationOwner, error);
                if (string.Equals(CurrentSessionId, conversationOwner, StringComparison.Ordinal)) QueueActiveRemoteConnectionRecovery(error);
            }
        });
        if (ReferenceEquals(source.Service, _chatService)) _ = ReconcileConnectionProjectionOnTransportLossAsync(error);
    }

    private async Task ProcessRetiredConnectionAsync(AcpSessionEventSource source, AcpConnectionRetirementReason reason)
    {
        if (_conversationAttentionStore is not null)
        {
            await _conversationAttentionStore.Dispatch(new DetachConversationAttentionContentAction(
                source.ProfileId, source.ConnectionInstanceId)).ConfigureAwait(false);
        }
        var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        foreach (var turn in state.Turns?.Values ?? Enumerable.Empty<ActiveTurnState>())
        {
            if (!string.Equals(turn.ProfileId, source.ProfileId, StringComparison.Ordinal)
                || !string.Equals(turn.ConnectionInstanceId, source.ConnectionInstanceId, StringComparison.Ordinal)
                || turn.Phase is ChatTurnPhase.Completed or ChatTurnPhase.Cancelled or ChatTurnPhase.Failed
                || reason == AcpConnectionRetirementReason.Replaced && IsOperationReplacingConnection(source, turn.ConversationId)) continue;

            ChatAction action = reason is AcpConnectionRetirementReason.Disconnected or AcpConnectionRetirementReason.Shutdown
                ? new CancelTurnAction(turn.ConversationId, turn.TurnId, ConnectionInstanceId: turn.ConnectionInstanceId)
                : new FailTurnAction(turn.ConversationId, turn.TurnId,
                    Localize("ChatOperation_ConnectionInterrupted", "The connection was interrupted. Reconnect to continue."),
                    ConnectionInstanceId: turn.ConnectionInstanceId);
            await _chatStore.Dispatch(action).ConfigureAwait(false);
        }

        // Cancellation completes the prompt's catch path, so commit the retirement reason first;
        // otherwise an unexpected disconnect can win the terminal-state race as a user cancellation.
        CancelPromptOperationsForSource(source, reason);
    }

    // When the underlying transport genuinely dies, the connection projection can otherwise keep
    // showing a healthy "Connected" state while the transcript surface errors ("connected up top,
    // not-ready down below"). If the service now reports disconnected but the projection still
    // believes it is live, reconcile it. We only correct a Connected projection so this never
    // clobbers an in-flight Connecting/Initializing handshake, and never double-publishes over an
    // already Disconnected/Error state (the WebSocket recovery path publishes its own disconnect).
    // Transient send conflicts no longer flip the service's IsConnected, so this fires only on a
    // real disconnect.
    private async Task ReconcileConnectionProjectionOnTransportLossAsync(string error)
    {
        if (_chatService is not { IsConnected: false })
        {
            return;
        }

        var connectionState = await _chatConnectionStore.GetCurrentStateAsync().ConfigureAwait(false);
        if (connectionState.Phase == ConnectionPhase.Connected)
        {
            await PublishDisconnectedConnectionStateAsync(error).ConfigureAwait(false);
        }
    }

    private Task<bool> TryAuthenticateAsync(CancellationToken cancellationToken, Func<bool>? isCurrent = null)
        => _authenticationCoordinator.TryAuthenticateAsync(
            _chatService,
            IsInitialized,
            _acpConnectionCoordinator,
            Logger,
            message => ShowTransientNotificationToast(message),
            cancellationToken,
            requiredFallback: new AuthenticationHintPresentation(
                Localize(
                    "ChatAuth_Required",
                    "The agent requires authentication before it can respond."),
                ResourceKey: "ChatAuth_Required",
                Fallback: "The agent requires authentication before it can respond."),
            formatAuthenticationFailed: detail => new AuthenticationHintPresentation(
                FormatLocalize(
                    "ChatAuth_FailedWithDetail",
                    "Authentication failed: {0}",
                    detail),
                ResourceKey: "ChatAuth_FailedWithDetail",
                Fallback: "Authentication failed: {0}",
                FormatArgs: [detail]),
            unsupportedMethodTypeFallback: new AuthenticationHintPresentation(
                Localize(
                    "ChatAuth_UnsupportedMethodType",
                    "The agent only offers sign-in methods this app cannot run. Sign in with the agent's own command-line tool, then reconnect."),
                ResourceKey: "ChatAuth_UnsupportedMethodType",
                Fallback: "The agent only offers sign-in methods this app cannot run. Sign in with the agent's own command-line tool, then reconnect."),
            terminalAuthenticateAsync: _terminalAuthenticationCoordinator?.CanAuthenticate(_chatService) == true
                ? (method, token) => _terminalAuthenticationCoordinator.TryAuthenticateAsync(
                    method, this, ReconnectAfterTerminalAuthenticationAsync, token)
                : null,
            isCurrent: isCurrent);

    private async Task<bool> ReconnectAfterTerminalAuthenticationAsync(
        AcpConnectionContext connectionContext, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        if (!connectionContext.MatchesExpectedConnection(this)) return false;
        var profile = ResolveNewSessionDraftProfile(connectionContext.ExpectedProfileId);
        if (profile?.Id != connectionContext.ExpectedProfileId) return false;
        var connected = await ConnectToAcpProfileCoreAsync(
            profile, connectionContext, lifetime.Token, updateSelectedProfileIntent: false).ConfigureAwait(false);
        if (!connected) return false;
        if (connectionContext.ConversationId is not { } conversationId) return true;
        if (!string.Equals(conversationId, CurrentSessionId, StringComparison.Ordinal)) return false;

        // A restarted agent process cannot inherit a warm remote session. Existing hydration owns
        // capability gating, transcript replay and the new authoritative connection identity.
        return await EnsureActiveConversationRemoteHydratedAsync(
            conversationId, new ConversationFailurePublicationContext(
                conversationId, ActivationVersion: null, OperationOwner: conversationId, ExpectedShellSnapshotVersion: null), lifetime.Token,
            allowWarmReuseShortCircuit: false).ConfigureAwait(false);
    }

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

        var currentState = await _chatStore.GetCurrentStateAsync();
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

        // ACP session/load replays user_message_chunk with no per-message timestamp; the
        // protocol carries only messageId/content. A first-seen replayed user message has
        // no authoritative time, so CreateContentSnapshot leaves it null. When an existing
        // snapshot is being merged (e.g. optimistic local emit reconciled with replay), its
        // time — if any — is preserved above. No wall clock is ever synthesized here.

        await UpsertTranscriptSnapshotAsync(conversationId, snapshot).ConfigureAwait(true);
    }

    private ConversationMessageSnapshot CreateContentSnapshot(
        ContentBlock content,
        bool isOutgoing,
        string? id = null,
        DateTime? timestamp = null,
        string? protocolMessageId = null)
    {
        // The caller is the single source of truth for time:
        //   - session/load replayed content has no authoritative timestamp (ACP carries none),
        //     so callers pass null and we keep it null rather than inventing a wall clock.
        //   - locally-owned events (e.g. an outgoing user prompt emitted by this client) pass
        //     their own observed time.
        // We never fall back to DateTime.UtcNow here; that would mask "no time" as "now".
        var snapshot = new ConversationMessageSnapshot
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id,
            Timestamp = timestamp,
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
                // Directional templates only render DisplayBodyText until dedicated media
                // templates ship; project a visible plain fallback so Skia rows are never blank.
                snapshot.TextContent = ResolveMediaPlaceholder("image", image.MimeType);
                break;
            case AudioContentBlock audio:
                snapshot.ContentType = "audio";
                snapshot.AudioData = audio.Data ?? string.Empty;
                snapshot.AudioMimeType = audio.MimeType ?? string.Empty;
                snapshot.TextContent = ResolveMediaPlaceholder("audio", audio.MimeType);
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

    private string ResolveMediaPlaceholder(string mediaKind, string? mimeType)
    {
        var isImage = string.Equals(mediaKind, "image", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return isImage
                ? Localize("ChatMedia_ImagePlaceholder", "[image]")
                : Localize("ChatMedia_AudioPlaceholder", "[audio]");
        }

        return isImage
            ? FormatLocalize("ChatMedia_ImagePlaceholderWithMime", "[image: {0}]", mimeType)
            : FormatLocalize("ChatMedia_AudioPlaceholderWithMime", "[audio: {0}]", mimeType);
    }

    private ConversationMessageSnapshot CreateToolCallSnapshot(ToolCallUpdate toolCall)
    {
        // ACP tool_call updates carry no timestamp; the creation instant is not a protocol
        // fact, so the snapshot starts with no message time. Subsequent tool_call_update
        // merges preserve whatever time was already present rather than overwriting it.
        return new ConversationMessageSnapshot
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = null,
            IsOutgoing = false,
            ContentType = "tool_call",
            Title = toolCall.Title ?? string.Empty,
            TextContent = ResolveToolCallOutput(toolCall.RawOutput, toolCall.Content, string.Empty),
            ToolCallId = toolCall.ToolCallId,
            ToolCallKind = ToolCallContentSnapshots.FormatKind(toolCall.Kind),
            ToolCallStatus = ToolCallContentSnapshots.FormatStatus(toolCall.Status),
            ToolCallJson = ResolveToolCallPayload(toolCall.RawInput, toolCall.Content),
            ToolCallRawInputJson = TryGetRawJson(toolCall.RawInput),
            ToolCallRawOutputJson = TryGetRawJson(toolCall.RawOutput),
            ToolCallContent = ToolCallContentSnapshots.ToDomainContent(toolCall.Content),
            ToolCallLocations = ToolCallContentSnapshots.ToDomainLocations(toolCall.Locations)
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

        var state = await _chatStore.GetCurrentStateAsync();
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
                // Preserve the existing time; a status update is not a new message and ACP
                // gives it no timestamp to write over what was already there.
                Timestamp = existing.Timestamp,
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
                ToolCallKind = toolCallStatusUpdate.Kind is null
                    ? existing.ToolCallKind
                    : ToolCallContentSnapshots.FormatKind(toolCallStatusUpdate.Kind),
                ToolCallStatus = toolCallStatusUpdate.Status is null
                    ? existing.ToolCallStatus
                    : ToolCallContentSnapshots.FormatStatus(toolCallStatusUpdate.Status),
                ToolCallJson = ResolveToolCallPayload(toolCallStatusUpdate.RawInput, toolCallStatusUpdate.Content) ?? existing.ToolCallJson,
                ToolCallRawInputJson = TryGetRawJson(toolCallStatusUpdate.RawInput) ?? existing.ToolCallRawInputJson,
                ToolCallRawOutputJson = TryGetRawJson(toolCallStatusUpdate.RawOutput) ?? existing.ToolCallRawOutputJson,
                ToolCallContent = toolCallStatusUpdate.Content is not null
                    ? ToolCallContentSnapshots.ToDomainContent(toolCallStatusUpdate.Content)
                    : existing.ToolCallContent,
                ToolCallLocations = toolCallStatusUpdate.Locations is not null
                    ? ToolCallContentSnapshots.ToDomainLocations(toolCallStatusUpdate.Locations)
                    : existing.ToolCallLocations,
                PlanEntry = ClonePlanEntrySnapshot(existing.PlanEntry),
                ModeId = existing.ModeId
            };

        await _chatStore.Dispatch(new UpsertTranscriptMessageAction(conversationId, merged)).ConfigureAwait(false);
    }

    private ConversationMessageSnapshot CreateToolCallSnapshot(ToolCallStatusUpdate toolCallStatusUpdate)
    {
        // A tool_call_update that arrives with no prior snapshot carries no timestamp.
        return new ConversationMessageSnapshot
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = null,
            IsOutgoing = false,
            ContentType = "tool_call",
            Title = toolCallStatusUpdate.Title ?? string.Empty,
            TextContent = ResolveToolCallOutput(toolCallStatusUpdate.RawOutput, toolCallStatusUpdate.Content, string.Empty),
            ToolCallId = toolCallStatusUpdate.ToolCallId,
            ToolCallKind = ToolCallContentSnapshots.FormatKind(toolCallStatusUpdate.Kind),
            ToolCallStatus = ToolCallContentSnapshots.FormatStatus(toolCallStatusUpdate.Status),
            ToolCallJson = ResolveToolCallPayload(toolCallStatusUpdate.RawInput, toolCallStatusUpdate.Content),
            ToolCallRawInputJson = TryGetRawJson(toolCallStatusUpdate.RawInput),
            ToolCallRawOutputJson = TryGetRawJson(toolCallStatusUpdate.RawOutput),
            ToolCallContent = ToolCallContentSnapshots.ToDomainContent(toolCallStatusUpdate.Content),
            ToolCallLocations = ToolCallContentSnapshots.ToDomainLocations(toolCallStatusUpdate.Locations)
        };
    }

    private static string? TryGetRawJson(System.Text.Json.JsonElement? element)
        => element?.GetRawText();

    private static List<SalmonEgg.Acp.Tool.ToolCallContent>? CloneToolCallContentList(
        IReadOnlyList<SalmonEgg.Acp.Tool.ToolCallContent>? content)
        => ToolCallContentSnapshots.CloneList(content);

    private static List<SalmonEgg.Acp.Tool.ToolCallLocation>? CloneToolCallLocationList(
        IReadOnlyList<SalmonEgg.Acp.Tool.ToolCallLocation>? locations)
        => ToolCallContentSnapshots.CloneLocations(locations);

    private static string? ResolveToolCallPayload(
        System.Text.Json.JsonElement? rawPayload,
        IReadOnlyList<SalmonEgg.Acp.Tool.ToolCallContent>? content)
        => TryGetRawJson(rawPayload)
            ?? ToolCallContentSnapshots.SerializePayload(content);

    private static string ResolveToolCallOutput(
        System.Text.Json.JsonElement? rawOutput,
        IReadOnlyList<SalmonEgg.Acp.Tool.ToolCallContent>? content,
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

    private static string FlattenToolCallContent(IReadOnlyList<SalmonEgg.Acp.Tool.ToolCallContent>? content)
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
                case SalmonEgg.Acp.Tool.ContentToolCallContent { Content: TextContentBlock textBlock } when !string.IsNullOrWhiteSpace(textBlock.Text):
                    parts.Add(textBlock.Text);
                    break;
                case SalmonEgg.Acp.Tool.DiffToolCallContent diff when !string.IsNullOrWhiteSpace(diff.NewText):
                    parts.Add(diff.NewText);
                    break;
                case SalmonEgg.Acp.Tool.DiffToolCallContent diff when !string.IsNullOrWhiteSpace(diff.Path):
                    parts.Add(diff.Path);
                    break;
                case SalmonEgg.Acp.Tool.TerminalToolCallContent terminal when !string.IsNullOrWhiteSpace(terminal.TerminalId):
                    parts.Add(terminal.TerminalId);
                    break;
            }
        }

        return parts.Count == 0 ? string.Empty : string.Join(Environment.NewLine, parts);
    }

    private async Task PreemptivelyCancelTurnAsync(string? expectedConversationId = null, string? expectedTurnId = null,
        string? expectedConnectionInstanceId = null)
    {
        var state = await _chatStore.GetCurrentStateAsync();
        var activeTurn = state.ResolveTurn(expectedConversationId ?? state.HydratedConversationId);
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

        if (expectedTurnId is not null && !string.Equals(activeTurn.ConnectionInstanceId, expectedConnectionInstanceId, StringComparison.Ordinal)) return;

        await PreemptivelyCancelOutstandingToolCallsAsync(state, activeTurn).ConfigureAwait(true);
        await _chatStore.Dispatch(new CancelTurnAction(activeTurn.ConversationId, activeTurn.TurnId,
            StopReason.Cancelled.Value, HasStopReason: true, activeTurn.ConnectionInstanceId)).ConfigureAwait(true);
    }

    private async Task PreemptivelyCancelOutstandingToolCallsAsync()
    {
        var state = await _chatStore.GetCurrentStateAsync();
        var activeTurn = state.ActiveTurn;
        if (activeTurn is null)
        {
            return;
        }

        await PreemptivelyCancelOutstandingToolCallsAsync(state, activeTurn).ConfigureAwait(true);
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
        // Scope to unprocessed tool calls of the active turn by STATE, not by wall clock.
        // This method runs only against the current activeTurn (see PreemptivelyCancelTurnAsync
        // guards), and session/load replay happens outside any active turn, so replayed tool
        // calls are never touched here. Filtering on the authoritative status predicate — and
        // refraining from overwriting the (possibly absent) timestamp — keeps message time from
        // being invented or clobbered at cancellation time.
        var pendingToolCalls = transcript
            .Where(message =>
                string.Equals(message.ContentType, "tool_call", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(message.ToolCallId)
                && (string.IsNullOrWhiteSpace(message.ToolCallStatus)
                    || string.Equals(message.ToolCallStatus, SalmonEgg.Acp.Tool.ToolCallStatus.Pending.ToString(), StringComparison.Ordinal)
                    || string.Equals(message.ToolCallStatus, SalmonEgg.Acp.Tool.ToolCallStatus.InProgress.ToString(), StringComparison.Ordinal)))
            .ToArray();

        foreach (var existing in pendingToolCalls)
        {
            await _chatStore.Dispatch(new UpsertTranscriptMessageAction(activeTurn.ConversationId, new ConversationMessageSnapshot
            {
                Id = existing.Id,
                Timestamp = existing.Timestamp,
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
                ToolCallStatus = SalmonEgg.Acp.Tool.ToolCallStatus.Cancelled.ToString(),
                ToolCallJson = existing.ToolCallJson,
                ToolCallRawInputJson = existing.ToolCallRawInputJson,
                ToolCallRawOutputJson = existing.ToolCallRawOutputJson,
                ToolCallContent = ToolCallContentSnapshots.CloneDomainPayload(existing.ToolCallContent),
                ToolCallLocations = ToolCallContentSnapshots.CloneDomainPayload(existing.ToolCallLocations),
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
        var nextSessionInfo = await ResolveProjectedSessionInfoAsync(conversationId, delta.SessionInfo)
            .ConfigureAwait(true);
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
                delta.ShowPlanPanel ?? true)).ConfigureAwait(true);
        }

        if (nextSessionInfo is not null)
        {
            await PersistProjectedSessionInfoSnapshotAsync(conversationId).ConfigureAwait(true);
        }
    }

    private async Task<ConversationSessionInfoSnapshot?> ResolveProjectedSessionInfoAsync(
        string conversationId,
        AcpSessionInfoSnapshot? sessionInfo)
    {
        var projected = ToConversationSessionInfoSnapshot(sessionInfo);
        if (projected is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(projected.Cwd)
            && projected.AdditionalDirectories is not null)
        {
            return projected;
        }

        var storeState = await _chatStore.GetCurrentStateAsync().ConfigureAwait(true);
        var established = storeState.ResolveSessionStateSlice(conversationId)?.SessionInfo
            ?? TryGetConversationSnapshot(conversationId)?.SessionInfo;
        var establishedCwd = !string.IsNullOrWhiteSpace(projected.Cwd)
            ? projected.Cwd
            : !string.IsNullOrWhiteSpace(established?.Cwd)
                ? established.Cwd
                : ResolveEstablishedSessionManagerCwd(storeState, conversationId);
        var establishedDirectories = projected.AdditionalDirectories
            ?? established?.AdditionalDirectories;
        if (string.Equals(projected.Cwd, establishedCwd, StringComparison.Ordinal)
            && ReferenceEquals(projected.AdditionalDirectories, establishedDirectories))
        {
            return projected;
        }

        return new ConversationSessionInfoSnapshot
        {
            Title = projected.Title,
            HasTitle = projected.HasTitle,
            Cwd = string.IsNullOrWhiteSpace(establishedCwd) ? null : establishedCwd.Trim(),
            AdditionalDirectories = establishedDirectories is null
                ? null
                : new List<string>(establishedDirectories),
            UpdatedAtUtc = projected.UpdatedAtUtc,
            HasUpdatedAt = projected.HasUpdatedAt,
            Meta = projected.Meta is null
                ? null
                : new Dictionary<string, object?>(projected.Meta, StringComparer.Ordinal)
        };
    }

    private string? ResolveEstablishedSessionManagerCwd(ChatState storeState, string conversationId)
    {
        var localCwd = _sessionManager.GetSession(conversationId)?.Cwd;
        if (!string.IsNullOrWhiteSpace(localCwd))
        {
            return localCwd.Trim();
        }

        var remoteSessionId = storeState.ResolveBinding(conversationId)?.RemoteSessionId;
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            return null;
        }

        var remoteCwd = _sessionManager.GetSession(remoteSessionId)?.Cwd;
        return string.IsNullOrWhiteSpace(remoteCwd) ? null : remoteCwd.Trim();
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
            BooleanValue = option.BooleanValue,
            RawProtocolJson = option.RawProtocolJson,
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

        var normalizedCwd = string.IsNullOrWhiteSpace(sessionInfo.Cwd) ? null : sessionInfo.Cwd;
        var normalizedAdditionalDirectories = sessionInfo.AdditionalDirectories is null
            ? null
            : new List<string>(sessionInfo.AdditionalDirectories);
        var normalizedUpdatedAt = string.IsNullOrWhiteSpace(sessionInfo.UpdatedAt) ? null : sessionInfo.UpdatedAt;
        var normalizedMeta = sessionInfo.Meta is { Count: > 0 }
            ? new Dictionary<string, object?>(sessionInfo.Meta, StringComparer.Ordinal)
            : null;
        if (!sessionInfo.HasTitle
            && normalizedCwd is null
            && normalizedAdditionalDirectories is null
            && !sessionInfo.HasUpdatedAt
            && normalizedMeta is null)
        {
            return null;
        }

        return new ConversationSessionInfoSnapshot
        {
            Title = sessionInfo.Title,
            HasTitle = sessionInfo.HasTitle,
            Cwd = normalizedCwd,
            AdditionalDirectories = normalizedAdditionalDirectories,
            UpdatedAtUtc = AcpSessionTimestampPolicy.ParseUpdatedAtUtc(normalizedUpdatedAt),
            HasUpdatedAt = sessionInfo.HasUpdatedAt,
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
        if (_chatService?.PublishesConfigurationResponses == true)
        {
            await WaitForPendingSessionUpdatesAsync().ConfigureAwait(true);
            return;
        }
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
        string remoteSessionId,
        string acceptedModeId)
    {
        if (response is null || string.IsNullOrWhiteSpace(acceptedModeId))
        {
            return;
        }

        await ApplySessionUpdateDeltaAsync(conversationId, _acpSessionUpdateProjector.Project(
            new SessionUpdateEventArgs(
                remoteSessionId,
                new CurrentModeUpdate(acceptedModeId)))).ConfigureAwait(true);
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
