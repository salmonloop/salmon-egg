using System;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.ViewModels.Chat.Transcript;

internal sealed class ChatTranscriptProjectionContext
{
    public required Func<ChatTranscriptVirtualizedMessageCollection> GetMessageHistory { get; init; }

    public required Action<ChatTranscriptVirtualizedMessageCollection> SetMessageHistory { get; init; }

    public required Func<ConversationMessageSnapshot, int, ChatMessageViewModel> FromSnapshot { get; init; }

    public required Func<ChatMessageViewModel, ConversationMessageSnapshot, bool> MatchesSnapshot { get; init; }

    public required Func<ChatMessageViewModel, ConversationMessageSnapshot, int, bool> TryPatchMessage { get; init; }

    public required Func<ConversationMessageSnapshot, int, string> GetProjectionItemKey { get; init; }

    public required Func<string?, bool, bool> UpdateVisibleTranscriptConversationId { get; init; }

    public required Action RaiseTranscriptStateChanged { get; init; }
}
