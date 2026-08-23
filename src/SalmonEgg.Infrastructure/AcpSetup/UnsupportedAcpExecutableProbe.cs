using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.AcpSetup;

/// <summary>
/// Probe used on platforms with no child-process host (WASM in particular).
/// </summary>
/// <remarks>
/// Reports <see cref="SupportsProcessProbing"/> as false and answers every query with "unknown" rather
/// than "absent", so the wizard shows an undetermined state and manual instructions instead of telling
/// the user to install components it never actually looked for.
/// </remarks>
public sealed class UnsupportedAcpExecutableProbe : IAcpExecutableProbe
{
    public bool SupportsProcessProbing => false;

    public Task<string?> ResolveExecutablePathAsync(
        string command,
        CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<string?> ReadVersionAsync(
        string command,
        IReadOnlyList<string> versionArguments,
        CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<bool?> IsGlobalNodePackageInstalledAsync(
        string packageId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<bool?>(null);

    public Task<bool?> IsGlobalUvToolInstalledAsync(
        string packageId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<bool?>(null);
}
