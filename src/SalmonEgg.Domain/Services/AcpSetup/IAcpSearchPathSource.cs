using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services.AcpSetup;

/// <summary>
/// Supplies the directories an executable may be found in, which is broader than the PATH this process
/// inherited.
/// </summary>
/// <remarks>
/// A GUI-launched desktop process inherits the session environment rather than the one a shell profile
/// builds, so a version-manager toolchain (nvm, fnm, volta, asdf, mise) is invisible to it and every
/// catalog component probes as missing. The inherited PATH is therefore a floor, not the answer.
///
/// This is a seam rather than a single implementation because the two ways of widening the search answer
/// different questions and neither subsumes the other:
/// <list type="bullet">
/// <item>Asking the user's login shell yields the toolchain they have <em>activated</em> — the one their
/// terminal would use. It is the only route to a version manager implemented as a shell function, since
/// such a manager has no executable on disk to find.</item>
/// <item>Scanning a version manager's on-disk layout yields <em>every</em> version installed, which the
/// shell cannot report because a manager puts only the current one on PATH.</item>
/// </list>
///
/// Order is meaningful: sources are consulted in registration order and their directories keep that
/// order, so the first match stays the one a shell would run.
/// </remarks>
public interface IAcpSearchPathSource
{
    /// <summary>
    /// Returns additional directories to search, most preferred first.
    /// </summary>
    /// <remarks>
    /// Empty rather than throwing when this source has nothing to contribute or could not answer. A
    /// source that fails must not deny the search the directories other sources found, and must never
    /// prevent the inherited PATH from being used.
    /// </remarks>
    Task<IReadOnlyList<string>> GetSearchDirectoriesAsync(CancellationToken cancellationToken = default);
}
