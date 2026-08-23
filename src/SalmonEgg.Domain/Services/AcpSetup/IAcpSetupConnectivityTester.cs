using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Domain.Services.AcpSetup;

/// <summary>
/// Starts a launch plan and performs an ACP handshake against it, then tears the attempt down. Used
/// by the wizard to prove a configuration works before it is saved.
/// </summary>
public interface IAcpSetupConnectivityTester
{
    Task<AcpSetupTestResult> TestAsync(
        AcpLaunchPlan launchPlan,
        CancellationToken cancellationToken = default);
}
