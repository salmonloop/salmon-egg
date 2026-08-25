using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Domain.Services.AcpSetup;

/// <summary>
/// Inspects the local machine for the executables and packages an ACP component needs. Platform PATH
/// rules and package-manager invocations live behind this seam so wizard orchestration stays
/// platform-agnostic.
/// </summary>
public interface IAcpExecutableProbe
{
    /// <summary>
    /// True when this platform can start child processes. When false, callers must report
    /// <c>Undetermined</c> rather than claiming a component is missing.
    /// </summary>
    bool SupportsProcessProbing { get; }

    /// <summary>
    /// Resolves <paramref name="command"/> to an absolute path, or null when it is not on PATH.
    /// </summary>
    /// <remarks>
    /// Returns the first match, the way a shell would. Callers that need to know a second install exists
    /// use <see cref="ResolveExecutableCandidatesAsync"/>; callers that only need something runnable
    /// (installers, version reads) stay on this.
    /// </remarks>
    Task<string?> ResolveExecutablePathAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves every distinct executable <paramref name="command"/> matches, in PATH precedence order.
    /// </summary>
    /// <remarks>
    /// The first entry is what <see cref="ResolveExecutablePathAsync"/> returns and what a shell would
    /// run; later entries are shadowed installs the user may have meant instead. Distinct means distinct
    /// target: a PATH that lists one directory several times, or symlinks pointing at one file, yield one
    /// candidate rather than several identical ones.
    ///
    /// Empty when the command resolves to nothing, so callers treat "not found" the same as they do for
    /// the single-path overload.
    /// </remarks>
    Task<IReadOnlyList<string>> ResolveExecutableCandidatesAsync(
        string command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="command"/> with <paramref name="versionArguments"/> and returns the first
    /// non-empty output line, or null when the command could not be run or printed nothing.
    /// </summary>
    Task<string?> ReadVersionAsync(
        string command,
        IReadOnlyList<string> versionArguments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks whether <paramref name="packageId"/> is installed as a Node global package, and where.
    /// </summary>
    /// <remarks>
    /// Answers where rather than only whether, because a package manager answers for the toolchain
    /// currently on PATH: the location is what lets the wizard say which one it asked. An unanswerable
    /// query reports unknown, which callers must not read as absent.
    /// </remarks>
    Task<AcpPackageQueryResult> LocateGlobalNodePackageAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks whether <paramref name="packageId"/> is installed as a uv tool, and where.
    /// </summary>
    Task<AcpPackageQueryResult> LocateGlobalUvToolAsync(
        string packageId,
        CancellationToken cancellationToken = default);
}
