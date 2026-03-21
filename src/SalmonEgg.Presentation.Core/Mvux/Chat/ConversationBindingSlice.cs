namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public sealed record ConversationBindingSlice(
    string? ConversationId,
    string? RemoteSessionId,
    string? ProfileId);
