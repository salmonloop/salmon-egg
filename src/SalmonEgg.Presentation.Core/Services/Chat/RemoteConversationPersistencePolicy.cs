namespace SalmonEgg.Presentation.Core.Services.Chat;

internal static class RemoteConversationPersistencePolicy
{
    public static bool IsRemoteBacked(string? remoteSessionId)
        => !string.IsNullOrWhiteSpace(remoteSessionId);

    public static bool ShouldPersistRuntimeContent(string? remoteSessionId)
        => !IsRemoteBacked(remoteSessionId);

    public static bool ShouldRestoreRuntimeContent(string? remoteSessionId)
        => !IsRemoteBacked(remoteSessionId);
}
