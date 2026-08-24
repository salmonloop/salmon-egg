namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// What a package-manager query learned: whether the package is installed, and which toolchain answered.
/// </summary>
/// <remarks>
/// A package manager answers for the toolchain currently on PATH. With several versions installed — nvm,
/// asdf, a system Node alongside a user one — a package installed under a different version reads as
/// absent, and the honest report is "this launcher says no", not "it is not on this machine". Carrying the
/// launcher and the matched location lets the wizard say which one it asked.
/// </remarks>
public sealed class AcpPackageQueryResult
{
    private AcpPackageQueryResult(bool? isInstalled, string? location, string? launcherPath)
    {
        IsInstalled = isInstalled;
        Location = location;
        LauncherPath = launcherPath;
    }

    /// <summary>
    /// True when found, false when the launcher answered and did not list it, null when the query could
    /// not be answered at all. Null must never be read as absence.
    /// </summary>
    public bool? IsInstalled { get; }

    /// <summary>Path the package was found at, null unless <see cref="IsInstalled"/> is true.</summary>
    public string? Location { get; }

    /// <summary>The package manager that answered, null when none could be run.</summary>
    public string? LauncherPath { get; }

    public static AcpPackageQueryResult Found(string location, string launcherPath)
        => new(isInstalled: true, location, launcherPath);

    public static AcpPackageQueryResult Absent(string launcherPath)
        => new(isInstalled: false, location: null, launcherPath);

    public static AcpPackageQueryResult Unknown()
        => new(isInstalled: null, location: null, launcherPath: null);
}
