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

    /// <summary>
    /// Makes the next detection look at the machine again rather than answer from a cached search.
    /// </summary>
    /// <remarks>
    /// Routed through here because this type owns the probe. A caller that re-detects on the user's request
    /// is asserting the machine may have changed — which is exactly what happens after the wizard's own
    /// "install the toolchain, then detect again" advice is followed.
    /// </remarks>
    public void InvalidateSearchPaths() => _probe.InvalidateSearchPaths();

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
            AcpComponentDetectionMode.GlobalNodePackage or AcpComponentDetectionMode.GlobalUvTool
                => await DetectPackageAsync(component, effectiveOverrides, cancellationToken).ConfigureAwait(false),
            _ => AcpComponentProbeResult.Undetermined(component.Id)
        };
    }

    /// <summary>
    /// Resolves whether the toolchain <paramref name="component"/> would install through exists on this
    /// machine, or null when the component needs no toolchain.
    /// </summary>
    /// <remarks>
    /// This deliberately reproduces <c>DesktopAcpComponentInstaller</c>'s command derivation step for
    /// step — the same launcher resolution, the same sibling derivation, and the same
    /// <see cref="AcpPackageManagerCandidates.Preferred"/>-only choice. Its whole purpose is to predict
    /// what an install would do, so a derivation that differed anywhere would let the wizard promise an
    /// install that then fails, or withhold one that would have worked. If the installer's derivation
    /// changes, this must change with it.
    /// </remarks>
    public async Task<AcpToolchainProbeResult?> DetectToolchainAsync(
        AcpComponentDescriptor component,
        AcpCommandOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component.RequiredToolchain is not { } requirement)
        {
            return null;
        }

        if (!_probe.SupportsProcessProbing)
        {
            return AcpToolchainProbeResult.Undetermined(requirement, ProbingUnsupportedDetail);
        }

        var effectiveOverrides = overrides ?? AcpCommandOverrides.Empty;

        // The component's own launcher is resolved first only to sharpen the sibling derivation; not
        // finding it is ordinary, because the manager may be on PATH while the component is not — which is
        // exactly the state this probe exists to recognise.
        var resolvedLauncher = await _probe
            .ResolveExecutablePathAsync(
                effectiveOverrides.Resolve(component.ProbeCommand),
                cancellationToken)
            .ConfigureAwait(false);

        var manager = AcpPackageManagerCommand
            .Resolve(component.Distribution, resolvedLauncher, effectiveOverrides)
            .Preferred;

        var managerPath = await _probe
            .ResolveExecutablePathAsync(manager, cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(managerPath)
            ? AcpToolchainProbeResult.Missing(requirement, ToolchainMissingDetail(manager))
            : AcpToolchainProbeResult.Available(requirement, managerPath);
    }

    private static string ToolchainMissingDetail(string manager)
        => $"Package manager '{manager}' was not found on PATH.";

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
    /// cannot run without <c>npx</c> on PATH, however the package query answers — so the launcher is
    /// resolved first and the query is never started when it is absent.
    /// </summary>
    /// <remarks>
    /// The manager asked is the one belonging to the launcher that was just resolved, not a bare name the
    /// inherited PATH resolves independently. Those differ exactly when the user overrode the launcher to
    /// escape a PATH that cannot see their toolchain — which is the case this whole path exists for, and
    /// where asking the wrong manager answers about a toolchain the user did not choose. See
    /// <see cref="AcpPackageManagerCommand"/>.
    /// </remarks>
    private async Task<AcpComponentProbeResult> DetectPackageAsync(
        AcpComponentDescriptor component,
        AcpCommandOverrides overrides,
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

        // Derived from the resolved path rather than from the override text: an override may name a
        // launcher by bare name or relative path, and the sibling lookup needs the real directory.
        var packageManager = AcpPackageManagerCommand.Resolve(
            component.Distribution,
            launcherPath,
            overrides);

        var packageQuery = await _probe
            .LocateGlobalPackageAsync(component.Distribution, component.PackageId, packageManager, cancellationToken)
            .ConfigureAwait(false);
        return packageQuery.IsInstalled switch
        {
            true => AcpComponentProbeResult.Installed(
                component.Id,
                launcherPath,
                version: null,
                packageLocation: packageQuery.Location),
            // The launcher answered "no", which is only authoritative for the toolchain it belongs to.
            // Naming that launcher is the difference between "not installed" and "not installed *here*",
            // which is what a user with several toolchain versions needs to see.
            false => AcpComponentProbeResult.Missing(
                component.Id,
                PackageAbsentDetail(component.PackageId, packageQuery.QueryExecutablePath)),
            null => AcpComponentProbeResult.Undetermined(
                component.Id,
                PackageQueryUnknownDetail(component.PackageId, packageQuery.QueryExecutablePath))
        };
    }

    private static string LauncherMissingDetail(string launcher)
        => $"Launcher '{launcher}' was not found on PATH.";

    private static string ExecutableMissingDetail(string command)
        => $"Command '{command}' was not found on PATH.";

    private static string PackageAbsentDetail(string packageId, string? queryExecutablePath)
        => queryExecutablePath is null
            ? $"The package manager does not list '{packageId}' among its global packages."
            : $"'{queryExecutablePath}' does not list '{packageId}' among its global packages.";

    private static string PackageQueryUnknownDetail(string packageId, string? queryExecutablePath)
        => queryExecutablePath is null
            ? $"Could not run the package manager needed to look for '{packageId}'."
            : $"Could not determine whether '{packageId}' is installed through '{queryExecutablePath}'.";
}
