using System;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public static class ConversationWarmReusePolicy
{
    public static bool HasAuthoritativeWarmRuntime(ConversationRuntimeSlice? runtimeState)
    {
        if (runtimeState is not { Phase: ConversationRuntimePhase.Warm } hydratedRuntime)
        {
            return false;
        }

        return string.Equals(hydratedRuntime.Reason, "SessionLoadCompleted", StringComparison.Ordinal)
            || string.Equals(hydratedRuntime.Reason, "WarmReuse", StringComparison.Ordinal)
            || string.Equals(hydratedRuntime.Reason, "MarkedHydrated", StringComparison.Ordinal);
    }

    /// <summary>
    /// Canonical warm reuse check. Warm reuse is only valid when the authoritative foreground
    /// connection identity still matches the conversation's warm runtime identity.
    /// </summary>
    public static bool CanReuseRemoteWarmConversation(
        ConversationRuntimeSlice? runtimeState,
        ConversationBindingSlice? binding,
        string? currentConnectionInstanceId,
        bool hasReusableProjection)
    {
        if (!HasAuthoritativeWarmRuntime(runtimeState))
        {
            return false;
        }

        var hydratedRuntime = runtimeState!.Value;

        if (binding is null || string.IsNullOrWhiteSpace(binding.RemoteSessionId))
        {
            return false;
        }

        if (!hasReusableProjection)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(currentConnectionInstanceId)
            || !string.Equals(hydratedRuntime.ConnectionInstanceId, currentConnectionInstanceId, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(hydratedRuntime.RemoteSessionId, binding.RemoteSessionId, StringComparison.Ordinal)
            && string.Equals(hydratedRuntime.ProfileId, binding.ProfileId, StringComparison.Ordinal);
    }

    /// <summary>
     /// Returns a human-readable reason why warm reuse was denied, or null if reuse is allowed.
     /// Useful for diagnostic logging when falling back from hot return to slow hydration.
    /// </summary>
    public static string? GetWarmReuseDenialReason(
        ConversationRuntimeSlice? runtimeState,
        ConversationBindingSlice? binding,
        string? currentConnectionInstanceId,
        bool hasReusableProjection)
    {
        if (runtimeState is not { Phase: ConversationRuntimePhase.Warm } hydratedRuntime)
        {
            return "RuntimeStateNotWarm";
        }

        if (!HasAuthoritativeWarmRuntime(runtimeState))
        {
            return "WarmRuntimeNotAuthoritative";
        }

        if (binding is null || string.IsNullOrWhiteSpace(binding.RemoteSessionId))
        {
            return "MissingBinding";
        }

        if (!hasReusableProjection)
        {
            return "ProjectionNotReady";
        }

        if (string.IsNullOrWhiteSpace(currentConnectionInstanceId)
            || !string.Equals(hydratedRuntime.ConnectionInstanceId, currentConnectionInstanceId, StringComparison.Ordinal))
        {
            return "ConnectionInstanceIdMismatch";
        }

        if (!string.Equals(hydratedRuntime.RemoteSessionId, binding.RemoteSessionId, StringComparison.Ordinal))
        {
            return "RemoteSessionIdMismatch";
        }

        if (!string.Equals(hydratedRuntime.ProfileId, binding.ProfileId, StringComparison.Ordinal))
        {
            return "ProfileIdMismatch";
        }

        return null;
    }
}
