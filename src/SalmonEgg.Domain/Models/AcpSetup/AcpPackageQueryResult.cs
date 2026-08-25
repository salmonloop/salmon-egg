namespace SalmonEgg.Domain.Models.AcpSetup;

/// <summary>
/// What a package-manager query answered about one package: whether it is installed, and where the
/// manager found it.
/// </summary>
/// <remarks>
/// <see cref="IsInstalled"/> is nullable because a query can fail to run at all — the manager is
/// absent, times out, or errors — and that is a third answer, not a "no". Reporting an unanswerable
/// query as absent makes the wizard tell users to install something they already have.
///
/// The location matters on machines with several toolchain versions: a package manager answers for the
/// version currently on PATH, so a package installed under a different one reads as absent. Carrying
/// the path that answered lets the wizard say which toolchain it asked instead of leaving the user to
/// guess why an install they remember doing is invisible.
/// </remarks>
public sealed class AcpPackageQueryResult
{
    private AcpPackageQueryResult(bool? isInstalled, string? location, string? queryExecutablePath)
    {
        IsInstalled = isInstalled;
        Location = location;
        QueryExecutablePath = queryExecutablePath;
    }

    /// <summary>True or false when the query ran; null when it could not be answered.</summary>
    public bool? IsInstalled { get; }

    /// <summary>Where the manager reported the package, or null when it reported none.</summary>
    public string? Location { get; }

    /// <summary>The package-manager executable that answered, or null when none could be run.</summary>
    public string? QueryExecutablePath { get; }

    /// <summary>The package is installed, at <paramref name="location"/>.</summary>
    public static AcpPackageQueryResult Found(string location, string queryExecutablePath)
        => new(true, location, queryExecutablePath);

    /// <summary>The manager answered, and does not have this package.</summary>
    public static AcpPackageQueryResult Absent(string queryExecutablePath)
        => new(false, location: null, queryExecutablePath);

    /// <summary>The query could not be answered; callers must not read this as absent.</summary>
    public static AcpPackageQueryResult Unknown(string? queryExecutablePath = null)
        => new(null, location: null, queryExecutablePath);
}
