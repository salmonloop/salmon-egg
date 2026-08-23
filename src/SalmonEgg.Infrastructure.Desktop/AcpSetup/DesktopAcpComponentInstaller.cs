using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Installs ACP components through their declared package manager: npm for Node packages, uv for
/// Python tools. Installs globally so the resulting launch command works from any working directory.
/// </summary>
/// <remarks>
/// Binary distributions are not installed here. They are archive downloads with checksums to verify,
/// and silently fetching and unpacking executables is a materially different trust decision from
/// invoking a package manager the user already has configured — so the wizard shows documentation for
/// those and lets the user install them deliberately.
/// </remarks>
public sealed class DesktopAcpComponentInstaller : IAcpComponentInstaller
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

    private readonly IAcpExecutableProbe _probe;

    public DesktopAcpComponentInstaller(IAcpExecutableProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public bool SupportsAutomaticInstall => true;

    public async Task<AcpComponentInstallResult> InstallAsync(
        AcpComponentDescriptor component,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (!component.SupportsAutomaticInstall)
        {
            return AcpComponentInstallResult.Failure(
                component.Id,
                exitCode: null,
                output: null,
                errorDetail: $"'{component.DisplayName}' must be installed manually.");
        }

        var (launcher, arguments) = ResolveInstallCommand(component);
        var launcherPath = await _probe
            .ResolveExecutablePathAsync(launcher, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(launcherPath))
        {
            return AcpComponentInstallResult.Failure(
                component.Id,
                exitCode: null,
                output: null,
                errorDetail: $"'{launcher}' was not found on PATH.");
        }

        var result = await AcpSetupProcessRunner
            .RunAsync(launcherPath, arguments, InstallTimeout, onOutput, cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            return AcpComponentInstallResult.Success(component.Id, result.CombinedOutput);
        }

        return AcpComponentInstallResult.Failure(
            component.Id,
            result.ExitCode,
            result.CombinedOutput,
            result.FailureDetail ?? $"'{launcher}' exited with code {result.ExitCode}.");
    }

    /// <summary>
    /// Maps a distribution to its global-install invocation.
    /// </summary>
    /// <remarks>
    /// The package coordinate is passed verbatim, including any pinned version: the catalog decides
    /// which version the wizard installs, and rewriting it here would silently disagree with what the
    /// launch command later resolves.
    /// </remarks>
    private static (string Launcher, IReadOnlyList<string> Arguments) ResolveInstallCommand(
        AcpComponentDescriptor component)
        => component.Distribution switch
        {
            AcpDistributionKind.Npx => ("npm", new[] { "install", "--global", component.PackageId }),
            AcpDistributionKind.Uvx => ("uv", new[] { "tool", "install", component.PackageId }),
            _ => throw new NotSupportedException(
                $"Distribution '{component.Distribution}' has no automatic install path.")
        };
}
