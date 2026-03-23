using System;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public sealed record ActiveTurnState(
    string ConversationId,
    string TurnId,
    ChatTurnPhase Phase,
    DateTime StartedAtUtc,
    DateTime LastUpdatedAtUtc,
    string? ToolCallId = null,
    string? ToolTitle = null,
    string? FailureMessage = null);
