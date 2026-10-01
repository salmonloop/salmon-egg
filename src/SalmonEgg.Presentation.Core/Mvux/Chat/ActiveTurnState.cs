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
    string? FailureMessage = null,
    string? PendingUserMessageLocalId = null,
    string? PendingUserProtocolMessageId = null,
    string? PendingUserMessageText = null,
    string? ProfileId = null,
    string? RemoteSessionId = null,
    string? ConnectionInstanceId = null,
    string? StopReason = null,
    bool HasStopReason = false);
