using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    Task<string?> ResolveExecutablePathAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="command"/> with <paramref name="versionArguments"/> and returns the first
    /// non-empty output line, or null when the command could not be run or printed nothing.
    /// </summary>
    Task<string?> ReadVersionAsync(
        string command,
        IReadOnlyList<string> versionArguments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when <paramref name="packageId"/> is installed as a Node global package. Null when the
    /// query itself could not run, which callers must not read as "absent".
    /// </summary>
    Task<bool?> IsGlobalNodePackageInstalledAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when <paramref name="packageId"/> is installed as a uv tool. Null when the query itself
    /// could not run.
    /// </summary>
    Task<bool?> IsGlobalUvToolInstalledAsync(
        string packageId,
        CancellationToken cancellationToken = default);
}
