using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.AcpSetup;

/// <summary>
/// Connectivity tester used on platforms that cannot start child processes (WASM in particular).
/// </summary>
/// <remarks>
/// Fails at <see cref="AcpSetupTestStage.CommandResolution"/> rather than reporting success: a stdio
/// launch plan genuinely cannot run here, and a green test would let the wizard save a profile that
/// can never connect on this platform.
/// </remarks>
public sealed class UnsupportedAcpSetupConnectivityTester : IAcpSetupConnectivityTester
{
    private const string UnsupportedDetail =
        "Testing a stdio launch plan requires a desktop process host and is not supported on this platform.";

    public Task<AcpSetupTestResult> TestAsync(
        AcpLaunchPlan launchPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launchPlan);

        return Task.FromResult(AcpSetupTestResult.Failure(
            AcpSetupTestStage.CommandResolution,
            errorDetail: UnsupportedDetail));
    }
}
