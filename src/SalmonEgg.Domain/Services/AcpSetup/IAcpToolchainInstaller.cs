using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Domain.Services.AcpSetup;

/// <summary>Installs a missing package-manager toolchain for the ACP setup wizard.</summary>
public interface IAcpToolchainInstaller
{
    /// <summary>True when this platform can install a toolchain without external user steps.</summary>
    bool SupportsAutomaticInstall { get; }

    /// <summary>
    /// Downloads, verifies, and installs the toolchain described by <paramref name="requirement"/>.
    /// </summary>
    Task<AcpToolchainInstallResult> InstallAsync(
        AcpToolchainRequirement requirement,
        System.Action<string>? onOutput = null,
        CancellationToken cancellationToken = default);
}
