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
    /// Canonical warm reuse check with connection-liveness awareness.
    /// When <paramref name="isConnectionAlive"/> is provided, the policy verifies that the
    /// conversation's stored connection is still alive in the pool rather than requiring
    /// strict equality with <paramref name="currentConnectionInstanceId"/>.
    /// This correctly handles cross-profile scenarios where the global connection ID may
    /// differ from the conversation's bound connection.
    /// </summary>
    public static bool CanReuseRemoteWarmConversation(
        ConversationRuntimeSlice? runtimeState,
        ConversationBindingSlice? binding,
        string? currentConnectionInstanceId,
        Func<string, bool>? isConnectionAlive = null)
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

        var connectionAlive = isConnectionAlive is not null
            ? !string.IsNullOrWhiteSpace(hydratedRuntime.ConnectionInstanceId)
              && isConnectionAlive(hydratedRuntime.ConnectionInstanceId!)
            : !string.IsNullOrWhiteSpace(currentConnectionInstanceId)
              && string.Equals(hydratedRuntime.ConnectionInstanceId, currentConnectionInstanceId, StringComparison.Ordinal);

        if (!connectionAlive)
        {
            return false;
        }

        return string.Equals(hydratedRuntime.RemoteSessionId, binding.RemoteSessionId, StringComparison.Ordinal)
            && string.Equals(hydratedRuntime.ProfileId, binding.ProfileId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Backward-compatible overload that uses strict <see cref="ConversationRuntimeSlice.ConnectionInstanceId"/>
    /// equality against <paramref name="currentConnectionInstanceId"/>.
    /// </summary>
    public static bool CanReuseRemoteWarmConversation(
        ConversationRuntimeSlice? runtimeState,
        ConversationBindingSlice? binding,
        string? currentConnectionInstanceId)
        => CanReuseRemoteWarmConversation(runtimeState, binding, currentConnectionInstanceId, isConnectionAlive: null);

    /// <summary>
    /// Returns a human-readable reason why warm reuse was denied, or null if reuse is allowed.
    /// Useful for diagnostic logging when falling back from hot return to slow hydration.
    /// </summary>
    public static string? GetWarmReuseDenialReason(
        ConversationRuntimeSlice? runtimeState,
        ConversationBindingSlice? binding,
        string? currentConnectionInstanceId,
        Func<string, bool>? isConnectionAlive = null)
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

        if (isConnectionAlive is not null)
        {
            if (string.IsNullOrWhiteSpace(hydratedRuntime.ConnectionInstanceId)
                || !isConnectionAlive(hydratedRuntime.ConnectionInstanceId!))
            {
                return "ConnectionNotAlive";
            }
        }
        else if (string.IsNullOrWhiteSpace(currentConnectionInstanceId)
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

    /// <summary>
    /// Backward-compatible overload using strict connection ID equality.
    /// </summary>
    public static string? GetWarmReuseDenialReason(
        ConversationRuntimeSlice? runtimeState,
        ConversationBindingSlice? binding,
        string? currentConnectionInstanceId)
        => GetWarmReuseDenialReason(runtimeState, binding, currentConnectionInstanceId, isConnectionAlive: null);
}
