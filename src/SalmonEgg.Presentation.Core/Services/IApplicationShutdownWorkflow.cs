using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>
/// Application-scoped teardown, symmetric to <see cref="IApplicationStartupWorkflow"/>.
/// </summary>
/// <remarks>
/// Startup is owned by one workflow so that no view races it; teardown needs the same single owner
/// for the same reason. Hosts signal intent to end the process here and await the result; they do
/// not reach into runtime services to persist state themselves.
/// </remarks>
public interface IApplicationShutdownWorkflow
{
    /// <summary>
    /// Completes the work that must not be lost when the process ends.
    /// </summary>
    /// <remarks>
    /// Idempotent: concurrent and repeated calls share one run, so several close paths (window
    /// close, tray exit, platform lifecycle) can all await it safely.
    /// </remarks>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
