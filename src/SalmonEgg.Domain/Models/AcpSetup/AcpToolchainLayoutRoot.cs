namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// The base directory a <see cref="AcpToolchainLayout"/>'s segments hang off. Named rather than resolved
/// here so the domain stays free of filesystem and platform lookups.
/// </summary>
public enum AcpToolchainLayoutRoot
{
    /// <summary>The current user's home directory.</summary>
    UserHome,

    /// <summary>The Windows roaming application data directory; contributes nothing elsewhere.</summary>
    WindowsRoamingAppData,

    /// <summary>The single segment is already an absolute path.</summary>
    Absolute
}
