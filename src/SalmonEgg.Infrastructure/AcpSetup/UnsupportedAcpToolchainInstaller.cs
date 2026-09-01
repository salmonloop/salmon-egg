using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.AcpSetup;

/// <summary>
/// Toolchain installer used on platforms that cannot download and run local executables. Reports
/// <see cref="SupportsAutomaticInstall"/> as false so the wizard offers the vendor's documentation instead
/// of a button, and fails any call that arrives anyway rather than reporting a phantom success.
/// </summary>
public sealed class UnsupportedAcpToolchainInstaller : IAcpToolchainInstaller
{
    private const string UnsupportedDetail =
        "Installing a toolchain requires a desktop process host and is not supported on this platform.";

    public bool SupportsAutomaticInstall => false;

    public Task<AcpToolchainInstallResult> InstallAsync(
        AcpToolchainRequirement requirement,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        return Task.FromResult(AcpToolchainInstallResult.Failure(requirement, UnsupportedDetail));
    }
}
