using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.AcpSetup;

/// <summary>
/// Installer used on platforms that cannot run package managers. Reports
/// <see cref="SupportsAutomaticInstall"/> as false so the wizard offers documentation instead of a
/// one-click button, and fails any call that arrives anyway rather than reporting a phantom success.
/// </summary>
public sealed class UnsupportedAcpComponentInstaller : IAcpComponentInstaller
{
    private const string UnsupportedDetail =
        "Automatic installation requires a desktop process host and is not supported on this platform.";

    public bool SupportsAutomaticInstall => false;

    public Task<AcpComponentInstallResult> InstallAsync(
        AcpComponentDescriptor component,
        Action<string>? onOutput = null,
        AcpCommandOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        return Task.FromResult(AcpComponentInstallResult.Failure(
            component.Id,
            exitCode: null,
            output: null,
            errorDetail: UnsupportedDetail));
    }
}
