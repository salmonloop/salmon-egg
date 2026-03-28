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
            SelectConversationAction selectConversation => Mutate(current, current with
            {
                HydratedConversationId = selectConversation.ConversationId,
                Transcript = null,
                PlanEntries = null,
                AvailableModes = null,
                SelectedModeId = null,
                ConfigOptions = null,
                ShowConfigOptionsPanel = false,
                ShowPlanPanel = false,
                PlanTitle = null,
                IsHydrating = false,
                IsPromptInFlight = false,
                ActiveTurn = null
            }),
            SetBindingSliceAction setBinding => Mutate(current, current with
            {
                Bindings = UpdateBindings(current.Bindings, setBinding.Binding)
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
            AddMessageAction addMessage => Mutate(current, current with
            {
                Transcript = UpsertTranscript(current.Transcript, ToSnapshot(addMessage.Message))
            }),
            HydrateConversationAction hydrate when !string.Equals(hydrate.ConversationId, current.HydratedConversationId, StringComparison.Ordinal)
                => current,
            HydrateConversationAction hydrate => Mutate(current, current with
            {
                Transcript = hydrate.Transcript,
                PlanEntries = hydrate.PlanEntries,
                ShowPlanPanel = hydrate.ShowPlanPanel,
                PlanTitle = hydrate.PlanTitle
            }),
            ReplacePlanEntriesAction replacePlan when !string.Equals(replacePlan.ConversationId, current.HydratedConversationId, StringComparison.Ordinal)
                => current,
            ReplacePlanEntriesAction replacePlan => Mutate(current, current with
            {
                PlanEntries = replacePlan.PlanEntries,
                ShowPlanPanel = replacePlan.ShowPlanPanel,
                PlanTitle = replacePlan.PlanTitle
            }),
            UpsertTranscriptMessageAction upsertMessage when !string.Equals(upsertMessage.ConversationId, current.HydratedConversationId, StringComparison.Ordinal)
                => current,
            UpsertTranscriptMessageAction upsertMessage => Mutate(current, current with
            {
                Transcript = UpsertTranscript(current.Transcript, upsertMessage.Message)
            }),
            SetConversationSessionStateAction setSessionState when !string.Equals(setSessionState.ConversationId, current.HydratedConversationId, StringComparison.Ordinal)
                => current,
            SetConversationSessionStateAction setSessionState => Mutate(current, current with
            {
                AvailableModes = setSessionState.AvailableModes,
                SelectedModeId = setSessionState.SelectedModeId,
                ConfigOptions = setSessionState.ConfigOptions,
                ShowConfigOptionsPanel = setSessionState.ShowConfigOptionsPanel
            }),
            MergeConversationSessionStateAction mergeSessionState when !string.Equals(mergeSessionState.ConversationId, current.HydratedConversationId, StringComparison.Ordinal)
                => current,
            MergeConversationSessionStateAction mergeSessionState => Mutate(current, current with
            {
                AvailableModes = mergeSessionState.AvailableModes ?? current.AvailableModes,
                SelectedModeId = mergeSessionState.HasSelectedModeId ? mergeSessionState.SelectedModeId : current.SelectedModeId,
                ConfigOptions = mergeSessionState.ConfigOptions ?? current.ConfigOptions,
                ShowConfigOptionsPanel = mergeSessionState.ShowConfigOptionsPanel ?? current.ShowConfigOptionsPanel
            }),
            UpdateMessageAction updateMessage => Mutate(current, current with
            {
                Transcript = UpsertTranscript(current.Transcript, ToSnapshot(updateMessage.Message))
            }),
            AppendTextDeltaAction appendDelta when !string.Equals(appendDelta.ConversationId, current.HydratedConversationId, StringComparison.Ordinal)
                => current,
            AppendTextDeltaAction appendDelta => Mutate(current, current with
            {
                Transcript = AppendTranscriptDelta(current.Transcript, appendDelta.Delta)
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

        return next with
        {
            Generation = checked(current.Generation + 1)
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

    private static bool IsBindingEmpty(ConversationBindingSlice binding)
        => string.IsNullOrWhiteSpace(binding.RemoteSessionId)
            && string.IsNullOrWhiteSpace(binding.ProfileId);

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
