namespace SalmonEgg.Presentation.Core.Services.Chat;

internal static class RemoteConversationPersistencePolicy
{
    public static bool IsRemoteBacked(string? remoteSessionId, string? boundProfileId = null)
        => !string.IsNullOrWhiteSpace(remoteSessionId)
            || !string.IsNullOrWhiteSpace(boundProfileId);

    public static bool ShouldPersistRuntimeContent(string? remoteSessionId, string? boundProfileId = null)
        => !IsRemoteBacked(remoteSessionId, boundProfileId);

    public static bool ShouldRestoreRuntimeContent(string? remoteSessionId, string? boundProfileId = null)
        => !IsRemoteBacked(remoteSessionId, boundProfileId);
}
