using SalmonEgg.Domain.Models.Conversation;
using System.Collections.Immutable;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public record ChatState(
    string? HydratedConversationId = null,
    IImmutableDictionary<string, ConversationBindingSlice>? Bindings = null,
    IImmutableDictionary<string, ConversationContentSlice>? ConversationContents = null,
    IImmutableDictionary<string, ConversationSessionStateSlice>? ConversationSessionStates = null,
    IImmutableDictionary<string, ConversationRuntimeSlice>? RuntimeStates = null,
    IImmutableDictionary<string, ActiveTurnState>? Turns = null,
    long Generation = 0,
    string? AgentProfileId = null,
    string? AgentName = null,
    string? AgentVersion = null,
    IImmutableList<ConversationMessageSnapshot>? Transcript = null,
    IImmutableList<ConversationPlanEntrySnapshot>? PlanEntries = null,
    IImmutableList<ConversationModeOptionSnapshot>? AvailableModes = null,
    string? SelectedModeId = null,
    IImmutableList<ConversationConfigOptionSnapshot>? ConfigOptions = null,
    bool ShowConfigOptionsPanel = false,
    IImmutableList<ConversationAvailableCommandSnapshot>? AvailableCommands = null,
    ConversationSessionInfoSnapshot? SessionInfo = null,
    ConversationUsageSnapshot? Usage = null,
    bool ShowPlanPanel = false,
    string DraftText = "",
    long DraftRevision = 0,
    bool IsHydrating = false,
    IImmutableDictionary<string, ConversationOperationFailure>? OperationFailures = null)
{
    public static ChatState Empty { get; } = new();

    public ConversationBindingSlice? Binding => ResolveBinding(HydratedConversationId);

    public ActiveTurnState? ActiveTurn => ResolveTurn(HydratedConversationId);

    public ConversationOperationFailure? ResolveOperationFailure(string? conversationId)
        => conversationId is not null && OperationFailures?.TryGetValue(conversationId, out var failure) == true
            ? failure : null;

    public ActiveTurnState? ResolveTurn(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || Turns is null)
        {
            return null;
        }

        return Turns.TryGetValue(conversationId, out var turn) ? turn : null;
    }

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

    public ConversationRuntimeSlice? ResolveRuntimeState(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || RuntimeStates is null)
        {
            return null;
        }

        return RuntimeStates.TryGetValue(conversationId, out var state)
            ? state
            : null;
    }

    public ConversationContentSlice? ResolveContentSlice(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || ConversationContents is null)
        {
            return null;
        }

        return ConversationContents.TryGetValue(conversationId, out var content)
            ? content
            : null;
    }

    public ConversationSessionStateSlice? ResolveSessionStateSlice(string? conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || ConversationSessionStates is null)
        {
            return null;
        }

        return ConversationSessionStates.TryGetValue(conversationId, out var sessionState)
            ? sessionState
            : null;
    }
}
