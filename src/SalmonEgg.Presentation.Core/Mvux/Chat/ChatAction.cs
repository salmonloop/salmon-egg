using System;
using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

/// <summary>
/// Base record for all Chat actions.
/// </summary>
public abstract record ChatAction;

/// <summary>
/// Dispatched when the user sends a new text prompt.
/// </summary>
public sealed record SendPromptAction(string Text) : ChatAction;

/// <summary>
/// Dispatched when a new message is received or created locally.
/// </summary>
public sealed record AddMessageAction(ChatMessage Message) : ChatAction;

/// <summary>
/// Dispatched to update an existing message (e.g., streaming content).
/// </summary>
public sealed record UpdateMessageAction(ChatMessage Message) : ChatAction;

/// <summary>
/// Dispatched when the user selects a different conversation.
/// </summary>
public sealed record SelectConversationAction(string? ConversationId) : ChatAction;

/// <summary>
/// Dispatched to update the active conversation binding metadata in store state.
/// </summary>
public sealed record SetBindingSliceAction(ConversationBindingSlice? Binding) : ChatAction;

/// <summary>
/// Dispatched when the user edits the draft prompt text.
/// </summary>
public sealed record SetDraftTextAction(string Text) : ChatAction;

/// <summary>
/// Dispatched to clear the current chat state.
/// </summary>
public sealed record ClearChatAction : ChatAction;

public sealed record SetAgentIdentityAction(string? AgentName, string? AgentVersion) : ChatAction;

/// <summary>
/// Dispatched to update the prompt in-flight status.
/// </summary>
public sealed record SetPromptInFlightAction(bool IsInFlight) : ChatAction;

public sealed record BeginTurnAction(string ConversationId, string TurnId, ChatTurnPhase InitialPhase) : ChatAction;

public sealed record AdvanceTurnPhaseAction(string ConversationId, string TurnId, ChatTurnPhase NewPhase, string? ToolCallId = null, string? ToolTitle = null) : ChatAction;

public sealed record CompleteTurnAction(string ConversationId, string TurnId) : ChatAction;

public sealed record FailTurnAction(string ConversationId, string TurnId, string? ErrorMessage = null) : ChatAction;

public sealed record CancelTurnAction(string ConversationId, string TurnId) : ChatAction;

public sealed record ClearTurnAction(string ConversationId) : ChatAction;

[Obsolete("Use BeginTurn/AdvanceTurnPhase instead")]
public sealed record SetIsThinkingAction(bool IsThinking) : ChatAction;


/// <summary>
/// Dispatched when a text delta is received for the active streaming message.
/// </summary>
public sealed record AppendTextDeltaAction(string? ConversationId, string Delta) : ChatAction;

public sealed record HydrateConversationAction(
    string? ConversationId,
    IImmutableList<ConversationMessageSnapshot> Transcript,
    IImmutableList<ConversationPlanEntrySnapshot> PlanEntries,
    bool ShowPlanPanel,
    string? PlanTitle) : ChatAction;

public sealed record UpsertTranscriptMessageAction(string? ConversationId, ConversationMessageSnapshot Message) : ChatAction;

public sealed record ReplacePlanEntriesAction(
    string? ConversationId,
    IImmutableList<ConversationPlanEntrySnapshot> PlanEntries,
    bool ShowPlanPanel,
    string? PlanTitle) : ChatAction;
