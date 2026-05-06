using System;
using System.Collections.ObjectModel;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.ViewModels.Chat.Transcript;

internal sealed class ChatTranscriptProjectionContext
{
    public required Func<ObservableCollection<ChatMessageViewModel>> GetMessageHistory { get; init; }

    public required Action<ObservableCollection<ChatMessageViewModel>> SetMessageHistory { get; init; }

    public required Func<ConversationMessageSnapshot, int, ChatMessageViewModel> FromSnapshot { get; init; }

    public required Func<ChatMessageViewModel, ConversationMessageSnapshot, bool> MatchesSnapshot { get; init; }

    public required Func<string?, bool, bool> UpdateVisibleTranscriptConversationId { get; init; }

    public required Action RaiseTranscriptStateChanged { get; init; }
}
