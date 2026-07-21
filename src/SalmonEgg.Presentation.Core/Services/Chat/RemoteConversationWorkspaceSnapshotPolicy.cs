using System;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

internal static class RemoteConversationWorkspaceSnapshotPolicy
{
    public static bool CanReuseWarmProjectionSnapshot(
        ConversationBindingSlice? binding,
        ConversationWorkspaceSnapshot? snapshot,
        ConversationWorkspaceSnapshotOrigin? origin)
    {
        if (snapshot is null)
        {
            return false;
        }

        return !RemoteConversationPersistencePolicy.IsRemoteBacked(binding?.RemoteSessionId, binding?.ProfileId)
            || origin is ConversationWorkspaceSnapshotOrigin.RuntimeProjection;
    }

    public static bool HasAuthoritativeRemoteRuntimeProjection(
        ConversationBindingSlice? binding,
        ConversationWorkspaceSnapshot? snapshot,
        ConversationWorkspaceSnapshotOrigin? origin)
        => snapshot is not null
            && RemoteConversationPersistencePolicy.IsRemoteBacked(binding?.RemoteSessionId, binding?.ProfileId)
            && origin is ConversationWorkspaceSnapshotOrigin.RuntimeProjection
            // An empty RuntimeProjection shell is not reusable warm content. Without projected
            // transcript/plan/session chrome, warm short-circuit would leave the chat blank.
            && HasProjectedWarmSnapshotState(snapshot);

    public static bool CanRestoreCachedTranscriptAfterInterruptedHydration(
        ConversationBindingSlice? binding,
        ConversationWorkspaceSnapshot? snapshot,
        ConversationWorkspaceSnapshotOrigin? origin,
        string? currentConnectionInstanceId)
    {
        if (snapshot is null)
        {
            return false;
        }

        if (!RemoteConversationPersistencePolicy.IsRemoteBacked(binding?.RemoteSessionId, binding?.ProfileId))
        {
            return true;
        }

        return HasMatchingRuntimeProjectionConnection(snapshot, origin, currentConnectionInstanceId);
    }

    public static bool CanRestoreCachedTranscriptAfterAuthoritativeHydration(
        ConversationBindingSlice? binding,
        ConversationWorkspaceSnapshot? snapshot,
        ConversationWorkspaceSnapshotOrigin? origin,
        string? currentConnectionInstanceId)
    {
        if (snapshot is null)
        {
            return false;
        }

        return !RemoteConversationPersistencePolicy.IsRemoteBacked(binding?.RemoteSessionId, binding?.ProfileId)
            || HasMatchingRuntimeProjectionConnection(snapshot, origin, currentConnectionInstanceId);
    }


    private static bool HasProjectedWarmSnapshotState(ConversationWorkspaceSnapshot snapshot)
    {
        if ((snapshot.Transcript?.Count ?? 0) > 0
            || (snapshot.Plan?.Count ?? 0) > 0
            || snapshot.ShowPlanPanel)
        {
            return true;
        }

        if ((snapshot.AvailableModes?.Count ?? 0) > 0
            || (snapshot.ConfigOptions?.Count ?? 0) > 0
            || snapshot.ShowConfigOptionsPanel
            || !string.IsNullOrWhiteSpace(snapshot.SelectedModeId))
        {
            return true;
        }

        return (snapshot.AvailableCommands?.Count ?? 0) > 0
            || snapshot.SessionInfo is not null
            || snapshot.Usage is not null;
    }

    private static bool HasMatchingRuntimeProjectionConnection(
        ConversationWorkspaceSnapshot snapshot,
        ConversationWorkspaceSnapshotOrigin? origin,
        string? currentConnectionInstanceId)
        => origin is ConversationWorkspaceSnapshotOrigin.RuntimeProjection
            && !string.IsNullOrWhiteSpace(snapshot.ConnectionInstanceId)
            && !string.IsNullOrWhiteSpace(currentConnectionInstanceId)
            && string.Equals(snapshot.ConnectionInstanceId, currentConnectionInstanceId, StringComparison.Ordinal);
}

internal static class ConversationProjectionRestoreConnectionPolicy
{
    public static string? ResolveCurrentConnectionInstanceId(ChatConnectionState? connectionState)
        => !string.IsNullOrWhiteSpace(connectionState?.ConnectionInstanceId)
            ? connectionState.ConnectionInstanceId
            : null;
}
