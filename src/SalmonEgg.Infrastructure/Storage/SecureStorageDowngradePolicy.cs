namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// Decides what a host does when the platform secret store is unavailable for a write.
/// </summary>
/// <remarks>
/// Reads are never gated by this policy. A secret that an earlier session already downgraded has to
/// stay visible so it can be inspected, rotated, or cleared; hiding it would leave plaintext material
/// on disk that the user has no supported way to remove.
/// </remarks>
public enum SecureStorageDowngradePolicy
{
    /// <summary>
    /// Stores the secret in the weaker plaintext store and records the downgrade.
    /// </summary>
    /// <remarks>
    /// This keeps an interactive session working when a desktop keyring is missing or locked, at the
    /// cost of the secret no longer being protected by the platform.
    /// </remarks>
    AllowPlaintextDowngrade,

    /// <summary>
    /// Fails the write instead of storing the secret unprotected.
    /// </summary>
    /// <remarks>
    /// The right default for non-interactive hosts: a scripted invocation cannot see a warning stream
    /// in time to react, so a silent downgrade would persist credentials in plaintext on machines the
    /// operator never inspects.
    /// </remarks>
    FailClosed
}
