using System.Collections.Generic;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public readonly record struct DiscoverRemoteSessionOpenRequest(
    string RemoteSessionId,
    string? RemoteSessionCwd,
    string? ProfileId,
    string? RemoteSessionTitle,
    IReadOnlyList<string>? RemoteSessionAdditionalDirectories = null);

public readonly record struct DiscoverRemoteSessionOpenResult(
    bool Succeeded,
    string? LocalConversationId,
    string? ErrorMessage);
