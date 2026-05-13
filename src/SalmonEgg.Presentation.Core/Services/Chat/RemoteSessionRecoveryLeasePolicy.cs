using System;
using System.Collections.Generic;
using System.Linq;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Identifies one authoritative remote session recovery lease.
/// </summary>
/// <param name="RecoveryMode">The ACP recovery method used by the lease.</param>
/// <param name="ProfileId">The profile that owns the remote session.</param>
/// <param name="ConnectionInstanceId">The authoritative connection instance that owns the transport.</param>
/// <param name="RemoteSessionId">The ACP remote session identifier.</param>
/// <param name="Cwd">The working directory used for the recovery request.</param>
public readonly record struct RemoteSessionRecoveryLeaseKey(
    AcpSessionRecoveryMode RecoveryMode,
    string? ProfileId,
    string? ConnectionInstanceId,
    string RemoteSessionId,
    string Cwd);

/// <summary>
/// Describes whether a requested remote session recovery should reuse or start a lease.
/// </summary>
public enum RemoteSessionRecoveryLeaseDecisionKind
{
    ReuseExisting,
    StartNew
}

/// <summary>
/// Result of evaluating a requested remote session recovery lease against active leases.
/// </summary>
/// <param name="Kind">The lease action selected by the policy.</param>
/// <param name="ConflictingLeasesToCancel">Existing leases that conflict with the requested lease.</param>
public readonly record struct RemoteSessionRecoveryLeaseDecision(
    RemoteSessionRecoveryLeaseDecisionKind Kind,
    IReadOnlyList<RemoteSessionRecoveryLeaseKey> ConflictingLeasesToCancel)
{
    /// <summary>
    /// Gets a value indicating whether the caller should start a new recovery lease.
    /// </summary>
    public bool ShouldStartNew => Kind == RemoteSessionRecoveryLeaseDecisionKind.StartNew;

    /// <summary>
    /// Gets a value indicating whether the caller should reuse an existing recovery lease.
    /// </summary>
    public bool ShouldReuseExisting => Kind == RemoteSessionRecoveryLeaseDecisionKind.ReuseExisting;

    /// <summary>
    /// Creates a decision that reuses an existing lease.
    /// </summary>
    public static RemoteSessionRecoveryLeaseDecision ReuseExisting()
        => new(
            RemoteSessionRecoveryLeaseDecisionKind.ReuseExisting,
            Array.Empty<RemoteSessionRecoveryLeaseKey>());

    /// <summary>
    /// Creates a decision that starts a new lease and cancels any conflicting leases.
    /// </summary>
    /// <param name="conflictingLeasesToCancel">Existing leases that conflict with the requested lease.</param>
    public static RemoteSessionRecoveryLeaseDecision StartNew(
        IReadOnlyCollection<RemoteSessionRecoveryLeaseKey> conflictingLeasesToCancel)
        => new(
            RemoteSessionRecoveryLeaseDecisionKind.StartNew,
            conflictingLeasesToCancel.Count == 0
                ? Array.Empty<RemoteSessionRecoveryLeaseKey>()
                : conflictingLeasesToCancel.ToArray());
}

/// <summary>
/// Canonical conflict policy for in-flight ACP remote session recovery leases.
/// </summary>
public static class RemoteSessionRecoveryLeasePolicy
{
    /// <summary>
    /// Evaluates whether the requested lease should reuse an existing recovery request, start independently,
    /// or cancel conflicting leases before starting.
    /// </summary>
    /// <param name="requested">The requested remote recovery lease.</param>
    /// <param name="activeLeases">The currently active remote recovery leases.</param>
    public static RemoteSessionRecoveryLeaseDecision Decide(
        RemoteSessionRecoveryLeaseKey requested,
        IReadOnlyCollection<RemoteSessionRecoveryLeaseKey> activeLeases)
    {
        ArgumentNullException.ThrowIfNull(activeLeases);

        if (activeLeases.Any(candidate => IsSameLease(candidate, requested)))
        {
            return RemoteSessionRecoveryLeaseDecision.ReuseExisting();
        }

        var conflictingLeases = activeLeases
            .Where(candidate => Conflicts(candidate, requested))
            .ToArray();
        return RemoteSessionRecoveryLeaseDecision.StartNew(conflictingLeases);
    }

    /// <summary>
    /// Determines whether two lease keys identify the same in-flight recovery request.
    /// </summary>
    /// <param name="candidate">The active lease key.</param>
    /// <param name="requested">The requested lease key.</param>
    public static bool IsSameLease(
        RemoteSessionRecoveryLeaseKey candidate,
        RemoteSessionRecoveryLeaseKey requested)
        => candidate.RecoveryMode == requested.RecoveryMode
            && Same(candidate.ProfileId, requested.ProfileId)
            && Same(candidate.ConnectionInstanceId, requested.ConnectionInstanceId)
            && Same(candidate.RemoteSessionId, requested.RemoteSessionId)
            && Same(candidate.Cwd, requested.Cwd);

    /// <summary>
    /// Determines whether an active lease must be canceled before starting the requested lease.
    /// </summary>
    /// <param name="candidate">The active lease key.</param>
    /// <param name="requested">The requested lease key.</param>
    public static bool Conflicts(
        RemoteSessionRecoveryLeaseKey candidate,
        RemoteSessionRecoveryLeaseKey requested)
    {
        if (!Same(candidate.ProfileId, requested.ProfileId)
            || !Same(candidate.RemoteSessionId, requested.RemoteSessionId))
        {
            return false;
        }

        if (!Same(candidate.ConnectionInstanceId, requested.ConnectionInstanceId))
        {
            return true;
        }

        return candidate.RecoveryMode != requested.RecoveryMode
            || !Same(candidate.Cwd, requested.Cwd);
    }

    private static bool Same(string? left, string? right)
        => string.Equals(left, right, StringComparison.Ordinal);
}
