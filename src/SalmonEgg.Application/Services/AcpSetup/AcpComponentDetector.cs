using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Application.Services.AcpSetup;

/// <summary>
/// Resolves one component's availability by dispatching its declared detection mode to the platform
/// probe. Keeps the wizard free of detection branching, and keeps "we could not look" distinct from
/// "it is not installed" so the UI never tells the user to install something they already have.
/// </summary>
public sealed class AcpComponentDetector
{
    internal const string ProbingUnsupportedDetail = "Process probing is unavailable on this platform.";

    private readonly IAcpExecutableProbe _probe;

    public AcpComponentDetector(IAcpExecutableProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    /// <param name="overrides">
    /// User-supplied paths for commands the catalog names by executable name. Applied here so a probe
    /// answers about the executable the launch plan will actually run; see
    /// <see cref="AcpCommandOverrides"/>.
    /// </param>
    public async Task<AcpComponentProbeResult> DetectAsync(
        AcpComponentDescriptor component,
        AcpCommandOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        var effectiveOverrides = overrides ?? AcpCommandOverrides.Empty;

        if (component.DetectionMode is AcpComponentDetectionMode.None || component.IsBuiltIn)
        {
            return AcpComponentProbeResult.BuiltIn(component.Id);
        }

        if (component.DetectionMode is AcpComponentDetectionMode.Manual)
        {
            return AcpComponentProbeResult.Undetermined(component.Id);
        }

        if (!_probe.SupportsProcessProbing)
        {
            return AcpComponentProbeResult.Undetermined(component.Id, ProbingUnsupportedDetail);
        }

        return component.DetectionMode switch
        {
            AcpComponentDetectionMode.ExecutableOnPath
                => await DetectExecutableAsync(component, effectiveOverrides, cancellationToken).ConfigureAwait(false),
            AcpComponentDetectionMode.GlobalNodePackage
                => await DetectPackageAsync(
                    component,
                    effectiveOverrides,
                    _probe.IsGlobalNodePackageInstalledAsync,
                    cancellationToken).ConfigureAwait(false),
            AcpComponentDetectionMode.GlobalUvTool
                => await DetectPackageAsync(
                    component,
                    effectiveOverrides,
                    _probe.IsGlobalUvToolInstalledAsync,
                    cancellationToken).ConfigureAwait(false),
            _ => AcpComponentProbeResult.Undetermined(component.Id)
        };
    }

    private async Task<AcpComponentProbeResult> DetectExecutableAsync(
        AcpComponentDescriptor component,
        AcpCommandOverrides overrides,
        CancellationToken cancellationToken)
    {
        var command = overrides.Resolve(component.ProbeCommand);
        // Enumerated rather than resolved to a single path: a shadowed second install is invisible to a
        // shell and to the launch plan, so the wizard has to be the thing that notices it exists.
        var candidates = await _probe
            .ResolveExecutableCandidatesAsync(command, cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return AcpComponentProbeResult.Missing(component.Id, ExecutableMissingDetail(command));
        }

        var executablePath = candidates[0];
        var version = component.ProbeVersionArguments.Count == 0
            ? null
            : await _probe
                .ReadVersionAsync(command, component.ProbeVersionArguments, cancellationToken)
                .ConfigureAwait(false);

        return AcpComponentProbeResult.Installed(component.Id, executablePath, version, candidates);
    }

    /// <summary>
    /// A package-manager component is only usable when its launcher exists too: <c>npx @scope/pkg</c>
    /// cannot run without <c>npx</c> on PATH, however the package query answers. The query is passed as
    /// a factory so it is never started — and never left unawaited — when the launcher is absent.
    /// </summary>
    private async Task<AcpComponentProbeResult> DetectPackageAsync(
        AcpComponentDescriptor component,
        AcpCommandOverrides overrides,
        Func<string, CancellationToken, Task<bool?>> queryPackageAsync,
        CancellationToken cancellationToken)
    {
        var launcher = overrides.Resolve(component.ProbeCommand);
        var launcherPath = await _probe
            .ResolveExecutablePathAsync(launcher, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(launcherPath))
        {
            return AcpComponentProbeResult.Missing(component.Id, LauncherMissingDetail(launcher));
        }

        var isInstalled = await queryPackageAsync(component.PackageId, cancellationToken).ConfigureAwait(false);
        return isInstalled switch
        {
            true => AcpComponentProbeResult.Installed(component.Id, launcherPath, version: null),
            // The launcher answered "no", which is only authoritative for the toolchain it belongs to.
            // Naming that launcher is the difference between "not installed" and "not installed *here*",
            // which is what a user with several toolchain versions needs to see.
            false => AcpComponentProbeResult.Missing(
                component.Id,
                PackageAbsentDetail(component.PackageId, launcherPath)),
            null => AcpComponentProbeResult.Undetermined(component.Id)
        };
    }

    private static string LauncherMissingDetail(string launcher)
        => $"Launcher '{launcher}' was not found on PATH.";

    private static string ExecutableMissingDetail(string command)
        => $"Command '{command}' was not found on PATH.";

    private static string PackageAbsentDetail(string packageId, string launcherPath)
        => $"'{launcherPath}' does not list '{packageId}' among its global packages.";
}
