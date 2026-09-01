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

    /// <summary>
    /// The Windows program-files directory; contributes nothing elsewhere.
    /// </summary>
    /// <remarks>
    /// Named rather than spelled as an absolute path because the directory is not fixed: it follows the
    /// system drive, is localized in some installations, and has a separate 32-bit sibling. A layout that
    /// hard-coded <c>C:\Program Files</c> would miss every machine that does not use it, which is exactly
    /// the population this root exists for — Node's official MSI installs here, and the wizard could not
    /// see the result.
    /// </remarks>
    WindowsProgramFiles,

    /// <summary>
    /// The directory this app installs toolchains into.
    /// </summary>
    /// <remarks>
    /// A named root rather than a path for the same reason as the Windows ones: where app data lives is a
    /// platform decision the infrastructure layer owns. Present so a toolchain the wizard installed is
    /// discovered by the same scan that finds every other one, instead of through a second lookup that
    /// only knows about our own installs and would drift from it.
    /// </remarks>
    SalmonEggToolchains,

    /// <summary>The single segment is already an absolute path.</summary>
    Absolute
}
