using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Cli;

namespace SalmonEgg.Domain.Services;

/// <summary>
/// Reports whether a shell on this machine would reach this app's <c>salmon-egg</c> command.
/// </summary>
/// <remarks>
/// Observation, not configuration: the answer lives in the machine's PATH and in whatever the resolved
/// executable says about itself, both of which an installer, another installation, or the user can change
/// without this app knowing. So there is nothing to cache and nothing to persist — each call resolves again.
/// </remarks>
public interface ICliCommandRegistrationInspector
{
    Task<CliCommandRegistration> InspectAsync(CancellationToken cancellationToken = default);
}
