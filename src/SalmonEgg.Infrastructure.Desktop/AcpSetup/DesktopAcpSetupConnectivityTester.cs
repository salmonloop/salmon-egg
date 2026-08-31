using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.Desktop.AcpSetup;

/// <summary>
/// Proves a launch plan works by starting it and performing a real ACP initialize handshake, then
/// tearing the attempt down.
/// </summary>
public sealed class DesktopAcpSetupConnectivityTester : IAcpSetupConnectivityTester
{
    private readonly IAcpSetupHandshakeProbe _handshakeProbe;

    public DesktopAcpSetupConnectivityTester(IAcpSetupHandshakeProbe handshakeProbe)
    {
        _handshakeProbe = handshakeProbe ?? throw new ArgumentNullException(nameof(handshakeProbe));
    }

    public async Task<AcpSetupTestResult> TestAsync(
        AcpLaunchPlan launchPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launchPlan);

        if (string.IsNullOrWhiteSpace(launchPlan.Command))
        {
            return AcpSetupTestResult.Failure(
                AcpSetupTestStage.Validation,
                errorDetail: "Launch command is empty.");
        }

        return await _handshakeProbe.ProbeAsync(launchPlan, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Performs the ACP handshake for a launch plan. Split from the tester so the staged failure mapping can
/// be tested without launching processes, and so the ACP client dependency stays out of this assembly's
/// public surface.
/// </summary>
public interface IAcpSetupHandshakeProbe
{
    Task<AcpSetupTestResult> ProbeAsync(
        AcpLaunchPlan launchPlan,
        CancellationToken cancellationToken = default);
}
