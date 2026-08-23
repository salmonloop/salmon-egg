using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Domain.Services.AcpSetup;

/// <summary>
/// Installs an ACP component through its declared package manager. Only distributions that report
/// <see cref="AcpComponentDescriptor.SupportsAutomaticInstall"/> may be passed in.
/// </summary>
public interface IAcpComponentInstaller
{
    /// <summary>
    /// True when this platform can run installers at all. When false the wizard shows manual
    /// instructions instead of a one-click button.
    /// </summary>
    bool SupportsAutomaticInstall { get; }

    /// <summary>
    /// Installs <paramref name="component"/>, reporting installer output lines through
    /// <paramref name="onOutput"/> as they arrive so the UI can show progress.
    /// </summary>
    Task<AcpComponentInstallResult> InstallAsync(
        AcpComponentDescriptor component,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default);
}
