using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Cli;

namespace SalmonEgg.Domain.Services;

/// <summary>
/// Creates or removes the command's entry on PATH on platforms where the app, not an installer, owns it.
/// </summary>
/// <remarks>
/// Implementations must refuse on platforms whose installer owns the registration. Both writing the entry
/// and the installer writing it would leave two owners for one file, and the loser of that race is the
/// uninstall: whichever one runs second leaves a command behind that points at a deleted app.
/// </remarks>
public interface ICliCommandLinkService
{
    /// <summary>True when this platform lets the app own the registration.</summary>
    bool IsSupported { get; }

    Task<CliCommandLinkResult> LinkAsync(CancellationToken cancellationToken = default);

    Task<CliCommandLinkResult> UnlinkAsync(CancellationToken cancellationToken = default);
}
