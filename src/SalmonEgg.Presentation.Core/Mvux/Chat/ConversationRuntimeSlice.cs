using System;

namespace SalmonEgg.Presentation.Core.Mvux.Chat;

public enum ConversationRuntimePhase
{
    Unknown = 0,
    Selecting = 1,
    Selected = 2,
    RemoteConnectionReady = 3,
    RemoteHydrating = 4,
    Warm = 5,
    Stale = 6,
    Faulted = 7
}

public readonly record struct ConversationRuntimeSlice(
    string ConversationId,
    ConversationRuntimePhase Phase,
    long ConnectionGeneration,
    string? RemoteSessionId,
    string? ProfileId,
    string? Reason,
    DateTime UpdatedAtUtc);
