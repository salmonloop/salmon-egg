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

    public async Task<AcpComponentProbeResult> DetectAsync(
        AcpComponentDescriptor component,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

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
                => await DetectExecutableAsync(component, cancellationToken).ConfigureAwait(false),
            AcpComponentDetectionMode.GlobalNodePackage
                => await DetectPackageAsync(
                    component,
                    _probe.IsGlobalNodePackageInstalledAsync,
                    cancellationToken).ConfigureAwait(false),
            AcpComponentDetectionMode.GlobalUvTool
                => await DetectPackageAsync(
                    component,
                    _probe.IsGlobalUvToolInstalledAsync,
                    cancellationToken).ConfigureAwait(false),
            _ => AcpComponentProbeResult.Undetermined(component.Id)
        };
    }

    private async Task<AcpComponentProbeResult> DetectExecutableAsync(
        AcpComponentDescriptor component,
        CancellationToken cancellationToken)
    {
        var executablePath = await _probe
            .ResolveExecutablePathAsync(component.ProbeCommand, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return AcpComponentProbeResult.Missing(component.Id);
        }

        var version = component.ProbeVersionArguments.Count == 0
            ? null
            : await _probe
                .ReadVersionAsync(component.ProbeCommand, component.ProbeVersionArguments, cancellationToken)
                .ConfigureAwait(false);

        return AcpComponentProbeResult.Installed(component.Id, executablePath, version);
    }

    /// <summary>
    /// A package-manager component is only usable when its launcher exists too: <c>npx @scope/pkg</c>
    /// cannot run without <c>npx</c> on PATH, however the package query answers. The query is passed as
    /// a factory so it is never started — and never left unawaited — when the launcher is absent.
    /// </summary>
    private async Task<AcpComponentProbeResult> DetectPackageAsync(
        AcpComponentDescriptor component,
        Func<string, CancellationToken, Task<bool?>> queryPackageAsync,
        CancellationToken cancellationToken)
    {
        var launcherPath = await _probe
            .ResolveExecutablePathAsync(component.ProbeCommand, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(launcherPath))
        {
            return AcpComponentProbeResult.Missing(component.Id, LauncherMissingDetail(component.ProbeCommand));
        }

        var isInstalled = await queryPackageAsync(component.PackageId, cancellationToken).ConfigureAwait(false);
        return isInstalled switch
        {
            true => AcpComponentProbeResult.Installed(component.Id, launcherPath, version: null),
            false => AcpComponentProbeResult.Missing(component.Id),
            null => AcpComponentProbeResult.Undetermined(component.Id)
        };
    }

    private static string LauncherMissingDetail(string launcher)
        => $"Launcher '{launcher}' was not found on PATH.";
}
