using SalmonEgg.Domain.Models.Conversation;
using System.Collections.Immutable;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public record ChatState(
    string? SelectedConversationId = null,
    string? HydratedConversationId = null,
    IImmutableDictionary<string, ConversationBindingSlice>? Bindings = null,
    long Generation = 0,
    string? SelectedAcpProfileId = null,
    bool IsPromptInFlight = false,
    bool IsThinking = false,
    bool IsConnecting = false,
    bool IsInitializing = false,
    bool IsAuthenticationRequired = false,
    string ConnectionStatus = "Disconnected",
    string? ConnectionError = null,
    string? AuthenticationHintMessage = null,
    string? AgentName = null,
    string? AgentVersion = null,
    IImmutableList<ConversationMessageSnapshot>? Transcript = null,
    IImmutableList<ConversationPlanEntrySnapshot>? PlanEntries = null,
    bool ShowPlanPanel = false,
    string? PlanTitle = null,
    string DraftText = "")
{
    public static ChatState Empty { get; } = new();

    public ConversationBindingSlice? Binding => ResolveBinding(HydratedConversationId);

    public ConversationBindingSlice? ResolveBinding(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || Bindings is null)
        {
            return null;
        }

        return Bindings.TryGetValue(conversationId, out var binding)
            ? binding
            : null;
    }
}
