using System;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public readonly record struct ConversationWarmReuseConnectionIdentity(
    string? ProfileId,
    string? ConnectionInstanceId);

public readonly record struct ConversationWarmReuseDecision(
    bool CanReuse,
    string? DenialReason);

public static class ConversationRuntimeReasons
{
    public const string SessionLoadCompleted = nameof(SessionLoadCompleted);
    public const string WarmReuse = nameof(WarmReuse);
    public const string WarmReuseAfterProfileReconnect = nameof(WarmReuseAfterProfileReconnect);
    public const string MarkedHydrated = nameof(MarkedHydrated);
}

public static class ConversationWarmReusePolicy
{
    public static bool HasAuthoritativeWarmRuntime(ConversationRuntimeSlice? runtimeState)
    {
        if (runtimeState is not { Phase: ConversationRuntimePhase.Warm } hydratedRuntime)
        {
            return false;
        }

        return string.Equals(hydratedRuntime.Reason, ConversationRuntimeReasons.SessionLoadCompleted, StringComparison.Ordinal)
            || string.Equals(hydratedRuntime.Reason, ConversationRuntimeReasons.WarmReuse, StringComparison.Ordinal)
            || string.Equals(hydratedRuntime.Reason, ConversationRuntimeReasons.WarmReuseAfterProfileReconnect, StringComparison.Ordinal)
            || string.Equals(hydratedRuntime.Reason, ConversationRuntimeReasons.MarkedHydrated, StringComparison.Ordinal);
    }

    /// <summary>
    /// Canonical warm reuse check. Warm reuse is only valid when the authoritative foreground
    /// connection identity still matches the conversation's warm runtime identity.
    /// </summary>
    public static bool CanReuseRemoteWarmConversation(
        ConversationRuntimeSlice? runtimeState,
        ConversationBindingSlice? binding,
        ConversationWarmReuseConnectionIdentity currentConnection,
        bool hasReusableProjection)
        => EvaluateRemoteWarmConversation(
            runtimeState,
            binding,
            currentConnection,
            hasReusableProjection).CanReuse;

    /// <summary>
    /// Canonical warm reuse decision. Warm reuse is only valid when the authoritative
    /// foreground connection identity still matches the conversation's warm runtime identity.
    /// </summary>
    public static ConversationWarmReuseDecision EvaluateRemoteWarmConversation(
        ConversationRuntimeSlice? runtimeState,
        ConversationBindingSlice? binding,
        ConversationWarmReuseConnectionIdentity currentConnection,
        bool hasReusableProjection)
    {
        if (runtimeState is not { Phase: ConversationRuntimePhase.Warm } hydratedRuntime)
        {
            return Denied("RuntimeStateNotWarm");
        }

        if (!HasAuthoritativeWarmRuntime(runtimeState))
        {
            return Denied("WarmRuntimeNotAuthoritative");
        }

        if (binding is null || string.IsNullOrWhiteSpace(binding.RemoteSessionId))
        {
            return Denied("MissingBinding");
        }

        if (!hasReusableProjection)
        {
            return Denied("ProjectionNotReady");
        }

        if (!ConnectionProfileMatches(binding.ProfileId, currentConnection.ProfileId))
        {
            return Denied("ConnectionProfileMismatch");
        }

        if (string.IsNullOrWhiteSpace(currentConnection.ConnectionInstanceId)
            || !string.Equals(hydratedRuntime.ConnectionInstanceId, currentConnection.ConnectionInstanceId, StringComparison.Ordinal))
        {
            return Denied("ConnectionInstanceIdMismatch");
        }

        if (!string.Equals(hydratedRuntime.RemoteSessionId, binding.RemoteSessionId, StringComparison.Ordinal))
        {
            return Denied("RemoteSessionIdMismatch");
        }

        if (!string.Equals(hydratedRuntime.ProfileId, binding.ProfileId, StringComparison.Ordinal))
        {
            return Denied("ProfileIdMismatch");
        }

        return new ConversationWarmReuseDecision(true, null);
    }

    /// <summary>
    /// Returns a human-readable reason why warm reuse was denied, or null if reuse is allowed.
    /// Useful for diagnostic logging when falling back from hot return to slow hydration.
    /// </summary>
    public static string? GetWarmReuseDenialReason(
        ConversationRuntimeSlice? runtimeState,
        ConversationBindingSlice? binding,
        ConversationWarmReuseConnectionIdentity currentConnection,
        bool hasReusableProjection)
        => EvaluateRemoteWarmConversation(
            runtimeState,
            binding,
            currentConnection,
            hasReusableProjection).DenialReason;

    private static bool ConnectionProfileMatches(string? requiredProfileId, string? currentProfileId)
    {
        if (string.IsNullOrWhiteSpace(requiredProfileId))
        {
            return true;
        }

        return string.Equals(requiredProfileId, currentProfileId, StringComparison.Ordinal);
    }

    private static ConversationWarmReuseDecision Denied(string reason)
        => new(false, reason);
}
