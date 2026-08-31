using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// Probe stub that answers from pre-set values, so installer behaviour can be tested without touching
/// PATH or launching package managers.
/// </summary>
internal sealed class StubAcpExecutableProbe : IAcpExecutableProbe
{
    private readonly Dictionary<string, string?> _resolvedPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _candidates = new(StringComparer.Ordinal);

    public bool SupportsProcessProbing { get; init; } = true;

    /// <summary>How many times a caller asked for the search to be redone.</summary>
    public int InvalidateCount { get; private set; }

    public void InvalidateSearchPaths() => InvalidateCount++;

    public List<string> ResolveRequests { get; } = new();

    public void SetResolvedPath(string command, string? path) => _resolvedPaths[command] = path;

    public Task<string?> ResolveExecutablePathAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        ResolveRequests.Add(command);
        return Task.FromResult(_resolvedPaths.TryGetValue(command, out var path) ? path : null);
    }

    /// <summary>
    /// Candidates default to the single resolved path, so a test that only cares about presence does not
    /// have to set both. Tests about shadowed installs set this explicitly.
    /// </summary>
    public void SetCandidates(string command, params string[] candidates)
        => _candidates[command] = candidates;

    public Task<IReadOnlyList<string>> ResolveExecutableCandidatesAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        ResolveRequests.Add(command);
        if (_candidates.TryGetValue(command, out var candidates))
        {
            return Task.FromResult<IReadOnlyList<string>>(candidates);
        }

        var resolved = _resolvedPaths.TryGetValue(command, out var path) ? path : null;
        return Task.FromResult<IReadOnlyList<string>>(
            resolved is null ? Array.Empty<string>() : new[] { resolved });
    }

    public Task<string?> ReadVersionAsync(
        string command,
        IReadOnlyList<string> versionArguments,
        CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<AcpPackageQueryResult> LocateGlobalPackageAsync(
        AcpDistributionKind distribution,
        string packageId,
        AcpPackageManagerCandidates packageManager,
        CancellationToken cancellationToken = default)
        => Task.FromResult(AcpPackageQueryResult.Unknown());
}

/// <summary>
/// Handshake stub recording whether the tester delegated, so the tester's own guards can be
/// distinguished from the handshake's staged failures.
/// </summary>
internal sealed class StubAcpSetupHandshakeProbe : IAcpSetupHandshakeProbe
{
    private readonly AcpSetupTestResult _result;

    public StubAcpSetupHandshakeProbe(AcpSetupTestResult result)
    {
        _result = result;
    }

    public int ProbeCount { get; private set; }

    public AcpLaunchPlan? LastPlan { get; private set; }

    public Task<AcpSetupTestResult> ProbeAsync(
        AcpLaunchPlan launchPlan,
        CancellationToken cancellationToken = default)
    {
        ProbeCount++;
        LastPlan = launchPlan;
        return Task.FromResult(_result);
    }
}

/// <summary>Shared builders for the descriptors and plans these tests exercise.</summary>
internal static class AcpSetupFixtures
{
    public static AcpComponentDescriptor NpxComponent(string packageId = "@scope/adapter")
        => new()
        {
            Id = "adapter.npx",
            DisplayName = "Npx Adapter",
            Distribution = AcpDistributionKind.Npx,
            DetectionMode = AcpComponentDetectionMode.GlobalNodePackage,
            ProbeCommand = "npx",
            PackageId = packageId
        };

    public static AcpComponentDescriptor UvxComponent(string packageId = "adapter-tool")
        => new()
        {
            Id = "adapter.uvx",
            DisplayName = "Uvx Adapter",
            Distribution = AcpDistributionKind.Uvx,
            DetectionMode = AcpComponentDetectionMode.GlobalUvTool,
            ProbeCommand = "uvx",
            PackageId = packageId
        };

    public static AcpComponentDescriptor BinaryComponent()
        => new()
        {
            Id = "adapter.binary",
            DisplayName = "Binary Adapter",
            Distribution = AcpDistributionKind.Binary,
            DetectionMode = AcpComponentDetectionMode.ExecutableOnPath,
            ProbeCommand = "binary-adapter"
        };

    public static AcpLaunchPlan Plan(string command = "npx", params string[] arguments)
        => new() { Command = command, Arguments = arguments };
}
