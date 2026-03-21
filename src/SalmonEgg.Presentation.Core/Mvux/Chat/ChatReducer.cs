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
                SelectedConversationId = null,
                HydratedConversationId = selectConversation.ConversationId,
                Binding = null,
                Transcript = null,
                PlanEntries = null,
                ShowPlanPanel = false,
                PlanTitle = null,
                IsPromptInFlight = false,
                IsThinking = false
            }),
            SetBindingSliceAction setBinding => Mutate(current, current with { Binding = setBinding.Binding }),
            SelectProfileAction selectProfile => Mutate(current, current with { SelectedAcpProfileId = selectProfile.ProfileId }),
            SetDraftTextAction draftText => Mutate(current, current with { DraftText = draftText.Text }),
            SetPromptInFlightAction setPromptInFlight => Mutate(current, current with { IsPromptInFlight = setPromptInFlight.IsInFlight }),
            SetIsThinkingAction setIsThinking => Mutate(current, current with { IsThinking = setIsThinking.IsThinking }),
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
            SetConnectionLifecycleAction lifecycle => Mutate(current, current with
            {
                IsConnecting = lifecycle.IsConnecting,
                IsInitializing = lifecycle.IsInitialized,
                ConnectionStatus = lifecycle.IsConnected ? "Connected" : "Disconnected",
                ConnectionError = lifecycle.ErrorMessage
            }),
            SetAuthenticationStateAction authentication => Mutate(current, current with
            {
                IsAuthenticationRequired = authentication.IsRequired,
                AuthenticationHintMessage = authentication.HintMessage
            }),
            SetAgentIdentityAction identity => Mutate(current, current with
            {
                AgentName = identity.AgentName,
                AgentVersion = identity.AgentVersion
            }),
            UpdateConnectionStatusAction updateStatus => Mutate(current, current with
            {
                ConnectionStatus = updateStatus.IsConnected ? "Connected" : "Disconnected",
                ConnectionError = updateStatus.ErrorMessage
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
