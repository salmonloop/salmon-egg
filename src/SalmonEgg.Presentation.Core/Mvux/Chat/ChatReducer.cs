using System;
using System.Collections.Immutable;
using System.Linq;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public static class ChatReducer
{
    public static ChatState Reduce(ChatState? state, ChatAction action)
    {
        var current = state ?? ChatState.Empty;

        return action switch
        {
            SelectConversationAction selectConversation => Mutate(current, ProjectConversation(current, selectConversation.ConversationId)),
            SetBindingSliceAction setBinding => Mutate(current, current with
            {
                Bindings = UpdateBindings(current.Bindings, setBinding.Binding)
            }),
            SetConversationRuntimeStateAction setRuntimeState when string.IsNullOrWhiteSpace(setRuntimeState.RuntimeState.ConversationId)
                => current,
            SetConversationRuntimeStateAction setRuntimeState => Mutate(current, current with
            {
                RuntimeStates = UpdateRuntimeStates(current.RuntimeStates, setRuntimeState.RuntimeState)
            }),
            ClearConversationRuntimeStateAction clearRuntimeState when string.IsNullOrWhiteSpace(clearRuntimeState.ConversationId)
                => current,
            ClearConversationRuntimeStateAction clearRuntimeState => Mutate(current, current with
            {
                RuntimeStates = (current.RuntimeStates ?? ImmutableDictionary<string, ConversationRuntimeSlice>.Empty)
                    .Remove(clearRuntimeState.ConversationId)
            }),
            ResetConversationRuntimeStatesAction => Mutate(current, current with
            {
                RuntimeStates = ImmutableDictionary<string, ConversationRuntimeSlice>.Empty
            }),
            SetIsHydratingAction setIsHydrating => Mutate(current, current with { IsHydrating = setIsHydrating.IsHydrating }),
            SetDraftTextAction draftText => Mutate(current, current with { DraftText = draftText.Text }),
            SetPromptInFlightAction setPromptInFlight => Mutate(current, current with { IsPromptInFlight = setPromptInFlight.IsInFlight }),
            BeginTurnAction begin => Mutate(current, current with
            {
                ActiveTurn = new ActiveTurnState(begin.ConversationId, begin.TurnId, begin.InitialPhase, DateTime.UtcNow, DateTime.UtcNow)
            }),
            AdvanceTurnPhaseAction advance when MatchesActiveTurn(current.ActiveTurn, advance.ConversationId, advance.TurnId)
                && !IsTerminalPhase(current.ActiveTurn!.Phase) => Mutate(current, current with
            {
                ActiveTurn = current.ActiveTurn with { Phase = advance.NewPhase, ToolCallId = advance.ToolCallId, ToolTitle = advance.ToolTitle, LastUpdatedAtUtc = DateTime.UtcNow }
            }),
            CompleteTurnAction complete when MatchesActiveTurn(current.ActiveTurn, complete.ConversationId, complete.TurnId)
                && !IsTerminalPhase(current.ActiveTurn!.Phase) => Mutate(current, current with
            {
                ActiveTurn = current.ActiveTurn with { Phase = ChatTurnPhase.Completed, LastUpdatedAtUtc = DateTime.UtcNow }
            }),
            FailTurnAction fail when MatchesActiveTurn(current.ActiveTurn, fail.ConversationId, fail.TurnId)
                && !IsTerminalPhase(current.ActiveTurn!.Phase) => Mutate(current, current with
            {
                ActiveTurn = current.ActiveTurn with { Phase = ChatTurnPhase.Failed, FailureMessage = fail.ErrorMessage, LastUpdatedAtUtc = DateTime.UtcNow }
            }),
            CancelTurnAction cancel when MatchesActiveTurn(current.ActiveTurn, cancel.ConversationId, cancel.TurnId)
                && !IsTerminalPhase(current.ActiveTurn!.Phase) => Mutate(current, current with
            {
                ActiveTurn = current.ActiveTurn with { Phase = ChatTurnPhase.Cancelled, LastUpdatedAtUtc = DateTime.UtcNow }
            }),
            ClearTurnAction clear when current.ActiveTurn?.ConversationId == clear.ConversationId => Mutate(current, current with
            {
                ActiveTurn = null
            }),
            AddMessageAction addMessage when !string.IsNullOrWhiteSpace(current.HydratedConversationId) => Mutate(current, current with
            {
                ConversationContents = UpdateConversationContents(
                    current.ConversationContents,
                    current.HydratedConversationId,
                    BuildUpdatedContentSlice(
                        current.ResolveContentSlice(current.HydratedConversationId),
                        transcript: UpsertTranscript(
                            ResolveTranscript(current, current.HydratedConversationId),
                            ToSnapshot(addMessage.Message))))
            }),
            AddMessageAction addMessage => Mutate(current, current with
            {
                Transcript = UpsertTranscript(current.Transcript, ToSnapshot(addMessage.Message))
            }),
            HydrateConversationAction hydrate => Mutate(current, current with
            {
                ConversationContents = UpdateConversationContents(
                    current.ConversationContents,
                    hydrate.ConversationId,
                    new ConversationContentSlice(
                        hydrate.Transcript,
                        hydrate.PlanEntries,
                        hydrate.ShowPlanPanel,
                        hydrate.PlanTitle))
            }),
            ReplacePlanEntriesAction replacePlan => Mutate(current, current with
            {
                ConversationContents = UpdateConversationContents(
                    current.ConversationContents,
                    replacePlan.ConversationId,
                    new ConversationContentSlice(
                        ResolveTranscript(current, replacePlan.ConversationId) ?? ImmutableList<ConversationMessageSnapshot>.Empty,
                        replacePlan.PlanEntries,
                        replacePlan.ShowPlanPanel,
                        replacePlan.PlanTitle))
            }),
            UpsertTranscriptMessageAction upsertMessage => Mutate(current, current with
            {
                ConversationContents = UpdateConversationContents(
                    current.ConversationContents,
                    upsertMessage.ConversationId,
                    BuildUpdatedContentSlice(
                        current.ResolveContentSlice(upsertMessage.ConversationId),
                        transcript: UpsertTranscript(
                            ResolveTranscript(current, upsertMessage.ConversationId),
                            upsertMessage.Message)))
            }),
            SetConversationSessionStateAction setSessionState => Mutate(current, current with
            {
                ConversationSessionStates = UpdateConversationSessionStates(
                    current.ConversationSessionStates,
                    setSessionState.ConversationId,
                    new ConversationSessionStateSlice(
                        setSessionState.AvailableModes,
                        setSessionState.SelectedModeId,
                        setSessionState.ConfigOptions,
                        setSessionState.ShowConfigOptionsPanel))
            }),
            MergeConversationSessionStateAction mergeSessionState => Mutate(current, current with
            {
                ConversationSessionStates = UpdateConversationSessionStates(
                    current.ConversationSessionStates,
                    mergeSessionState.ConversationId,
                    BuildUpdatedSessionStateSlice(
                        current.ResolveSessionStateSlice(mergeSessionState.ConversationId),
                        mergeSessionState.AvailableModes,
                        mergeSessionState.SelectedModeId,
                        mergeSessionState.HasSelectedModeId,
                        mergeSessionState.ConfigOptions,
                        mergeSessionState.ShowConfigOptionsPanel))
            }),
            UpdateMessageAction updateMessage when !string.IsNullOrWhiteSpace(current.HydratedConversationId) => Mutate(current, current with
            {
                ConversationContents = UpdateConversationContents(
                    current.ConversationContents,
                    current.HydratedConversationId,
                    BuildUpdatedContentSlice(
                        current.ResolveContentSlice(current.HydratedConversationId),
                        transcript: UpsertTranscript(
                            ResolveTranscript(current, current.HydratedConversationId),
                            ToSnapshot(updateMessage.Message))))
            }),
            UpdateMessageAction updateMessage => Mutate(current, current with
            {
                Transcript = UpsertTranscript(current.Transcript, ToSnapshot(updateMessage.Message))
            }),
            AppendTextDeltaAction appendDelta => Mutate(current, current with
            {
                ConversationContents = UpdateConversationContents(
                    current.ConversationContents,
                    appendDelta.ConversationId,
                    BuildUpdatedContentSlice(
                        current.ResolveContentSlice(appendDelta.ConversationId),
                        transcript: AppendTranscriptDelta(
                            ResolveTranscript(current, appendDelta.ConversationId),
                            appendDelta.Delta)))
            }),
            SetAgentIdentityAction identity => Mutate(current, current with
            {
                AgentName = identity.AgentName,
                AgentVersion = identity.AgentVersion
            }),
            _ => current
        };
    }

    private static ChatState Mutate(ChatState current, ChatState next)
    {
        if (current == next)
        {
            return current;
        }

        var projected = ProjectActiveConversationState(next);
        return projected with
        {
            Generation = checked(current.Generation + 1)
        };
    }

    private static ChatState ProjectActiveConversationState(ChatState state)
    {
        var conversationId = state.HydratedConversationId;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return state;
        }

        var content = state.ResolveContentSlice(conversationId);
        var sessionState = state.ResolveSessionStateSlice(conversationId);

        return state with
        {
            Transcript = content?.Transcript,
            PlanEntries = content?.PlanEntries,
            ShowPlanPanel = content?.ShowPlanPanel ?? false,
            PlanTitle = content?.PlanTitle,
            AvailableModes = sessionState?.AvailableModes,
            SelectedModeId = sessionState?.SelectedModeId,
            ConfigOptions = sessionState?.ConfigOptions,
            ShowConfigOptionsPanel = sessionState?.ShowConfigOptionsPanel ?? false
        };
    }

    private static bool MatchesActiveTurn(ActiveTurnState? activeTurn, string conversationId, string turnId)
        => activeTurn is not null
            && string.Equals(activeTurn.ConversationId, conversationId, StringComparison.Ordinal)
            && string.Equals(activeTurn.TurnId, turnId, StringComparison.Ordinal);

    private static bool IsTerminalPhase(ChatTurnPhase phase)
        => phase is ChatTurnPhase.Completed or ChatTurnPhase.Failed or ChatTurnPhase.Cancelled;

    private static IImmutableDictionary<string, ConversationBindingSlice>? UpdateBindings(
        IImmutableDictionary<string, ConversationBindingSlice>? bindings,
        ConversationBindingSlice? binding)
    {
        if (binding is null || string.IsNullOrWhiteSpace(binding.ConversationId))
        {
            return bindings;
        }

        var current = bindings ?? ImmutableDictionary<string, ConversationBindingSlice>.Empty;
        if (IsBindingEmpty(binding))
        {
            return current.Remove(binding.ConversationId);
        }

        return current.SetItem(binding.ConversationId, binding);
    }

    private static IImmutableDictionary<string, ConversationRuntimeSlice> UpdateRuntimeStates(
        IImmutableDictionary<string, ConversationRuntimeSlice>? runtimeStates,
        ConversationRuntimeSlice runtimeState)
    {
        if (string.IsNullOrWhiteSpace(runtimeState.ConversationId))
        {
            return runtimeStates ?? ImmutableDictionary<string, ConversationRuntimeSlice>.Empty;
        }

        var current = runtimeStates ?? ImmutableDictionary<string, ConversationRuntimeSlice>.Empty;
        return current.SetItem(runtimeState.ConversationId, runtimeState);
    }

    private static bool IsBindingEmpty(ConversationBindingSlice binding)
        => string.IsNullOrWhiteSpace(binding.RemoteSessionId)
            && string.IsNullOrWhiteSpace(binding.ProfileId);

    private static ChatState ProjectConversation(ChatState current, string? conversationId)
    {
        var content = current.ResolveContentSlice(conversationId);
        var sessionState = current.ResolveSessionStateSlice(conversationId);
        return current with
        {
            HydratedConversationId = conversationId,
            Transcript = content?.Transcript,
            PlanEntries = content?.PlanEntries,
            ShowPlanPanel = content?.ShowPlanPanel ?? false,
            PlanTitle = content?.PlanTitle,
            AvailableModes = sessionState?.AvailableModes,
            SelectedModeId = sessionState?.SelectedModeId,
            ConfigOptions = sessionState?.ConfigOptions,
            ShowConfigOptionsPanel = sessionState?.ShowConfigOptionsPanel ?? false,
            IsHydrating = false,
            IsPromptInFlight = false,
            ActiveTurn = null
        };
    }

    private static bool ShouldProjectConversation(ChatState current, string? conversationId)
        => string.Equals(current.HydratedConversationId, conversationId, StringComparison.Ordinal);

    private static IImmutableDictionary<string, ConversationContentSlice>? UpdateConversationContents(
        IImmutableDictionary<string, ConversationContentSlice>? contents,
        string? conversationId,
        ConversationContentSlice slice)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return contents;
        }

        var current = contents ?? ImmutableDictionary<string, ConversationContentSlice>.Empty;
        return current.SetItem(conversationId, slice);
    }

    private static IImmutableDictionary<string, ConversationSessionStateSlice>? UpdateConversationSessionStates(
        IImmutableDictionary<string, ConversationSessionStateSlice>? sessionStates,
        string? conversationId,
        ConversationSessionStateSlice slice)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return sessionStates;
        }

        var current = sessionStates ?? ImmutableDictionary<string, ConversationSessionStateSlice>.Empty;
        return current.SetItem(conversationId, slice);
    }

    private static ConversationContentSlice BuildUpdatedContentSlice(
        ConversationContentSlice? existing,
        IImmutableList<ConversationMessageSnapshot>? transcript = null,
        IImmutableList<ConversationPlanEntrySnapshot>? planEntries = null,
        bool? showPlanPanel = null,
        string? planTitle = null)
    {
        var current = existing ?? new ConversationContentSlice(
            ImmutableList<ConversationMessageSnapshot>.Empty,
            ImmutableList<ConversationPlanEntrySnapshot>.Empty,
            false,
            null);
        return new ConversationContentSlice(
            transcript ?? current.Transcript,
            planEntries ?? current.PlanEntries,
            showPlanPanel ?? current.ShowPlanPanel,
            planTitle ?? current.PlanTitle);
    }

    private static ConversationSessionStateSlice BuildUpdatedSessionStateSlice(
        ConversationSessionStateSlice? existing,
        IImmutableList<ConversationModeOptionSnapshot>? availableModes,
        string? selectedModeId,
        bool hasSelectedModeId,
        IImmutableList<ConversationConfigOptionSnapshot>? configOptions,
        bool? showConfigOptionsPanel)
    {
        var current = existing ?? new ConversationSessionStateSlice(
            ImmutableList<ConversationModeOptionSnapshot>.Empty,
            null,
            ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            false);
        return new ConversationSessionStateSlice(
            availableModes ?? current.AvailableModes,
            hasSelectedModeId ? selectedModeId : current.SelectedModeId,
            configOptions ?? current.ConfigOptions,
            showConfigOptionsPanel ?? current.ShowConfigOptionsPanel);
    }

    private static IImmutableList<ConversationMessageSnapshot>? ResolveTranscript(ChatState current, string? conversationId)
    {
        var sliceTranscript = current.ResolveContentSlice(conversationId)?.Transcript;
        if (sliceTranscript != null)
        {
            return sliceTranscript;
        }

        return ShouldProjectConversation(current, conversationId)
            ? current.Transcript
            : null;
    }

    private static IImmutableList<ConversationMessageSnapshot> UpsertTranscript(
        IImmutableList<ConversationMessageSnapshot>? transcript,
        ConversationMessageSnapshot message)
    {
        var current = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        var existingIndex = -1;
        for (var i = 0; i < current.Count; i++)
        {
            if (string.Equals(current[i].Id, message.Id, StringComparison.Ordinal))
            {
                existingIndex = i;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            return current.SetItem(existingIndex, message);
        }

        return current.Add(message);
    }

    private static IImmutableList<ConversationMessageSnapshot> AppendTranscriptDelta(
        IImmutableList<ConversationMessageSnapshot>? transcript,
        string delta)
    {
        var current = transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty;
        if (current.Count > 0)
        {
            var last = current[^1];
            if (!last.IsOutgoing && string.Equals(last.ContentType, "text", StringComparison.Ordinal))
            {
                return current.SetItem(current.Count - 1, CloneMessage(
                    last,
                    textContent: (last.TextContent ?? string.Empty) + delta,
                    timestamp: DateTime.UtcNow));
            }
        }

        return current.Add(new ConversationMessageSnapshot
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            IsOutgoing = false,
            ContentType = "text",
            TextContent = delta
        });
    }

    private static ConversationMessageSnapshot ToSnapshot(ChatMessage message)
    {
        var text = message.Parts?.OfType<TextPart>().LastOrDefault()?.Text
            ?? message.Content
            ?? string.Empty;

        return new ConversationMessageSnapshot
        {
            Id = message.Id,
            Timestamp = message.Timestamp.UtcDateTime,
            IsOutgoing = message.IsOutgoing,
            ContentType = "text",
            TextContent = text
        };
    }

    private static ConversationMessageSnapshot CloneMessage(
        ConversationMessageSnapshot source,
        string? textContent = null,
        DateTime? timestamp = null)
    {
        return new ConversationMessageSnapshot
        {
            Id = source.Id,
            Timestamp = timestamp ?? source.Timestamp,
            IsOutgoing = source.IsOutgoing,
            ContentType = source.ContentType,
            Title = source.Title,
            TextContent = textContent ?? source.TextContent,
            ImageData = source.ImageData,
            ImageMimeType = source.ImageMimeType,
            AudioData = source.AudioData,
            AudioMimeType = source.AudioMimeType,
            ToolCallId = source.ToolCallId,
            ToolCallKind = source.ToolCallKind,
            ToolCallStatus = source.ToolCallStatus,
            ToolCallJson = source.ToolCallJson,
            PlanEntry = source.PlanEntry is null
                ? null
                : new ConversationPlanEntrySnapshot
                {
                    Content = source.PlanEntry.Content,
                    Status = source.PlanEntry.Status,
                    Priority = source.PlanEntry.Priority
                },
            ModeId = source.ModeId
        };
    }
}
