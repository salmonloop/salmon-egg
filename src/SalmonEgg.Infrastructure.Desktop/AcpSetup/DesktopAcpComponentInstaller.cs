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

    /// <summary>
    /// Advice key for an install that could not start because the toolchain is absent. A key rather than a
    /// sentence: this layer knows the cause, and the presentation layer owns the words.
    /// </summary>
    internal const string ToolchainMissingRemediationKey = "AcpSetup_Install_ToolchainMissing";

    private readonly IAcpExecutableProbe _probe;

    public DesktopAcpComponentInstaller(IAcpExecutableProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public bool SupportsAutomaticInstall => true;

    public async Task<AcpComponentInstallResult> InstallAsync(
        AcpComponentDescriptor component,
        Action<string>? onOutput = null,
        AcpCommandOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (!component.HasAutomaticInstallPath)
        {
            return AcpComponentInstallResult.Failure(
                component.Id,
                exitCode: null,
                output: null,
                errorDetail: $"'{component.DisplayName}' must be installed manually.");
        }

        // The component's own launcher is resolved first so the manager can be derived from a real
        // directory: an override may name it by bare name or relative path, which no sibling lookup can
        // use. A launcher that does not resolve is not fatal here — the manager may still be on PATH —
        // so this only sharpens the derivation.
        var resolvedLauncher = await _probe
            .ResolveExecutablePathAsync(
                (overrides ?? AcpCommandOverrides.Empty).Resolve(component.ProbeCommand),
                cancellationToken)
            .ConfigureAwait(false);

        var (launcher, arguments) = ResolveInstallCommand(component, resolvedLauncher, overrides);
        var launcherPath = await _probe
            .ResolveExecutablePathAsync(launcher, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(launcherPath))
        {
            // The absent executable is a package manager, so the user's problem is a missing toolchain
            // rather than a missing command. Naming the toolchain — and carrying a key the presentation
            // layer localizes — is the difference between advice they can act on and an untranslated
            // sentence about a program they may never have installed deliberately.
            return AcpComponentInstallResult.Failure(
                component.Id,
                exitCode: null,
                output: null,
                errorDetail: $"'{launcher}' was not found on PATH.",
                remediationKey: ToolchainMissingRemediationKey,
                missingToolchainName: component.RequiredToolchain?.DisplayName);
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
    ///
    /// The package manager is resolved through the same rule detection uses, so an install lands in the
    /// toolchain the wizard asked about. Installing into a different one would report success and leave
    /// the next probe — and the launch — looking at a toolchain that still does not have the package.
    /// Only the preferred candidate is used: an install has to choose one toolchain, and falling back to
    /// a bare name would write into whichever one PATH happens to resolve.
    /// </remarks>
    private static (string Launcher, IReadOnlyList<string> Arguments) ResolveInstallCommand(
        AcpComponentDescriptor component,
        string? resolvedLauncherPath,
        AcpCommandOverrides? overrides)
    {
        var launcher = AcpPackageManagerCommand
            .Resolve(component.Distribution, resolvedLauncherPath, overrides)
            .Preferred;

        return component.Distribution switch
        {
            AcpDistributionKind.Npx => (launcher, new[] { "install", "--global", component.PackageId }),
            AcpDistributionKind.Uvx => (launcher, new[] { "tool", "install", component.PackageId }),
            _ => throw new NotSupportedException(
                $"Distribution '{component.Distribution}' has no automatic install path.")
        };
    }
}
