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

    public static bool CanReuseRemoteWarmConversation(
        ConversationRuntimeSlice? runtimeState,
        ConversationBindingSlice? binding,
        string? currentConnectionInstanceId)
    {
        if (!HasAuthoritativeWarmRuntime(runtimeState))
        {
            return false;
        }

        var hydratedRuntime = runtimeState!.Value;

        if (binding is null
            || string.IsNullOrWhiteSpace(binding.RemoteSessionId)
            || string.IsNullOrWhiteSpace(currentConnectionInstanceId))
        {
            return false;
        }

        return string.Equals(hydratedRuntime.RemoteSessionId, binding.RemoteSessionId, StringComparison.Ordinal)
            && string.Equals(hydratedRuntime.ProfileId, binding.ProfileId, StringComparison.Ordinal)
            && string.Equals(hydratedRuntime.ConnectionInstanceId, currentConnectionInstanceId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns a human-readable reason why warm reuse was denied, or null if reuse is allowed.
    /// Useful for diagnostic logging when falling back from hot return to slow hydration.
    /// </summary>
    public static string? GetWarmReuseDenialReason(
        ConversationRuntimeSlice? runtimeState,
        ConversationBindingSlice? binding,
        string? currentConnectionInstanceId)
    {
        if (runtimeState is not { Phase: ConversationRuntimePhase.Warm } hydratedRuntime)
        {
            return "RuntimeStateNotWarm";
        }

        if (!HasAuthoritativeWarmRuntime(runtimeState))
        {
            return "WarmRuntimeNotAuthoritative";
        }

        if (binding is null
            || string.IsNullOrWhiteSpace(binding.RemoteSessionId)
            || string.IsNullOrWhiteSpace(currentConnectionInstanceId))
        {
            return "MissingBindingOrConnectionInstanceId";
        }

        if (!string.Equals(hydratedRuntime.RemoteSessionId, binding.RemoteSessionId, StringComparison.Ordinal))
        {
            return "RemoteSessionIdMismatch";
        }

        if (!string.Equals(hydratedRuntime.ProfileId, binding.ProfileId, StringComparison.Ordinal))
        {
            return "ProfileIdMismatch";
        }

        if (!string.Equals(hydratedRuntime.ConnectionInstanceId, currentConnectionInstanceId, StringComparison.Ordinal))
        {
            return "ConnectionInstanceIdMismatch";
        }

        return null;
    }
}
